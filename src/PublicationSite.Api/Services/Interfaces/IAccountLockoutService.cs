using PublicationSite.Api.Entities;

namespace PublicationSite.Api.Services.Interfaces;

/// <summary>
/// Locks an account after too many wrong passwords, using the threshold and duration an
/// administrator has configured.
///
/// Identity has its own lockout, but it reads <c>IdentityOptions.Lockout</c>, which is bound
/// once at start-up and so cannot follow a setting changed at runtime. Rather than have two
/// mechanisms half-working, Identity's is switched off and this one owns the behaviour — which
/// also lets it cover changing a password, not just signing in. An attacker at a borrowed
/// unlocked laptop attacks the change-password form, where the sign-in page's protection would
/// never have been consulted.
/// </summary>
public interface IAccountLockoutService
{
    /// <summary>
    /// Throws when the account is currently locked, naming the time it reopens. Call before
    /// checking a password, so a locked account is never told whether its password was right.
    /// </summary>
    Task EnsureNotLockedOutAsync(ApplicationUser user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a wrong password and locks the account once the configured threshold is reached.
    /// </summary>
    /// <returns>True when this attempt was the one that locked the account.</returns>
    Task<bool> RecordFailureAsync(ApplicationUser user, CancellationToken cancellationToken = default);

    /// <summary>Clears the run of failures after a password is accepted.</summary>
    Task RecordSuccessAsync(ApplicationUser user, CancellationToken cancellationToken = default);
}
