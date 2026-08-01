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
                if (await db.SupervisorProfiles.AnyAsync(s => s.UserId == user.Id, cancellationToken)) break;
                RequireDepartment(request);
                db.SupervisorProfiles.Add(new SupervisorProfile
                {
                    UserId = user.Id,
                    DepartmentId = request.DepartmentId!.Value,
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

            case RoleNames.InternalCommitteeMember:
            case RoleNames.ExternalCommitteeMember:
                var committeeType = request.Role == RoleNames.InternalCommitteeMember
                    ? CommitteeMemberRoleType.Internal
                    : CommitteeMemberRoleType.External;

                var existing = await db.CommitteeMemberProfiles
                    .FirstOrDefaultAsync(c => c.UserId == user.Id, cancellationToken);

                if (existing is not null)
                {
                    // One profile, two roles. Moving between internal and external is a change of
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
}
