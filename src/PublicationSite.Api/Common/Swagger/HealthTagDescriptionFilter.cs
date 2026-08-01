using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PublicationSite.Api.Common.Swagger;

/// <summary>
/// Fills in the two things <c>/health</c> cannot say for itself.
///
/// Every other entry in the reference is a controller action, and carries its group description on
/// the controller class and its response descriptions on <c>[ProducesResponseType]</c> plus a
/// <c>&lt;response&gt;</c> comment. <c>/health</c> is a minimal endpoint: no class, no attributes,
/// nowhere to write either. Left alone it is the one heading in the document with nothing under it
/// and the one response still labelled "Success" — which is the gap all the other descriptions
/// were written to close.
/// </summary>
public class HealthTagDescriptionFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        document.Tags ??= [];

        if (document.Tags.All(t => t.Name != "Health"))
        {
            document.Tags.Add(new OpenApiTag
            {
                Name = "Health",
                Description =
                    "Whether the API is up. Read by the host that decides whether to keep the process " +
                    "running, so it answers from the server alone and queries nothing — a check that " +
                    "touched the database would report the API down whenever the database was merely " +
                    "slow, and the host would restart something that was working."
            });
        }

        if (document.Paths.TryGetValue("/health", out var path)
            && path.Operations.TryGetValue(OperationType.Get, out var get)
            && get.Responses.TryGetValue("200", out var ok))
        {
            ok.Description =
                "The API is up. The body carries the server's clock as well, which is how you tell a " +
                "live answer from a cached one.";
        }
    }
}
