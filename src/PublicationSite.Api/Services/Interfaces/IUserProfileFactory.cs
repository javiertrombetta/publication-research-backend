using PublicationSite.Api.DTOs.Users;
using PublicationSite.Api.Entities;

namespace PublicationSite.Api.Services.Interfaces;

/// <summary>
/// Creates the profile row a role needs: a Supervisor has a department, a committee member has an
/// affiliation, a Student has a programme and a cohort.
///
/// Shared because there are three ways an account comes to hold a role: an administrator creating
/// one outright, someone accepting an invitation, and an administrator granting a role to an
/// account that already exists. All three have to produce the same shape, and a second copy of the
/// rules would drift. A role handled in one and forgotten in the others leaves an account holding a
/// role but unable to take part in anything it is for.
/// </summary>
public interface IUserProfileFactory
{
    /// <summary>
    /// Gives the user whatever profile their role needs, unless they already have one.
    ///
    /// Idempotent: called again for a role the user already holds, it leaves the existing profile
    /// alone rather than replacing it, so re-granting a role never discards someone's department
    /// or areas of expertise.
    ///
    /// Profiles are deliberately never deleted when a role is taken away. A Publication Container
    /// points at the Coordinator Profile assigned to it, so removing one would either be refused
    /// by the database or detach publications from the coordinator who handled them. A profile
    /// left behind is inert: the role decides what someone may do, and the queries that look for
    /// people to assign check the role rather than the presence of a profile.
    /// </summary>
    Task EnsureForRoleAsync(ApplicationUser user, CreateUserRequest request, CancellationToken cancellationToken = default);
}
