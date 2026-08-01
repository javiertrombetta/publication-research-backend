using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PublicationSite.Api.Data;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

/// <summary>
/// Loads the whole settings table in one query and keeps it in memory. There are a few dozen rows
/// at most and they change once in a blue moon, so caching the lot is cheaper and simpler than a
/// query per key, and the paths that read settings (sign-in, password validation, sending a
/// notification) cannot afford a round trip each.
/// </summary>
public class SystemSettingsProvider(ApplicationDbContext db, IMemoryCache cache) : ISystemSettingsProvider
{
    private const string CacheKey = "system-settings";

    public async Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);
        return settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    public async Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken = default)
    {
        var raw = await GetStringAsync(key, cancellationToken);

        // A value that cannot be parsed falls back rather than throwing: a malformed row must not
        // be able to take sign-in down, and the default is always a safe configuration.
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    public async Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken = default)
    {
        var raw = await GetStringAsync(key, cancellationToken);
        return bool.TryParse(raw, out var value) ? value : fallback;
    }

    public void Invalidate() => cache.Remove(CacheKey);

    private async Task<IReadOnlyDictionary<string, string>> LoadAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyDictionary<string, string>? cached) && cached is not null)
        {
            return cached;
        }

        var settings = await db.SystemSettings
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.Ordinal, cancellationToken);

        // An expiry as well as explicit invalidation: invalidation covers this process, the
        // expiry covers a second one writing to the same database.
        cache.Set(CacheKey, (IReadOnlyDictionary<string, string>)settings, TimeSpan.FromMinutes(5));

        return settings;
    }
}
