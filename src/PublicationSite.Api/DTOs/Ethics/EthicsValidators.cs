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

/// <summary>
/// The coordinator's read of documents the supervisor has already accepted. Same rule as the
/// supervisor's: passing them on explains itself, sending them back is nothing but the reason.
/// </summary>
public class CoordinatorDocumentReviewRequestValidator : AbstractValidator<CoordinatorDocumentReviewRequest>
{
    public CoordinatorDocumentReviewRequestValidator()
    {
        RuleFor(x => x.Comments).MaximumLength(4000);

        RuleFor(x => x.Comments).NotEmpty().When(x => !x.Approve)
            .WithMessage("Say what needs changing. It is all the student is given to work from.");
    }
}

/// <summary>
/// The coordinator confirming, or overturning, a supervisor's finding that no documentation is
/// needed. Overturning it puts a student to work who had been told they were finished, so it is
/// asked for a reason. Confirming is agreement with reasoning already on the record.
/// </summary>
public class CoordinatorNotRequiredReviewRequestValidator : AbstractValidator<CoordinatorNotRequiredReviewRequest>
{
    public CoordinatorNotRequiredReviewRequestValidator()
    {
        RuleFor(x => x.Comments).MaximumLength(4000);

        RuleFor(x => x.Comments).NotEmpty().When(x => x.RequireDocumentation)
            .WithMessage("Say why documentation is needed after all. You are overturning the supervisor's ruling, and this is what the student is given to work from.");
    }
}

/// <summary>
/// The head of department's comments. Required, because the step produces nothing else: the
/// coordinator's final decision rests on what is written here.
/// </summary>
public class HeadOfDepartmentReviewRequestValidator : AbstractValidator<HeadOfDepartmentReviewRequest>
{
    public HeadOfDepartmentReviewRequestValidator()
    {
        RuleFor(x => x.Comments).MaximumLength(4000);

        RuleFor(x => x.Comments).NotEmpty()
            .WithMessage("Say what you make of it. The coordinator decides on the strength of this, and it is the only thing this step records.");
    }
}
