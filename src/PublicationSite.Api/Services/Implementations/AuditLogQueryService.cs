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
    public async Task<PagedResult<AuditLogEntryDto>> GetAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        var filtered = Filter(query);
        var totalCount = await filtered.CountAsync(cancellationToken);

        var items = await filtered
            .OrderByDescending(a => a.Timestamp)
            .Skip((query.SafePage - 1) * query.SafePageSize)
            .Take(query.SafePageSize)
            .Select(ToDto)
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogEntryDto>(items, query.SafePage, query.SafePageSize, totalCount);
    }

    public async Task<byte[]> ExportCsvAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        var items = await Filter(query).OrderByDescending(a => a.Timestamp).Select(ToDto).ToListAsync(cancellationToken);

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
