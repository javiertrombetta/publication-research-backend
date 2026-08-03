using FluentValidation;

namespace PublicationSite.Api.DTOs.Publications;

public class UpdatePublicationMetadataRequestValidator : AbstractValidator<UpdatePublicationMetadataRequest>
{
    public UpdatePublicationMetadataRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Abstract).NotEmpty();
    }
}

/// <summary>
/// The same rule the ethics decisions keep: a comment is asked for where somebody has to act on
/// it. Sending a paper back is nothing but the reason, since it is all the student has to work
/// from. Passing it on explains itself, and asking for a note there only gets one written to get
/// past the form.
/// </summary>
public class PaperReviewDecisionRequestValidator : AbstractValidator<PaperReviewDecisionRequest>
{
    public PaperReviewDecisionRequestValidator()
    {
        RuleFor(x => x.Comments).MaximumLength(4000);

        RuleFor(x => x.Comments).NotEmpty().When(x => !x.Accept)
            .WithMessage("Say what needs changing. It is all the student is given to work from.");
    }
}
