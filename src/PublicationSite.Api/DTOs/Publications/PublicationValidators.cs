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
/// Length only. Whether a comment is required is this institution's decision, set per decision in
/// System settings and enforced beside the decision itself: see IDecisionCommentPolicy. Accepting
/// and sending back arrive here in the same shape and have different answers.
/// </summary>
public class PaperReviewDecisionRequestValidator : AbstractValidator<PaperReviewDecisionRequest>
{
    public PaperReviewDecisionRequestValidator()
    {
        RuleFor(x => x.Comments).MaximumLength(4000);
    }
}
