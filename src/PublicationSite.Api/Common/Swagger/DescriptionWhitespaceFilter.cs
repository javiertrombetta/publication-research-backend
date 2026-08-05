using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text.RegularExpressions;

namespace PublicationSite.Api.Common.Swagger;

/// <summary>
/// Takes the source file's indentation back out of the prose.
///
/// Every summary here is an XML comment, so a paragraph that runs over one line arrives with the
/// twelve spaces that lined it up in the editor sitting inside the sentence, and the blank line
/// between two paragraphs arrives as a line holding only those spaces. A browser collapses runs of
/// whitespace, so the reference looks right and the fault stays hidden in the document itself,
/// where every other reader of it finds the ragged text: the Postman collection is generated from
/// this, and Markdown treats a line beginning with four spaces as a code block, which turns an
/// ordinary second paragraph into a grey box.
///
/// Paragraph breaks are kept, since they are the author's. Only the indentation goes.
/// </summary>
public partial class DescriptionWhitespaceFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        foreach (var (_, item) in document.Paths)
        {
            foreach (var (_, operation) in item.Operations)
            {
                operation.Summary = Tidy(operation.Summary);
                operation.Description = Tidy(operation.Description);

                foreach (var parameter in operation.Parameters ?? [])
                {
                    parameter.Description = Tidy(parameter.Description);
                }

                if (operation.RequestBody is { } body) body.Description = Tidy(body.Description);

                foreach (var (_, response) in operation.Responses)
                {
                    response.Description = Tidy(response.Description);
                }
            }
        }

        foreach (var (_, schema) in document.Components.Schemas)
        {
            schema.Description = Tidy(schema.Description);
            foreach (var (_, property) in schema.Properties)
            {
                property.Description = Tidy(property.Description);
            }
        }

        foreach (var tag in document.Tags ?? [])
        {
            tag.Description = Tidy(tag.Description);
        }
    }

    private static string? Tidy(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var lines = text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim());
        var joined = string.Join("\n", lines);

        // Three or more newlines only ever come from a blank line that was itself indented.
        return BlankRun().Replace(joined, "\n\n").Trim();
    }

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankRun();
}
