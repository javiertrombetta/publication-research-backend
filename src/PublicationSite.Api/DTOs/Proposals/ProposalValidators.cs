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

        // Length only. Whether this decision has to carry a message is the institution's to set,
        // and a rule here would refuse the send before the service could read that setting.
        RuleFor(x => x.Comments).MaximumLength(4000);

        // A date already gone would expire the round at the moment it was created, which is not a
        // deadline anybody meant to set. A minute's grace, so a form filled in slowly is not
        // refused for the sake of a few seconds.
        // Required. A round with no date never ends: the proposals sit there waiting on supervisors
        // who may never reply, and somebody has to notice for themselves. The screen fills the
        // field in from the institution's expected response time, so this is a floor rather than
        // one more thing to think about on every send.
        RuleFor(x => x.RespondBy)
            .NotNull()
            .WithMessage("Say when the supervisors have to answer by.");

        RuleFor(x => x.RespondBy)
            .Must(by => by is null || by > DateTime.UtcNow.AddMinutes(-1))
            .WithMessage("The date supervisors have to answer by has already passed.");
    }
}

public class AssignSupervisorRequestValidator : AbstractValidator<AssignSupervisorRequest>
{
    public AssignSupervisorRequestValidator()
    {
        RuleFor(x => x.Comments).MaximumLength(4000);
    }
}
