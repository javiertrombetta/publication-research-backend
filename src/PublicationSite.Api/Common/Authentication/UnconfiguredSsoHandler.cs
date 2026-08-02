using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace PublicationSite.Api.Common.Authentication;

/// <summary>
/// Stands in for Microsoft Entra on a deployment that has no tenant configured, and refuses
/// everything.
///
/// The single sign-on endpoint is guarded by an authentication scheme named "AzureAd". That scheme
/// is only real once a tenant is configured, and asking ASP.NET Core to authorise against a scheme
/// that was never registered throws, so the endpoint answered a caller with a 500 and the word
/// "unexpected". Nothing unexpected had happened: single sign-on is simply not set up here.
///
/// Registering this in its place keeps the scheme name valid and turns that into a plain 401 with a
/// reason, which is both the truth and something a client can act on.
/// </summary>
public sealed class UnconfiguredSsoHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AzureAd";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(AuthenticateResult.Fail(
            "Single sign-on is not configured on this server. No Microsoft Entra tenant has been "
            + "set, so there is no institutional account to sign in with. Sign in with a password."));
}
