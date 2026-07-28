using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using PublicationSite.Api.DTOs.Auth;
using PublicationSite.Api.DTOs.Users;
using PublicationSite.IntegrationTests.Infrastructure;
using Xunit;

namespace PublicationSite.IntegrationTests.Flows;

[Collection(ApiCollection.Name)]
public class AuthFlowTests(ApiTestFactory factory)
{
    [Fact]
    public async Task Register_then_login_requires_email_verification_first()
    {
        var client = factory.CreateClient();
        var email = $"staff-{Guid.NewGuid():N}@ais.ac.nz";

        var (registerStatus, _) = await client.PostAsync<object>("/api/auth/register", new
        {
            email, password = "SuperSecret123!", firstName = "Ada", lastName = "Lovelace"
        });
        registerStatus.Should().Be(HttpStatusCode.OK);

        var (loginBeforeVerifyStatus, _) = await client.PostAsync<AuthResponse>("/api/auth/login", new { email, password = "SuperSecret123!" });
        loginBeforeVerifyStatus.Should().Be(HttpStatusCode.Forbidden);

        await TestSeeder.ConfirmEmailAsync(factory, email);

        var (loginStatus, loginBody) = await client.PostAsync<AuthResponse>("/api/auth/login", new { email, password = "SuperSecret123!" });
        loginStatus.Should().Be(HttpStatusCode.OK);
        loginBody!.Data!.AccessToken.Should().NotBeNullOrWhiteSpace();
        loginBody.Data.User.Roles.Should().Contain("Staff");
    }

    [Fact]
    public async Task Protected_endpoint_rejects_missing_token()
    {
        var client = factory.CreateClient();

        var (status, _) = await client.GetAsync<UserDetailDto>("/api/users/me");

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_endpoint_accepts_valid_token()
    {
        var client = factory.CreateClient();
        var email = $"staff-{Guid.NewGuid():N}@ais.ac.nz";
        await client.PostAsync<object>("/api/auth/register", new { email, password = "SuperSecret123!", firstName = "Grace", lastName = "Hopper" });
        await TestSeeder.ConfirmEmailAsync(factory, email);
        var (_, loginBody) = await client.PostAsync<AuthResponse>("/api/auth/login", new { email, password = "SuperSecret123!" });

        client.AuthenticateWith(loginBody!.Data!.AccessToken);
        var (status, body) = await client.GetAsync<UserDetailDto>("/api/users/me");

        status.Should().Be(HttpStatusCode.OK);
        body!.Data!.Email.Should().Be(email);
    }

    [Fact]
    public async Task Register_rejects_non_institutional_email_domain()
    {
        var client = factory.CreateClient();

        var (status, body) = await client.PostAsync<object>("/api/auth/register", new
        {
            email = "person@gmail.com", password = "SuperSecret123!", firstName = "A", lastName = "B"
        });

        status.Should().Be(HttpStatusCode.UnprocessableEntity);
        body!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Register_rejects_invalid_payload_with_validation_errors()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new { email = "not-an-email", password = "short" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Refresh_token_issues_a_new_access_token_and_revokes_the_old_refresh_token()
    {
        var client = factory.CreateClient();
        var email = $"staff-{Guid.NewGuid():N}@ais.ac.nz";
        await client.PostAsync<object>("/api/auth/register", new { email, password = "SuperSecret123!", firstName = "A", lastName = "B" });
        await TestSeeder.ConfirmEmailAsync(factory, email);
        var (_, loginBody) = await client.PostAsync<AuthResponse>("/api/auth/login", new { email, password = "SuperSecret123!" });

        var (refreshStatus, refreshBody) = await client.PostAsync<AuthResponse>("/api/auth/refresh", new { refreshToken = loginBody!.Data!.RefreshToken });
        refreshStatus.Should().Be(HttpStatusCode.OK);
        refreshBody!.Data!.AccessToken.Should().NotBe(loginBody.Data.AccessToken);

        var (reuseStatus, _) = await client.PostAsync<AuthResponse>("/api/auth/refresh", new { refreshToken = loginBody.Data.RefreshToken });
        reuseStatus.Should().Be(HttpStatusCode.Forbidden);
    }
}
