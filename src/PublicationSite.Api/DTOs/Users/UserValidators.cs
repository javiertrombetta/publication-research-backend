using FluentValidation;

namespace PublicationSite.Api.DTOs.Users;

public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Role).NotEmpty();
    }
}

public class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Comments).NotEmpty();
    }
}

public class ChangeUserRoleRequestValidator : AbstractValidator<ChangeUserRoleRequest>
{
    public ChangeUserRoleRequestValidator()
    {
        RuleFor(x => x.Role).NotEmpty();
        RuleFor(x => x.Comments).NotEmpty();
    }
}

/// <summary>
/// What a person may write into their own profile.
///
/// An administrator editing somebody else has been held to this all along; the same person editing
/// themselves was not. An empty name went straight through and left an account that reads as a
/// blank space in every listing, on every queue and in the trail of what it decided. A name longer
/// than the column reached the database and came back as a server error, which tells the person
/// nothing about the one field they need to shorten.
///
/// The lengths are the columns' own, so what passes here is what will store.
/// </summary>
public class UpdateMyProfileRequestValidator : AbstractValidator<UpdateMyProfileRequest>
{
    public UpdateMyProfileRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(150);

        // Optional, and left alone when omitted: the service treats null as "no change". Empty is
        // a different thing and the columns are required, so only the length is checked here.
        RuleFor(x => x.Programme).MaximumLength(200);
        RuleFor(x => x.Cohort).MaximumLength(50);
        RuleFor(x => x.Orcid).MaximumLength(50);
    }
}

