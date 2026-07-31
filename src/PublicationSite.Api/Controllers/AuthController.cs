using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Auth;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService, ICurrentUserService currentUser) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var emailSent = await authService.RegisterAsync(request);

        return Ok(ApiResponse.Ok(emailSent
            ? "Registration successful. Please check your email to verify your address."
            : "Your account was created, but the verification email could not be sent. " +
              "Ask an administrator to check the mail server before trying to sign in."));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await authService.LoginAsync(request);
        return Ok(ApiResponse<AuthResponse>.Ok(result));
    }

    [HttpPost("azure-sso/exchange")]
    [Authorize(AuthenticationSchemes = "AzureAd")]
    public async Task<IActionResult> AzureSsoExchange()
    {
        var result = await authService.LoginWithAzureSsoAsync(User);
        return Ok(ApiResponse<AuthResponse>.Ok(result));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
    {
        var result = await authService.RefreshAsync(request.RefreshToken);
        return Ok(ApiResponse<AuthResponse>.Ok(result));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
    {
        await authService.LogoutAsync(request.RefreshToken);
        return Ok(ApiResponse.Ok());
    }

    [HttpGet("verify-email")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyEmail([FromQuery] Guid userId, [FromQuery] string token)
    {
        await authService.VerifyEmailAsync(userId, token);
        return Ok(ApiResponse.Ok("Email verified. Your account is now enabled."));
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        await authService.ForgotPasswordAsync(request.Email);
        return Ok(ApiResponse.Ok("If that address is registered, a reset link has been sent."));
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        await authService.ResetPasswordAsync(request);
        return Ok(ApiResponse.Ok("Password reset successfully."));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        await authService.ChangePasswordAsync(currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Password changed successfully."));
    }
}
