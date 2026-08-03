using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Users;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class UserProfileFactory(ApplicationDbContext db) : IUserProfileFactory
{
    public async Task EnsureForRoleAsync(ApplicationUser user, CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        switch (request.Role)
        {
            case RoleNames.Student:
                if (await db.StudentProfiles.AnyAsync(s => s.UserId == user.Id, cancellationToken)) break;
                RequireDepartment(request);
                db.StudentProfiles.Add(new StudentProfile
                {
                    UserId = user.Id,
                    DepartmentId = request.DepartmentId!.Value,
                    StudentIdNumber = request.StudentIdNumber ?? string.Empty,
                    Programme = request.Programme ?? string.Empty,
                    Cohort = request.Cohort ?? string.Empty,
                    ResearchAreas = request.ResearchAreaIds is { Count: > 0 }
                        ? await db.ResearchAreas.Where(r => request.ResearchAreaIds.Contains(r.Id)).ToListAsync(cancellationToken)
                        : []
                });
                break;

            case RoleNames.Supervisor:
                // The departments come first: they are what the role needs, and a supervisor
                // recorded with none is one nobody can place.
                await SetMembershipsAsync(user, request, cancellationToken);

                if (await db.SupervisorProfiles.AnyAsync(s => s.UserId == user.Id, cancellationToken)) break;
                db.SupervisorProfiles.Add(new SupervisorProfile
                {
                    UserId = user.Id,
                    AreasOfExpertise = request.AreasOfExpertise,
                    ResearchInterests = request.ResearchInterests
                });
                break;

            case RoleNames.Coordinator:
                if (await db.CoordinatorProfiles.AnyAsync(c => c.UserId == user.Id, cancellationToken)) break;
                RequireDepartment(request);
                db.CoordinatorProfiles.Add(new CoordinatorProfile
                {
                    UserId = user.Id,
                    DepartmentId = request.DepartmentId!.Value
                });
                break;

            case RoleNames.HeadOfDepartment:
                if (await db.HeadOfDepartmentProfiles.AnyAsync(h => h.UserId == user.Id, cancellationToken)) break;
                RequireDepartment(request);

                // A department has one head. Checked here rather than by a unique index alone so
                // the administrator gets a sentence instead of a database error.
                if (await db.HeadOfDepartmentProfiles.AnyAsync(h => h.DepartmentId == request.DepartmentId, cancellationToken))
                {
                    throw new ConflictException("This department already has a Head of Department assigned.");
                }
                db.HeadOfDepartmentProfiles.Add(new HeadOfDepartmentProfile
                {
                    UserId = user.Id,
                    DepartmentId = request.DepartmentId!.Value
                });
                break;

            case RoleNames.Reviewer:
            case RoleNames.ExternalCommitteeMember:
                // A reviewer belongs to departments; an external member belongs to another
                // institution, so asking them for one would be asking the wrong question.
                if (request.Role == RoleNames.Reviewer)
                {
                    await SetMembershipsAsync(user, request, cancellationToken);
                }
                else
                {
                    await ClearMembershipsAsync(user, cancellationToken);
                }

                var committeeType = request.Role == RoleNames.Reviewer
                    ? CommitteeMemberRoleType.Reviewer
                    : CommitteeMemberRoleType.External;

                var existing = await db.CommitteeMemberProfiles
                    .FirstOrDefaultAsync(c => c.UserId == user.Id, cancellationToken);

                if (existing is not null)
                {
                    // One profile, two roles. Moving between reviewer and external is a change of
                    // what they are, not a second profile, and the type is what committee
                    // composition is counted by, so it has to follow the role.
                    existing.Type = committeeType;
                    break;
                }

                db.CommitteeMemberProfiles.Add(new CommitteeMemberProfile
                {
                    UserId = user.Id,
                    Type = committeeType,
                    Affiliation = request.Affiliation
                });
                break;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void RequireDepartment(CreateUserRequest request)
    {
        if (request.DepartmentId is null)
        {
            throw new BusinessRuleException($"A department is required for the '{request.Role}' role.");
        }
    }

    /// <summary>
    /// Records which departments somebody belongs to, for the roles that can be in several.
    ///
    /// One list in, one list stored: what is passed becomes the whole of their membership, so
    /// removing a department is saying the shorter list rather than asking for a deletion. A
    /// single DepartmentId is read as a list of one, because a caller who has one department to
    /// give should not have to know that the field is plural.
    /// </summary>
    private async Task SetMembershipsAsync(
        ApplicationUser user, CreateUserRequest request, CancellationToken cancellationToken)
    {
        var wanted = request.DepartmentIds is { Count: > 0 }
            ? request.DepartmentIds.Distinct().ToList()
            : request.DepartmentId is { } single ? [single] : new List<Guid>();

        if (wanted.Count == 0)
        {
            throw new BusinessRuleException($"At least one department is required for the '{request.Role}' role.");
        }

        var known = await db.Departments
            .Where(d => wanted.Contains(d.Id))
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        if (known.Count != wanted.Count)
        {
            throw new BusinessRuleException("One of the departments given does not exist.");
        }

        var existing = await db.DepartmentMemberships
            .Where(m => m.UserId == user.Id)
            .ToListAsync(cancellationToken);

        db.DepartmentMemberships.RemoveRange(existing.Where(m => !wanted.Contains(m.DepartmentId)));

        foreach (var departmentId in wanted.Where(id => existing.All(m => m.DepartmentId != id)))
        {
            db.DepartmentMemberships.Add(new DepartmentMembership { UserId = user.Id, DepartmentId = departmentId });
        }
    }

    /// <summary>For the role that belongs to no department at all.</summary>
    private async Task ClearMembershipsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var existing = await db.DepartmentMemberships
            .Where(m => m.UserId == user.Id)
            .ToListAsync(cancellationToken);

        db.DepartmentMemberships.RemoveRange(existing);
    }
}
