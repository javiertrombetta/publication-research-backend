using FluentValidation;

namespace PublicationSite.Api.DTOs.Committees;

/// <summary>
/// The same rule the rest of the pipeline keeps: a comment is asked for where a decision departs
/// from what was agreed, or where somebody has to act on it.
///
/// Appointing the committee this publication was opened for needs no explanation. Appointing a
/// differently shaped one does: the recorded composition is what the institution settled for this
/// research, and whoever reads the history later has to be able to see why it was set aside.
/// </summary>
public class AssignCommitteeRequestValidator : AbstractValidator<AssignCommitteeRequest>
{
    public AssignCommitteeRequestValidator()
    {
        RuleFor(x => x.MemberUserIds).NotEmpty()
            .WithMessage("Choose at least one committee member.");

        RuleFor(x => x.Comments).MaximumLength(4000);

        RuleFor(x => x.Comments).NotEmpty().When(x => x.OverrideComposition)
            .WithMessage("Say why this publication is being given a committee of a different shape. It stays on the publication's history.");
    }
}

public class CommitteeMemberReviewRequestValidator : AbstractValidator<CommitteeMemberReviewRequest>
{
    public CommitteeMemberReviewRequestValidator()
    {
        RuleFor(x => x.Comments).MaximumLength(4000);

        RuleFor(x => x.Comments).NotEmpty().When(x => !x.Approve)
            .WithMessage("Say what is wrong with the paper. Your comments are what the coordinator decides on.");
    }
}
