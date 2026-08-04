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

/// <summary>
/// Length only. Whether a comment is required at all is this institution's decision, set per
/// decision in System settings and enforced beside the decision itself: see IDecisionCommentPolicy.
/// A validator cannot answer it, because "accept" and "send back" arrive here in the same shape
/// and the two have different answers.
/// </summary>
public class SupervisorRequirementDecisionRequestValidator : AbstractValidator<SupervisorRequirementDecisionRequest>
{
    public SupervisorRequirementDecisionRequestValidator()
    {
        RuleFor(x => x.Comments).MaximumLength(4000);
    }
}

/// <inheritdoc cref="SupervisorRequirementDecisionRequestValidator"/>
public class DocumentReviewDecisionRequestValidator : AbstractValidator<DocumentReviewDecisionRequest>
{
    public DocumentReviewDecisionRequestValidator()
    {
        RuleFor(x => x.Comments).MaximumLength(4000);
    }
}

/// <inheritdoc cref="SupervisorRequirementDecisionRequestValidator"/>
public class CoordinatorFinalDecisionRequestValidator : AbstractValidator<CoordinatorFinalDecisionRequest>
{
    public CoordinatorFinalDecisionRequestValidator()
    {
        RuleFor(x => x.Comments).MaximumLength(4000);
    }
}

/// <inheritdoc cref="SupervisorRequirementDecisionRequestValidator"/>
public class CoordinatorDocumentReviewRequestValidator : AbstractValidator<CoordinatorDocumentReviewRequest>
{
    public CoordinatorDocumentReviewRequestValidator()
    {
        RuleFor(x => x.Comments).MaximumLength(4000);
    }
}

/// <inheritdoc cref="SupervisorRequirementDecisionRequestValidator"/>
public class CoordinatorNotRequiredReviewRequestValidator : AbstractValidator<CoordinatorNotRequiredReviewRequest>
{
    public CoordinatorNotRequiredReviewRequestValidator()
    {
        RuleFor(x => x.Comments).MaximumLength(4000);
    }
}

/// <inheritdoc cref="SupervisorRequirementDecisionRequestValidator"/>
public class HeadOfDepartmentReviewRequestValidator : AbstractValidator<HeadOfDepartmentReviewRequest>
{
    public HeadOfDepartmentReviewRequestValidator()
    {
        RuleFor(x => x.Comments).MaximumLength(4000);
    }
}
