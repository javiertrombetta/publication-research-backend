using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Common.Filters;

/// <summary>
/// Refuses anonymous requests when an administrator has switched the public catalogue off.
///
/// Applied to the controller rather than to each action, so an action added later is covered by
/// default instead of by remembering. The rule only ever tightens what anonymous callers can
/// reach; anything already requiring a signed-in user is unaffected by it.
/// </summary>
public class PublicCatalogueRequiredAttribute() : TypeFilterAttribute(typeof(Filter))
{
    private class Filter(ISystemSettingsProvider settings) : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // Signed-in people keep the catalogue. The setting governs the *public* catalogue,
            // meaning whether the institution shows its research to the world, and switching that
            // off is not a reason to stop its own students and staff reading what it has published.
            if (context.HttpContext.User.Identity?.IsAuthenticated == true)
            {
                await next();
                return;
            }

            var enabled = await settings.GetBoolAsync(
                SettingKeys.PublicCatalogueEnabled,
                SettingKeys.DefaultPublicCatalogueEnabled,
                context.HttpContext.RequestAborted);

            if (enabled)
            {
                await next();
                return;
            }

            // Not found rather than forbidden. There is nothing here to be granted access to, and
            // saying "forbidden" would confirm to an anonymous caller that a catalogue exists and
            // is merely being withheld.
            context.Result = new NotFoundObjectResult(
                ApiResponse.Fail("This site does not publish a public catalogue."));
        }
    }
}
