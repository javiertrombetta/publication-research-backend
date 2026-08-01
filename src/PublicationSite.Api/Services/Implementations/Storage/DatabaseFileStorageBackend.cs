using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations.Storage;

/// <summary>
/// Files as rows.
///
/// The whole file is read into memory to store it and again to serve it, which is the honest cost
/// of this option and the reason it is not the default. The size limit above decides how bad that
/// is: at the fifty megabytes shipped as the ceiling it is fine, and an installation that raises
/// the ceiling a long way should be using a bucket instead.
/// </summary>
public class DatabaseFileStorageBackend(ApplicationDbContext db) : IFileStorageBackend
{
    public string Name => "database";

    public async Task<string> WriteAsync(
        Stream content, string subFolder, string storedFileName, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        var row = new StoredFileContent
        {
            SubFolder = subFolder,
            FileName = storedFileName,
            Content = buffer.ToArray(),
            Length = buffer.Length
        };

        db.StoredFileContents.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        return row.Id.ToString();
    }

    public async Task<Stream> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(path, out var id)) throw new NotFoundException("File", path);

        var content = await db.StoredFileContents
            .Where(f => f.Id == id)
            .Select(f => f.Content)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("File", path);

        return new MemoryStream(content, writable: false);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(path, out var id)) return;

        await db.StoredFileContents.Where(f => f.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        // The table is in the same database everything else already uses, so the only thing worth
        // asking is whether it is reachable and shaped as expected.
        try
        {
            await db.StoredFileContents.Take(1).CountAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            throw new BusinessRuleException($"Could not read the stored files table. {ex.Message}");
        }
    }
}
