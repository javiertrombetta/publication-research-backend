using FluentValidation;

namespace PublicationSite.Api.DTOs.Proposals;

public class SaveProposalRequestValidator : AbstractValidator<SaveProposalRequest>
{
    public SaveProposalRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Abstract).NotEmpty();
    }
}

public class SendToSupervisorsRequestValidator : AbstractValidator<SendToSupervisorsRequest>
{
    public SendToSupervisorsRequestValidator()
    {
        RuleFor(x => x.ProposalIds).NotEmpty();
        RuleFor(x => x.SupervisorIds).NotEmpty();
        RuleFor(x => x.Comments).NotEmpty();
    }
}

public class AssignSupervisorRequestValidator : AbstractValidator<AssignSupervisorRequest>
{
    public AssignSupervisorRequestValidator()
    {
        RuleFor(x => x.Comments).NotEmpty();
    }
}
