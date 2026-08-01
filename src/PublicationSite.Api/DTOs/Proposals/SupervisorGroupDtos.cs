using FluentValidation;

namespace PublicationSite.Api.DTOs.Proposals;

/// <param name="OwnerName">The coordinator whose group this is. Redundant on a coordinator's own list and the whole point of the administrator's, where the same name can appear under several people.</param>
/// <param name="MemberCount">How many supervisors are in the group, whatever their state today.</param>
/// <param name="AvailableCount">How many of them could actually be sent a proposal right now. Lower than MemberCount when somebody has been disabled or has marked themselves as not taking work on, which is worth saying before the coordinator sends rather than after.</param>
public record SupervisorGroupDto(
    Guid Id,
    string Name,
    Guid OwnerId,
    string OwnerName,
    int MemberCount,
    int AvailableCount,
    IReadOnlyList<SupervisorGroupMemberDto> Members);

/// <param name="IsAvailable">False when the account is disabled or the supervisor has marked themselves as not taking work on. They stay in the group either way: the group is the coordinator's list, not a statement about who is free this week.</param>
public record SupervisorGroupMemberDto(Guid SupervisorId, string Name, bool IsAvailable);

public record SaveSupervisorGroupRequest(string Name, IReadOnlyList<Guid> SupervisorIds);

/// <param name="All">True to discard every group in the institution. Spelled out rather than implied by an empty list of ids, so clearing the lot cannot happen by sending nothing.</param>
public record DeleteSupervisorGroupsRequest(IReadOnlyList<Guid> GroupIds, bool All = false);

public class SaveSupervisorGroupRequestValidator : AbstractValidator<SaveSupervisorGroupRequest>
{
    public SaveSupervisorGroupRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.SupervisorIds).NotEmpty();
    }
}

public class DeleteSupervisorGroupsRequestValidator : AbstractValidator<DeleteSupervisorGroupsRequest>
{
    public DeleteSupervisorGroupsRequestValidator()
    {
        // Either name some, or say all. Neither is a request that does nothing, and answering it
        // with "0 groups deleted" reads like a failure rather than an empty instruction.
        RuleFor(x => x.GroupIds).NotEmpty().When(x => !x.All)
            .WithMessage("Choose the groups to delete, or ask for all of them.");
    }
}
