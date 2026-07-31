using System.Security.Claims;
using PublicationSite.Api.DTOs.Auth;

namespace PublicationSite.Api.Services.Interfaces;

public interface IAuthService
{
    /// <summary>
    /// Registers the account and sends its verification email.
    /// </summary>
    /// <returns>
    /// Whether the verification email went out. False does not undo the registration — the
    /// account exists and simply cannot be verified yet, which the caller has to say out loud
    /// rather than leaving the person waiting for a message that will never arrive.
    /// </returns>
    Task<bool> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task VerifyEmailAsync(Guid userId, string token, CancellationToken cancellationToken = default);
    Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> LoginWithAzureSsoAsync(ClaimsPrincipal azureAdPrincipal, CancellationToken cancellationToken = default);
}
