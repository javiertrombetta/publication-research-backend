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

        // A date already gone would expire the round at the moment it was created, which is not a
        // deadline anybody meant to set. A minute's grace, so a form filled in slowly is not
        // refused for the sake of a few seconds.
        RuleFor(x => x.RespondBy)
            .Must(by => by is null || by > DateTime.UtcNow.AddMinutes(-1))
            .WithMessage("The date supervisors have to answer by has already passed.");
    }
}

public class AssignSupervisorRequestValidator : AbstractValidator<AssignSupervisorRequest>
{
    public AssignSupervisorRequestValidator()
    {
        RuleFor(x => x.Comments).NotEmpty();
    }
}
