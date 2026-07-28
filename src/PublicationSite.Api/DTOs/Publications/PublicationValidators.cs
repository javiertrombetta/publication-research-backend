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

public class PaperReviewDecisionRequestValidator : AbstractValidator<PaperReviewDecisionRequest>
{
    public PaperReviewDecisionRequestValidator()
    {
        RuleFor(x => x.Comments).NotEmpty();
    }
}
