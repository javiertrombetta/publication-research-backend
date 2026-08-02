using Microsoft.AspNetCore.Mvc.Filters;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Common;

/// <summary>
/// Fills in how long a page is, for any request that did not say.
///
/// The number belongs to the institution rather than to the code, and an administrator who sets it
/// to twenty means every listing, not the one screen they happened to be looking at. Applied here,
/// once, so that holds for every paged endpoint including the ones written after it, and for any
/// client of the API rather than only this site.
///
/// A caller that names a page size still gets the one it asked for: this only speaks for the
/// silence. Page sizes still pass through SafePageSize, so the ceiling applies either way.
/// </summary>
public class ConfiguredPageSizeFilter(ISystemSettingsProvider settings) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Read once per request rather than per argument, and only when there is something to fill
        // in: most requests carry no paging at all, and this should cost them nothing.
        var unset = context.ActionArguments.Values
            .OfType<PageRequest>()
            .Where(p => p.PageSize <= 0)
            .ToList();

        if (unset.Count > 0)
        {
            var configured = await settings.GetIntAsync(
                SettingKeys.RowsPerPage, SettingKeys.DefaultRowsPerPage, context.HttpContext.RequestAborted);

            foreach (var request in unset)
            {
                request.PageSize = configured;
            }
        }

        await next();
    }
}
