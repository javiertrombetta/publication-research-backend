using PublicationSite.Api.Entities;

namespace PublicationSite.Api.Services.Interfaces;

public interface IContainerAccessService
{
    /// <summary>
    /// True if userId is Admin, the owning Student, the assigned Coordinator or
    /// Supervisor, the Head of Department for the student's department, or a member
    /// of the container's evaluation Committee.
    /// </summary>
    Task<bool> CanAccessAsync(Guid publicationContainerId, Guid userId);

    Task EnsureAccessAsync(Guid publicationContainerId, Guid userId);

    /// <summary>
    /// The same rule, narrowing a set rather than answering about one.
    ///
    /// A screen that fills in a page of ten publications used to ask this question ten times, and
    /// once per row is exactly the shape that turns one screen into seventy queries. Expressed as a
    /// filter it composes into whatever query is being run anyway, so a page costs the same as a
    /// row. It is the one definition either way: a second copy of who may read what is a second
    /// copy that can drift.
    /// </summary>
    IQueryable<PublicationContainer> WhereReadableBy(IQueryable<PublicationContainer> containers, Guid userId);
}
