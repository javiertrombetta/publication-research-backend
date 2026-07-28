using FluentValidation;

namespace PublicationSite.Api.DTOs.Common;

public class CommentsRequestValidator : AbstractValidator<CommentsRequest>
{
    public CommentsRequestValidator()
    {
        RuleFor(x => x.Comments).NotEmpty();
    }
}
