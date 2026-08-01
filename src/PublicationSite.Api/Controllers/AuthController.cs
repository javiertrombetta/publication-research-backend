using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PublicationSite.Api.Common;
using PublicationSite.Api.DTOs.Auth;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Controllers;

/// <summary>
/// Getting in and staying in: registering, signing in, refreshing an access token, and the two
/// recovery paths for a forgotten or expired password. Most of these are open without a session,
/// because somebody who cannot sign in is precisely who needs them.
///
/// Whether registration is open at all is a setting, and the role a new account receives is derived
/// from the email domain rather than chosen by the person registering.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>
    /// Creates an account from an institutional address, where an administrator has left
    /// self-registration open. The role follows the domain — a student address makes a student
    /// — so nobody chooses what they are.
    /// </summary>
    /// <remarks>
    /// The account cannot be used until the address is confirmed. If the verification email
    /// cannot be sent the account is still created and the response says so, rather than
    /// failing something that has already happened.
    /// </remarks>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="403">Signed in, but this record is not yours to see or act on.</response>
    /// <response code="404">No department with that id.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var emailSent = await authService.RegisterAsync(request);

        return Ok(ApiResponse.Ok(emailSent
            ? "Registration successful. Please check your email to verify your address."
            : "Your account was created, but the verification email could not be sent. " +
              "Ask an administrator to check the mail server before trying to sign in."));
    }

    /// <summary>
    /// Exchanges an email and password for an access token and a refresh token.
    /// </summary>
    /// <remarks>
    /// Refuses a disabled account, an unconfirmed address and an expired password with a
    /// message saying which, since each needs a different thing done about it. Repeated wrong
    /// passwords lock the account for a while — how many and how long are an administrator's
    /// settings.
    /// </remarks>
    /// <response code="200">The auth response.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="403">Signed in, but this record is not yours to see or act on.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await authService.LoginAsync(request);
        return Ok(ApiResponse<AuthResponse>.Ok(result));
    }

    /// <summary>
    /// Trades a Microsoft Entra token for this application's own, so somebody who has already
    /// signed in to the institution does not sign in again here. Available only where a tenant
    /// is configured.
    /// </summary>
    /// <remarks>
    /// External committee members are outside the tenant by definition and always sign in with
    /// a password, whatever this is set to.
    /// </remarks>
    /// <response code="200">The auth response.</response>
    /// <response code="400">The request did not pass validation.</response>
    /// <response code="403">Signed in, but this record is not yours to see or act on.</response>
    /// <response code="422">Understood, and refused: the workflow does not allow this at the point it has reached.</response>
    [HttpPost("azure-sso/exchange")]
    [Authorize(AuthenticationSchemes = "AzureAd")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AzureSsoExchange()
    {
        var result = await authService.LoginWithAzureSsoAsync(User);
        return Ok(ApiResponse<AuthResponse>.Ok(result));
    }

    /// <summary>
    /// Issues a new access token from a refresh token. Access tokens are deliberately short —
    /// they cannot be withdrawn before they expire — and this is what keeps a session alive
    /// without holding a long-lived one.
    /// </summary>
    /// <response code="200">The auth response.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="403">Signed in, but this record is not yours to see or act on.</response>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
    {
        var result = await authService.RefreshAsync(request.RefreshToken);
        return Ok(ApiResponse<AuthResponse>.Ok(result));
    }

    /// <summary>
    /// Retires the refresh token, so the session cannot be resumed. The access token already
    /// issued stays valid until it expires, which is the reason it is short.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
    {
        await authService.LogoutAsync(request.RefreshToken);
        return Ok(ApiResponse.Ok());
    }

    /// <summary>
    /// Confirms an address from the link in the verification email, which is what lets the
    /// account sign in.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation.</response>
    /// <response code="404">No application user with that id.</response>
    [HttpGet("verify-email")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VerifyEmail([FromQuery] Guid userId, [FromQuery] string token)
    {
        await authService.VerifyEmailAsync(userId, token);
        return Ok(ApiResponse.Ok("Email verified. Your account is now enabled."));
    }

    /// <summary>
    /// Starts a password reset. Answers the same way whether or not the address is registered:
    /// telling a stranger which of the two it is would turn this into a way of discovering who
    /// has an account.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        await authService.ForgotPasswordAsync(request.Email);
        return Ok(ApiResponse.Ok("If that address is registered, a reset link has been sent."));
    }

    /// <summary>
    /// Sets a new password from the emailed link. The link is single-use and the new password
    /// must meet the rules an administrator has configured.
    /// </summary>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="404">No application user with that id.</response>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        await authService.ResetPasswordAsync(request);
        return Ok(ApiResponse.Ok("Password reset successfully."));
    }

    /// <summary>
    /// Changes the signed-in user's own password, having checked the current one.
    /// </summary>
    /// <remarks>
    /// Wrong-current-password counts towards the same lockout as signing in does: an attacker
    /// with a borrowed unlocked laptop attacks this form, not the sign-in page.
    /// </remarks>
    /// <response code="200">Done. The envelope carries a message saying what changed; there is no data with it.</response>
    /// <response code="400">The request did not pass validation. Which field, and why, comes back as a problem document rather than the usual envelope.</response>
    /// <response code="403">Signed in, but this record is not yours to see or act on.</response>
    /// <response code="404">No application user with that id.</response>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        await authService.ChangePasswordAsync(currentUser.UserId, request);
        return Ok(ApiResponse.Ok("Password changed successfully."));
    }
}
