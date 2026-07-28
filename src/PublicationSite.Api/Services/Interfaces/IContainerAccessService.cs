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
}
