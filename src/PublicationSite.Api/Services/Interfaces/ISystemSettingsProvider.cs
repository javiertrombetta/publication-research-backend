namespace PublicationSite.Api.Services.Interfaces;

/// <summary>
/// Reads administrator-configurable settings, typed and with a default for every key.
///
/// Separate from <see cref="ISystemSettingService"/> on purpose: that one is the administrator's
/// write side, this one is what the rest of the application reads. Settings are consulted on
/// paths as hot as signing in and validating a password, so values are cached in memory and the
/// cache is dropped whenever a setting is written.
/// </summary>
public interface ISystemSettingsProvider
{
    Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default);

    Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken = default);

    Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken = default);

    /// <summary>Drops the cache. Called after any write so the next read sees the new value.</summary>
    void Invalidate();
}
