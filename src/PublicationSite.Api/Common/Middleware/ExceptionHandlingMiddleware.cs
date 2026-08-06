using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PublicationSite.Api.Common.Exceptions;

namespace PublicationSite.Api.Common.Middleware;

public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var (statusCode, message, errors) = exception switch
        {
            NotFoundException e => (HttpStatusCode.NotFound, e.Message, (IReadOnlyList<string>?)null),
            ForbiddenException e => (HttpStatusCode.Forbidden, e.Message, null),
            ConflictException e => (HttpStatusCode.Conflict, e.Message, null),
            BusinessRuleException e => (HttpStatusCode.UnprocessableEntity, e.Message, null),
            ValidationAppException e => (HttpStatusCode.BadRequest, e.Message, e.Errors),

            // Somebody else changed this record between the moment this request read it and the
            // moment it tried to write. Said in those words rather than in the database's: the
            // person on the other end pressed a button twice, or two people are working on the
            // same publication, and either way the answer is to look again before deciding.
            DbUpdateConcurrencyException => (HttpStatusCode.Conflict,
                "Somebody else changed this while you were working on it. Reload the page and check "
                + "where it stands before deciding again.", null),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.", null)
        };

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception processing {Method} {Path}",
                context.Request.Method, context.Request.Path);
        }
        else
        {
            logger.LogWarning("{ExceptionType} handled for {Method} {Path}: {Message}",
                exception.GetType().Name, context.Request.Method, context.Request.Path, exception.Message);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var payload = ApiResponse.Fail(message, errors);
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
