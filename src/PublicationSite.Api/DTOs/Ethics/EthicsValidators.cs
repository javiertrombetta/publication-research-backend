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
/// A comment is asked for where somebody has to act on it, or where the reasoning is the record.
///
/// The ruling on whether ethics documentation is required is the second kind: it is a judgement
/// about somebody else's research, it goes on the publication's history, and a coordinator
/// confirms or overturns it later on the strength of it. "Required" with nothing said is a
/// decision nobody can weigh.
///
/// Accepting documents that are in order is not: there is nothing to explain and nobody waiting
/// to be told. Sending them back is, and there the comment is the whole of the message, since it
/// is what the student is given to work from.
/// </summary>
public class SupervisorRequirementDecisionRequestValidator : AbstractValidator<SupervisorRequirementDecisionRequest>
{
    public SupervisorRequirementDecisionRequestValidator()
    {
        RuleFor(x => x.Comments).MaximumLength(4000);

        RuleFor(x => x.Comments).NotEmpty()
            .WithMessage("Say why. The coordinator confirms this decision on the strength of your reasoning, and it stays on the publication's history.");
    }
}

public class DocumentReviewDecisionRequestValidator : AbstractValidator<DocumentReviewDecisionRequest>
{
    public DocumentReviewDecisionRequestValidator()
    {
        RuleFor(x => x.Comments).MaximumLength(4000);

        RuleFor(x => x.Comments).NotEmpty().When(x => !x.Accept)
            .WithMessage("Say what needs changing. It is all the student is given to work from.");
    }
}

public class CoordinatorFinalDecisionRequestValidator : AbstractValidator<CoordinatorFinalDecisionRequest>
{
    public CoordinatorFinalDecisionRequestValidator()
    {
        RuleFor(x => x.Comments).MaximumLength(4000);

        RuleFor(x => x.Comments).NotEmpty().When(x => !x.Approve)
            .WithMessage("Say what needs changing. It is all the student is given to work from.");
    }
}
