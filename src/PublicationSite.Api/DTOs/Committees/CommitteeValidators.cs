using FluentValidation;

namespace PublicationSite.Api.DTOs.Committees;

/// <summary>
/// What is true of the request whatever this institution has configured. Whether a comment is
/// required is set per decision in System settings and enforced beside the decision itself: see
/// IDecisionCommentPolicy. Appointing the agreed composition and appointing a different one arrive
/// here in the same shape and have different answers.
/// </summary>
public class AssignCommitteeRequestValidator : AbstractValidator<AssignCommitteeRequest>
{
    public AssignCommitteeRequestValidator()
    {
        RuleFor(x => x.MemberUserIds).NotEmpty()
            .WithMessage("Choose at least one committee member.");

        RuleFor(x => x.Comments).MaximumLength(4000);
    }
}

/// <inheritdoc cref="AssignCommitteeRequestValidator"/>
public class CommitteeMemberReviewRequestValidator : AbstractValidator<CommitteeMemberReviewRequest>
{
    public CommitteeMemberReviewRequestValidator()
    {
        RuleFor(x => x.Comments).MaximumLength(4000);
    }
}
