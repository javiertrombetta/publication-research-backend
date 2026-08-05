using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Data;
using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Common.Authentication;

/// <summary>
/// Checks that the account behind an otherwise valid token is still allowed in.
///
/// A signed token says what was true when it was issued, and this one lasts an hour. Disabling an
/// account refused the next sign-in and said nothing about the tokens already out, so somebody
/// disabled at ten past the hour went on working until the hour was up. For an account disabled
/// because of what its owner was doing, that is the wrong hour to give away.
///
/// The cost is one lookup by primary key per authenticated request, returning a single column. It
/// is worth it here: the alternative is either an hour of leeway or an access token so short that
/// every screen spends its time refreshing.
/// </summary>
public static class AccountStillValidEvents
{
    public static JwtBearerEvents Create() => new()
    {
        OnTokenValidated = async context =>
        {
            var value = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(value, out var userId))
            {
                context.Fail("The token does not say who it belongs to.");
                return;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
            var status = await db.Users
                .Where(u => u.Id == userId)
                .Select(u => (UserStatus?)u.Status)
                .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

            // A missing row is a token for an account that no longer exists at all.
            if (status is not UserStatus.Enabled)
            {
                context.Fail("This account can no longer be used.");
            }
        }
    };
}
