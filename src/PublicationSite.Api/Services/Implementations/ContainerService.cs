using Microsoft.EntityFrameworkCore;
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
        var alreadyExists = await db.PublicationContainers.AnyAsync(c => c.StudentId == studentUserId, cancellationToken);
        if (alreadyExists)
        {
            throw new ConflictException("You already have a Publication Container.");
        }

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

    public async Task<IReadOnlyList<ActivityHistoryEntryDto>> GetActivityHistoryAsync(Guid id, Guid requestingUserId, CancellationToken cancellationToken = default)
    {
        await accessService.EnsureAccessAsync(id, requestingUserId);

        return await db.ActivityHistoryEntries
            .Where(a => a.PublicationContainerId == id)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new ActivityHistoryEntryDto(
                a.Id,
                a.ActorUser.FirstName + " " + a.ActorUser.LastName,
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

        return await ProjectToDto(query).OrderByDescending(c => c.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task<PublicationContainerDto> AssignCoordinatorManuallyAsync(AssignCoordinatorRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var container = await db.PublicationContainers.FirstOrDefaultAsync(c => c.StudentId == request.StudentUserId, cancellationToken);

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
            c.CreatedAt));
}
