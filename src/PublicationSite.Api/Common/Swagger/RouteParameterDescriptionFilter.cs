using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PublicationSite.Api.Common.Swagger;

/// <summary>
/// Says what the identifiers in a route are identifiers of.
///
/// A path parameter takes its description from a <c>&lt;param&gt;</c> tag on the action, and eighty
/// of them across this API mean exactly the same thing each time: the id of the thing the route is
/// about. Written at each action that would be the same sentence eighty times, which is the kind of
/// duplication that stops being maintained after the third one, and the reference would still have
/// had gaps wherever somebody forgot.
///
/// Only what is genuinely uniform lives here. A parameter that means something particular to its
/// endpoint is described where it is declared, and this leaves anything already documented alone.
/// </summary>
public class RouteParameterDescriptionFilter : IOperationFilter
{
    private static readonly Dictionary<string, string> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["containerId"] = "The publication container this belongs to.",
        ["publicationId"] = "The research paper this is about.",
        ["proposalId"] = "The research proposal this is about.",
        ["committeeId"] = "The evaluation committee this is about.",
        ["groupId"] = "The coordinator's saved group of supervisors.",
        ["documentId"] = "The uploaded ethics document.",
        ["versionId"] = "The version of the research paper.",
        ["coordinatorUserId"] = "The coordinator, by user id.",
        ["userId"] = "The user this is about.",
        ["token"] = "The single-use token from the emailed link. It is consumed by a successful call.",
        ["search"] = "A word to look for. Matched against the names and titles this listing shows, "
                   + "so a reader can find a row by whichever of them they remember."
    };

    /// <summary>What a bare <c>id</c> identifies, which is whatever its group is about.</summary>
    private static readonly Dictionary<string, string> BareIdByTag = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Catalogue"] = "The published paper.",
        ["Containers"] = "The publication container.",
        ["Departments"] = "The department.",
        ["Invitations"] = "The invitation.",
        ["Notifications"] = "The notification.",
        ["Settings"] = "The ethics document requirement.",
        ["Users"] = "The user."
    };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var tag = operation.Tags?.FirstOrDefault()?.Name ?? string.Empty;

        foreach (var parameter in operation.Parameters ?? [])
        {
            if (!string.IsNullOrWhiteSpace(parameter.Description)) continue;

            if (ByName.TryGetValue(parameter.Name, out var described))
            {
                parameter.Description = described;
            }
            else if (string.Equals(parameter.Name, "id", StringComparison.OrdinalIgnoreCase)
                     && BareIdByTag.TryGetValue(tag, out var byTag))
            {
                parameter.Description = byTag;
            }
        }
    }
}
