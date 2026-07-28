using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Catalogue;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class CatalogueService(ApplicationDbContext db, IFileStorageService fileStorageService) : ICatalogueService
{
    public async Task<PagedResult<CatalogueEntryDto>> SearchAsync(CatalogueSearchRequest request, CancellationToken cancellationToken = default)
    {
        var query = db.Publications
            .Include(p => p.PublicationContainer).ThenInclude(c => c.Student).ThenInclude(s => s.StudentProfile).ThenInclude(sp => sp!.Department)
            .Include(p => p.PublicationContainer).ThenInclude(c => c.AssignedSupervisor)
            .Include(p => p.Keywords)
            .Include(p => p.ResearchAreas)
            .Where(p => p.IsPublished);

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            query = query.Where(p => p.Title.Contains(request.Query) || p.Abstract.Contains(request.Query));
        }
        if (!string.IsNullOrWhiteSpace(request.Author))
        {
            query = query.Where(p => (p.PublicationContainer.Student.FirstName + " " + p.PublicationContainer.Student.LastName).Contains(request.Author));
        }
        if (!string.IsNullOrWhiteSpace(request.Supervisor))
        {
            query = query.Where(p => p.PublicationContainer.AssignedSupervisor != null &&
                (p.PublicationContainer.AssignedSupervisor.FirstName + " " + p.PublicationContainer.AssignedSupervisor.LastName).Contains(request.Supervisor));
        }
        if (request.Year is not null)
        {
            query = query.Where(p => p.PublicationYear == request.Year);
        }
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            query = query.Where(p => p.Keywords.Any(k => k.Name == request.Keyword));
        }
        if (!string.IsNullOrWhiteSpace(request.PublicationType))
        {
            query = query.Where(p => p.PublicationType == request.PublicationType);
        }
        if (!string.IsNullOrWhiteSpace(request.Department))
        {
            query = query.Where(p => p.PublicationContainer.Student.StudentProfile != null &&
                p.PublicationContainer.Student.StudentProfile.Department.Name == request.Department);
        }
        if (!string.IsNullOrWhiteSpace(request.ResearchArea))
        {
            query = query.Where(p => p.ResearchAreas.Any(r => r.Name == request.ResearchArea));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(p => p.PublishedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(p => ToDto(p))
            .ToListAsync(cancellationToken);

        return new PagedResult<CatalogueEntryDto>(items, request.Page, request.PageSize, totalCount);
    }

    public async Task<CatalogueEntryDto> GetByIdAsync(Guid publicationId, CancellationToken cancellationToken = default)
    {
        var publication = await LoadPublishedAsync(publicationId, cancellationToken);
        return ToDto(publication);
    }

    public async Task<(Stream Content, string FileName)> DownloadAsync(Guid publicationId, CancellationToken cancellationToken = default)
    {
        var publication = await LoadPublishedAsync(publicationId, cancellationToken);

        var latestVersion = await db.PublicationVersions
            .Where(v => v.PublicationId == publicationId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(PublicationVersion), publicationId);

        var stream = await fileStorageService.OpenReadAsync(latestVersion.FilePath, cancellationToken);
        return (stream, $"{publication.Title}.pdf");
    }

    public async Task<CitationDto> GetCitationAsync(Guid publicationId, CancellationToken cancellationToken = default)
    {
        var publication = await LoadPublishedAsync(publicationId, cancellationToken);
        var year = publication.PublicationYear ?? publication.PublishedAt?.Year ?? DateTime.UtcNow.Year;
        var author = publication.PublicationContainer.Student;
        var authorApa = $"{author.LastName}, {author.FirstName[..1]}.";
        var authorMla = $"{author.LastName}, {author.FirstName}";

        var apa = $"{authorApa} ({year}). {publication.Title}. Auckland Institute of Studies.";
        var mla = $"{authorMla}. \"{publication.Title}.\" Auckland Institute of Studies, {year}.";

        return new CitationDto(apa, mla);
    }

    private async Task<Publication> LoadPublishedAsync(Guid publicationId, CancellationToken cancellationToken)
    {
        return await db.Publications
            .Include(p => p.PublicationContainer).ThenInclude(c => c.Student).ThenInclude(s => s.StudentProfile).ThenInclude(sp => sp!.Department)
            .Include(p => p.PublicationContainer).ThenInclude(c => c.AssignedSupervisor)
            .Include(p => p.Keywords)
            .Include(p => p.ResearchAreas)
            .FirstOrDefaultAsync(p => p.Id == publicationId && p.IsPublished, cancellationToken)
            ?? throw new NotFoundException(nameof(Publication), publicationId);
    }

    private static CatalogueEntryDto ToDto(Publication p) => new(
        p.Id, p.Title, p.Abstract,
        p.PublicationContainer.Student.FirstName + " " + p.PublicationContainer.Student.LastName,
        p.PublicationContainer.AssignedSupervisor == null ? null :
            p.PublicationContainer.AssignedSupervisor.FirstName + " " + p.PublicationContainer.AssignedSupervisor.LastName,
        p.Keywords.Select(k => k.Name).ToList(),
        p.PublicationType, p.PublicationYear,
        p.PublicationContainer.Student.StudentProfile != null ? p.PublicationContainer.Student.StudentProfile.Department.Name : null,
        p.ResearchAreas.Select(r => r.Name).ToList());
}
