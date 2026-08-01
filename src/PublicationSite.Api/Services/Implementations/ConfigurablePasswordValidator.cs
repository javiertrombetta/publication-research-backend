using Microsoft.AspNetCore.Identity;
using PublicationSite.Api.Common;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

/// <summary>
/// Enforces the password rules an administrator has configured.
///
/// Identity's built-in <c>PasswordValidator</c> reads <c>IdentityOptions</c>, which are bound once
/// at start-up and cannot follow a database value. Changing a rule would mean restarting the API.
/// So the built-in options are set to the loosest configuration this validator will ever allow (see
/// Program.cs) and the real rules are applied here, freshly read on every check.
///
/// Registering this alongside the default validator would be belt and braces; it replaces it,
/// because two validators disagreeing about the minimum length produces two error messages for one
/// problem.
/// </summary>
public class ConfigurablePasswordValidator(ISystemSettingService settingService)
    : IPasswordValidator<ApplicationUser>
{
    public async Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return Fail("PasswordRequired", "Enter a password.");
        }

        var policy = await settingService.GetPasswordSettingsAsync();
        var errors = new List<IdentityError>();

        if (password.Length < policy.MinimumLength)
        {
            errors.Add(Error("PasswordTooShort",
                $"Passwords must be at least {policy.MinimumLength} characters long."));
        }

        if (policy.RequireDigit && !password.Any(char.IsDigit))
        {
            errors.Add(Error("PasswordRequiresDigit", "Passwords must contain a number."));
        }

        if (policy.RequireUppercase && !password.Any(char.IsUpper))
        {
            errors.Add(Error("PasswordRequiresUpper", "Passwords must contain a capital letter."));
        }

        if (policy.RequireLowercase && !password.Any(char.IsLower))
        {
            errors.Add(Error("PasswordRequiresLower", "Passwords must contain a lower-case letter."));
        }

        if (policy.RequireSymbol && password.All(char.IsLetterOrDigit))
        {
            errors.Add(Error("PasswordRequiresNonAlphanumeric",
                "Passwords must contain a symbol, such as ! or ?."));
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }

    private static IdentityResult Fail(string code, string description) =>
        IdentityResult.Failed(Error(code, description));

    private static IdentityError Error(string code, string description) =>
        new() { Code = code, Description = description };
}
