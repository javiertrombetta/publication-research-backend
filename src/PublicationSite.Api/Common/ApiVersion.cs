namespace PublicationSite.Api.Common;

/// <summary>
/// The version this API answers as.
///
/// It is not in the route. Every path is <c>api/…</c> and stays that way: the routes are what the
/// frontend, the Postman collection and the team's saved requests are all written against, and a
/// version in the path would break every one of them for a change that alters no behaviour. What
/// the version identifies is the published contract: the OpenAPI document and the name it is served
/// under, so a reader can tell which description of the API they are holding.
///
/// Raise the minor part when endpoints are added or described; the major part when something
/// already published changes shape, because that is the change callers have to act on.
/// </summary>
public static class ApiVersion
{
    public const string Current = "v2.0";
}
