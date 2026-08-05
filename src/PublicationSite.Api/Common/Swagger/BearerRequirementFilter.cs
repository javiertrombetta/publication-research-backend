using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PublicationSite.Api.Common.Swagger;

/// <summary>
/// Puts the padlock on the endpoints that need one, and only those.
///
/// The obvious way round is a single requirement declared for the whole document, since almost
/// every endpoint wants the token. It describes the other seventeen wrongly: registering, signing
/// in, refreshing a token, reading the public catalogue and looking up an invitation all showed as
/// requiring the token you sign in to get, and Try it out sent an Authorization header the endpoint
/// never asked for, so what the reader exercised was not what a caller does.
///
/// The specification's answer to that is an empty requirement on the operation, which overrides the
/// document's. That does not survive the trip: an empty collection is left out when the document is
/// written, so the override vanishes and the global requirement stands. Hence this direction
/// instead. Nothing is declared for the document, and the requirement is attached to each operation
/// that actually asks for authorisation, which leaves the anonymous ones carrying nothing to
/// inherit.
/// </summary>
public class BearerRequirementFilter : IOperationFilter
{
    private static readonly OpenApiSecurityRequirement Bearer = new()
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            []
        }
    };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;

        // Both halves matter. [AllowAnonymous] on the action wins over [Authorize] on the
        // controller, and an endpoint carrying neither is anonymous because nothing above it asked
        // for anything either.
        var allowsAnonymous = metadata.OfType<IAllowAnonymous>().Any();
        var requiresAuthorisation = metadata.OfType<IAuthorizeData>().Any();

        if (!allowsAnonymous && requiresAuthorisation)
        {
            operation.Security = [Bearer];
        }
    }
}
