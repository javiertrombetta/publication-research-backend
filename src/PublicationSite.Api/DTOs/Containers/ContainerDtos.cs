namespace PublicationSite.Api.DTOs.Containers;

public record PublicationContainerDto(
    Guid Id,
    Guid StudentId,
    string StudentName,
    Guid CoordinatorId,
    string CoordinatorName,
    Guid? AssignedSupervisorId,
    string? AssignedSupervisorName,
    int CurrentPipeline,
    string Status,
    DateTime CreatedAt);

public record ActivityHistoryEntryDto(
    Guid Id,
    string ActorName,
    string? OnBehalfOfName,
    string Action,
    string Comments,
    string? PreviousStatus,
    string? NewStatus,
    DateTime CreatedAt);

public record AssignCoordinatorRequest(Guid StudentUserId, Guid CoordinatorUserId, string Comments);
