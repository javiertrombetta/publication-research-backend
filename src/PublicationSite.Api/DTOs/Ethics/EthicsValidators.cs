using FluentValidation;

namespace PublicationSite.Api.DTOs.Ethics;

public class EthicsDeclarationRequestValidator : AbstractValidator<EthicsDeclarationRequest>
{
    public EthicsDeclarationRequestValidator()
    {
        RuleFor(x => x.Response).NotEmpty().Must(r => r is "Yes" or "No" or "Unsure")
            .WithMessage("Response must be Yes, No or Unsure.");
    }
}

public class SupervisorRequirementDecisionRequestValidator : AbstractValidator<SupervisorRequirementDecisionRequest>
{
    public SupervisorRequirementDecisionRequestValidator()
    {
        RuleFor(x => x.Comments).NotEmpty();
    }
}

public class DocumentReviewDecisionRequestValidator : AbstractValidator<DocumentReviewDecisionRequest>
{
    public DocumentReviewDecisionRequestValidator()
    {
        RuleFor(x => x.Comments).NotEmpty();
    }
}

public class CoordinatorFinalDecisionRequestValidator : AbstractValidator<CoordinatorFinalDecisionRequest>
{
    public CoordinatorFinalDecisionRequestValidator()
    {
        RuleFor(x => x.Comments).NotEmpty();
    }
}
