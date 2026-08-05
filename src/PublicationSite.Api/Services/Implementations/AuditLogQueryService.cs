using System.Linq.Expressions;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.AuditLog;
using PublicationSite.Api.DTOs.Catalogue;
using PublicationSite.Api.Services.Interfaces;
using PublicationSite.Api.DTOs.Common;

namespace PublicationSite.Api.Services.Implementations;

public class AuditLogQueryService(ApplicationDbContext db) : IAuditLogQueryService
{
    /// <summary>
    /// What the trail can be ordered by, one per column of the screen that reads it.
    ///
    /// The change column is missing on purpose: it is two values drawn as one, and there is no
    /// single thing to compare. Its neighbours are what anybody actually orders this by.
    /// </summary>
    private static readonly Dictionary<string, Expression<Func<Entities.AuditLogEntry, object?>>> TrailSorts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["when"] = a => a.Timestamp,
            ["who"] = a => a.ActorUser.LastName,
            ["action"] = a => a.ActionType,
            ["entity"] = a => a.EntityType,
            ["comments"] = a => a.Comments
        };

    public async Task<PagedResult<AuditLogEntryDto>> GetAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        var filtered = Filter(query);
        var totalCount = await filtered.CountAsync(cancellationToken);

        var items = await Order(filtered, query)
            .Skip((query.SafePage - 1) * query.SafePageSize)
            .Take(query.SafePageSize)
            .Select(ToDto)
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogEntryDto>(items, query.SafePage, query.SafePageSize, totalCount);
    }

    /// <summary>
    /// Newest first unless a column was asked for, and then in the order the reader chose. The
    /// export takes the same ordering as the screen, because it is meant to be the thing on the
    /// screen in a file: handing somebody a CSV sorted differently from the page they exported it
    /// from is handing them a second document to reconcile.
    /// </summary>
    private static IOrderedQueryable<Entities.AuditLogEntry> Order(
        IQueryable<Entities.AuditLogEntry> query, AuditLogQuery request) =>
        request.SortBy is not null && TrailSorts.TryGetValue(request.SortBy, out var key)
            ? request.SortDescending ? query.OrderByDescending(key) : query.OrderBy(key)
            : query.OrderByDescending(a => a.Timestamp);

    public async Task<byte[]> ExportCsvAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        var items = await Order(Filter(query), query).Select(ToDto).ToListAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,Actor,OnBehalfOf,ActionType,EntityType,EntityId,Comments");
        foreach (var item in items)
        {
            sb.AppendLine(string.Join(',', [
                item.Timestamp.ToString("O"),
                CsvEscape(item.ActorName),
                CsvEscape(item.OnBehalfOfName ?? string.Empty),
                CsvEscape(item.ActionType),
                CsvEscape(item.EntityType),
                item.EntityId?.ToString() ?? string.Empty,
                CsvEscape(item.Comments ?? string.Empty)
            ]));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private IQueryable<Entities.AuditLogEntry> Filter(AuditLogQuery query)
    {
        var result = db.AuditLogEntries.AsQueryable();

        if (query.UserId is not null) result = result.Where(a => a.ActorUserId == query.UserId || a.OnBehalfOfUserId == query.UserId);
        if (!string.IsNullOrWhiteSpace(query.EntityType)) result = result.Where(a => a.EntityType == query.EntityType);
        if (query.From is not null) result = result.Where(a => a.Timestamp >= query.From);
        if (query.To is not null) result = result.Where(a => a.Timestamp <= query.To);

        return result;
    }

    private static string CsvEscape(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    /// <summary>
    /// An expression rather than a method, so EF Core translates it into the SQL SELECT and joins
    /// the actor. Called as a method it was evaluated in memory instead, against an entity whose
    /// ActorUser navigation had never been loaded. Every request threw a NullReferenceException.
    /// </summary>
    private static readonly Expression<Func<Entities.AuditLogEntry, AuditLogEntryDto>> ToDto = a =>
        new AuditLogEntryDto(
            a.Id,
            a.ActorUser.FirstName + " " + a.ActorUser.LastName,
            a.OnBehalfOfUser == null ? null : a.OnBehalfOfUser.FirstName + " " + a.OnBehalfOfUser.LastName,
            a.ActionType, a.EntityType, a.EntityId, a.PreviousValue, a.NewValue, a.Comments, a.Timestamp);
}
