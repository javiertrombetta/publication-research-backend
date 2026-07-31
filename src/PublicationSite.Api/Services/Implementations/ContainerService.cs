using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Data;
using PublicationSite.Api.DTOs.Containers;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class ContainerService(
    ApplicationDbContext db,
    IDepartmentService departmentService,
    IContainerAccessService accessService,
    IAuditService auditService) : IContainerService
{
    public async Task<PublicationContainerDto> CreateAsync(Guid studentUserId, CancellationToken cancellationToken = default)
    {
        // A student may run several publication processes at the same time, each with its own
        // proposals, ethics workflow and paper, so there is deliberately no one-per-student cap.
        var studentProfile = await db.StudentProfiles.FirstOrDefaultAsync(s => s.UserId == studentUserId, cancellationToken)
            ?? throw new BusinessRuleException("Only students with a completed profile can start the publication process.");

        var coordinatorId = await departmentService.SelectCoordinatorForDepartmentAsync(studentProfile.DepartmentId, cancellationToken);

        var container = new PublicationContainer
        {
            StudentId = studentUserId,
            CoordinatorId = coordinatorId,
            CurrentPipeline = PipelineStage.ResearchProposals,
            Status = ContainerStatus.InProgress
        };

        db.PublicationContainers.Add(container);
        await db.SaveChangesAsync(cancellationToken);

        await auditService.LogActivityAsync(container.Id, studentUserId, "ContainerCreated",
            "Publication Container created; Coordinator auto-assigned by department workload.",
            newStatus: container.Status.ToString());

        return await GetByIdInternalAsync(container.Id, cancellationToken);
    }

    public async Task<PublicationContainerDto> GetByIdAsync(Guid id, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureAccessAsync(id, requestingUserId);
        return await GetByIdInternalAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<PublicationContainerDto>> GetMineAsync(Guid studentUserId, CancellationToken cancellationToken = default)
    {
        // Order before projecting: the DTO carries a correlated sub-query for Title, and EF Core
        // cannot translate an OrderBy applied on top of that projection.
        return await ProjectToDto(
                db.PublicationContainers
                    .Where(c => c.StudentId == studentUserId)
                    .OrderByDescending(c => c.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteOwnAsync(Guid containerId, Guid studentUserId, CancellationToken cancellationToken = default)
    {
        var container = await db.PublicationContainers.FirstOrDefaultAsync(c => c.Id == containerId, cancellationToken)
            ?? throw new NotFoundException(nameof(PublicationContainer), containerId);

        if (container.StudentId != studentUserId)
        {
            throw new ForbiddenException("You can only delete your own Publication Container.");
        }

        var hasProposals = await db.ResearchProposals.AnyAsync(p => p.PublicationContainerId == containerId, cancellationToken);
        if (hasProposals || container.CurrentPipeline != PipelineStage.ResearchProposals)
        {
            throw new BusinessRuleException(
                "This publication can no longer be deleted because its process has already started. " +
                "A Publication Container can only be discarded while it still has no research proposals.");
        }

        // Written before the delete so the trail survives it: AuditLogEntry deliberately has no
        // foreign key to the Container, so it is never cascaded away. The Container's own
        // ActivityHistory is cascade-deleted along with it.
        await auditService.LogAuditAsync(
            studentUserId,
            "ContainerDeleted",
            nameof(PublicationContainer),
            containerId,
            previousValue: container.Status.ToString(),
            comments: "Student discarded a Publication Container that had no research proposals.");

        db.PublicationContainers.Remove(container);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActivityHistoryEntryDto>> GetActivityHistoryAsync(Guid id, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureAccessAsync(id, requestingUserId);

        return await db.ActivityHistoryEntries
            .Where(a => a.PublicationContainerId == id)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new ActivityHistoryEntryDto(
                a.Id,
                a.ActorUser.FirstName + " " + a.ActorUser.LastName,
                // Staff is the placeholder role every @ais.ac.nz account starts with, so it is
                // ordered last: whatever operational role the actor also holds is the one they
                // were acting in.
                db.UserRoles
                    .Where(ur => ur.UserId == a.ActorUserId)
                    .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                    .OrderBy(name => name == RoleNames.Staff ? 1 : 0)
                    .FirstOrDefault(),
                a.OnBehalfOfUser == null ? null : a.OnBehalfOfUser.FirstName + " " + a.OnBehalfOfUser.LastName,
                a.Action,
                a.Comments,
                a.PreviousStatus,
                a.NewStatus,
                a.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PublicationContainerDto>> GetAllAsync(Guid? studentId, Guid? coordinatorId, string? status, CancellationToken cancellationToken = default)
    {
        var query = db.PublicationContainers.AsQueryable();

        if (studentId is not null) query = query.Where(c => c.StudentId == studentId);
        if (coordinatorId is not null) query = query.Where(c => c.CoordinatorId == coordinatorId);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ContainerStatus>(status, true, out var statusFilter))
        {
            query = query.Where(c => c.Status == statusFilter);
        }

        return await ProjectToDto(query.OrderByDescending(c => c.CreatedAt)).ToListAsync(cancellationToken);
    }

    public async Task<PublicationContainerDto> AssignCoordinatorManuallyAsync(AssignCoordinatorRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        // A student can have several containers, so "which one" has to be explicit: with an id
        // we reassign that container, without one we create an additional container for them.
        PublicationContainer? container = null;
        if (request.PublicationContainerId is { } containerId)
        {
            container = await db.PublicationContainers.FirstOrDefaultAsync(c => c.Id == containerId, cancellationToken)
                ?? throw new NotFoundException(nameof(PublicationContainer), containerId);

            if (container.StudentId != request.StudentUserId)
            {
                throw new BusinessRuleException("That Publication Container does not belong to the specified student.");
            }
        }

        if (container is null)
        {
            container = new PublicationContainer
            {
                StudentId = request.StudentUserId,
                CoordinatorId = request.CoordinatorUserId,
                CurrentPipeline = PipelineStage.ResearchProposals,
                Status = ContainerStatus.InProgress
            };
            db.PublicationContainers.Add(container);
            await db.SaveChangesAsync(cancellationToken);

            await auditService.LogActivityAsync(container.Id, actingUserId, "ContainerCreated",
                request.Comments, newStatus: container.Status.ToString());
        }
        else
        {
            var previousCoordinatorId = container.CoordinatorId;
            container.CoordinatorId = request.CoordinatorUserId;
            container.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            await auditService.LogActivityAsync(container.Id, actingUserId, "CoordinatorReassigned",
                request.Comments, previousStatus: previousCoordinatorId.ToString(), newStatus: request.CoordinatorUserId.ToString());
        }

        return await GetByIdInternalAsync(container.Id, cancellationToken);
    }

    private async Task<PublicationContainerDto> GetByIdInternalAsync(Guid id, CancellationToken cancellationToken)
    {
        return await ProjectToDto(db.PublicationContainers.Where(c => c.Id == id)).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(PublicationContainer), id);
    }

    private static IQueryable<PublicationContainerDto> ProjectToDto(IQueryable<PublicationContainer> query) =>
        query.Select(c => new PublicationContainerDto(
            c.Id,
            c.StudentId,
            c.Student.FirstName + " " + c.Student.LastName,
            c.CoordinatorId,
            c.Coordinator.FirstName + " " + c.Coordinator.LastName,
            c.AssignedSupervisorId,
            c.AssignedSupervisor == null ? null : c.AssignedSupervisor.FirstName + " " + c.AssignedSupervisor.LastName,
            (int)c.CurrentPipeline,
            c.Status.ToString(),
            c.CreatedAt,
            c.Publication != null && c.Publication.Title != ""
                ? c.Publication.Title
                : c.Proposals.Where(p => p.Status == ProposalStatus.Assigned).Select(p => p.Title).FirstOrDefault(),
            c.Proposals.Count,
            c.Publication == null ? null : c.Publication.Status.ToString()));
}
