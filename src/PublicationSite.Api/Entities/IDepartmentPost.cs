namespace PublicationSite.Api.Entities;

/// <summary>
/// A job held in exactly one department: heading it, or coordinating it.
///
/// Named so the one piece of code that moves somebody between departments can be written once
/// rather than once per profile. Supervising and reviewing are deliberately not here: those are
/// attachments and can be to several departments at a time, which is a different shape.
/// </summary>
public interface IDepartmentPost
{
    Guid UserId { get; }
    Guid DepartmentId { get; set; }
}
