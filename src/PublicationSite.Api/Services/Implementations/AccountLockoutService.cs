using Microsoft.AspNetCore.Identity;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class AccountLockoutService(
    UserManager<ApplicationUser> userManager,
    ISystemSettingService settingService,
    IAuditService auditService) : IAccountLockoutService
{
    public Task EnsureNotLockedOutAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        if (user.LockoutEnd is not { } until || until <= DateTimeOffset.UtcNow)
        {
            return Task.CompletedTask;
        }

        // A deleted account is locked until the end of time; saying "try again in 292 billion
        // minutes" would be absurd, and it is not a lockout the person can wait out.
        if (until == DateTimeOffset.MaxValue)
        {
            throw new ForbiddenException("This account is no longer active. Contact an administrator.");
        }

        var minutes = Math.Max(1, (int)Math.Ceiling((until - DateTimeOffset.UtcNow).TotalMinutes));

        throw new ForbiddenException(
            $"Too many failed attempts. This account is locked for another {minutes} " +
            (minutes == 1 ? "minute." : "minutes."));
    }

    public async Task<bool> RecordFailureAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var policy = await settingService.GetPasswordSettingsAsync(cancellationToken);

        user.AccessFailedCount++;

        if (user.AccessFailedCount < policy.LockoutAttempts)
        {
            await userManager.UpdateAsync(user);
            return false;
        }

        user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(policy.LockoutMinutes);

        // Reset now rather than when the lockout expires: nothing runs at that moment, and
        // leaving the count at the threshold would lock the account again on the next single
        // mistake, indefinitely.
        user.AccessFailedCount = 0;
        await userManager.UpdateAsync(user);

        await auditService.LogAuditAsync(user.Id, "AccountLockedOut", nameof(ApplicationUser), user.Id,
            comments: $"Locked for {policy.LockoutMinutes} minutes after {policy.LockoutAttempts} failed password attempts.");

        return true;
    }

    public async Task RecordSuccessAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        if (user.AccessFailedCount == 0)
        {
            return;
        }

        user.AccessFailedCount = 0;
        await userManager.UpdateAsync(user);
    }
}
