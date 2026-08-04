using FluentValidation;

namespace PublicationSite.Api.DTOs.Common;

/// <summary>
/// Length only.
///
/// This body serves both pipeline decisions, where whether a reason is required is the
/// institution's setting, and administrative actions on accounts, where it always is. Requiring
/// it here would refuse a pipeline decision before the service could read the setting, so each
/// one says so for itself.
/// </summary>
public class CommentsRequestValidator : AbstractValidator<CommentsRequest>
{
    public CommentsRequestValidator()
    {
        RuleFor(x => x.Comments).MaximumLength(4000);
    }
}
