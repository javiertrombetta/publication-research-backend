using PublicationSite.Api.Entities;

namespace PublicationSite.Api.Services.Interfaces;

public record TokenPair(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAt);

public interface ITokenService
{
    Task<TokenPair> IssueTokensAsync(ApplicationUser user, IList<string> roles);
    Task<TokenPair> RefreshAsync(string refreshToken);
    Task RevokeAsync(string refreshToken);
}
