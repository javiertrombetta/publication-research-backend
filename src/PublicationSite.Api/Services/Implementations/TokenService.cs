using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PublicationSite.Api.Common;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Common.Options;
using PublicationSite.Api.Data;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Services.Interfaces;

namespace PublicationSite.Api.Services.Implementations;

public class TokenService(
    ApplicationDbContext db,
    ISystemSettingsProvider settings,
    IOptions<JwtSettings> jwtOptions) : ITokenService
{
    private readonly JwtSettings _settings = jwtOptions.Value;

    /// <summary>
    /// How long tokens last, as an administrator has set it. Only the lifetimes are configurable.
    /// The issuer, audience and signing key stay in configuration, because those are deployment
    /// facts and changing one from a web form would invalidate every token in circulation and lock
    /// the administrator out of the screen they changed it on.
    /// </summary>
    private Task<int> AccessTokenMinutesAsync() =>
        settings.GetIntAsync(SettingKeys.AccessTokenMinutes, _settings.AccessTokenMinutes);

    private Task<int> RefreshTokenDaysAsync() =>
        settings.GetIntAsync(SettingKeys.RefreshTokenDays, _settings.RefreshTokenDays);

    public async Task<TokenPair> IssueTokensAsync(ApplicationUser user, IList<string> roles)
    {
        var accessTokenExpiresAt = DateTime.UtcNow.AddMinutes(await AccessTokenMinutesAsync());
        var accessToken = GenerateAccessToken(user, roles, accessTokenExpiresAt);
        var refreshToken = await GenerateAndStoreRefreshTokenAsync(user.Id);

        return new TokenPair(accessToken, refreshToken.Token, accessTokenExpiresAt);
    }

    public async Task<TokenPair> RefreshAsync(string refreshToken)
    {
        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.Token == refreshToken);

        if (stored is null || !stored.IsActive)
        {
            throw new ForbiddenException("Invalid or expired refresh token.");
        }

        var roles = await db.UserRoles
            .Where(ur => ur.UserId == stored.UserId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
            .ToListAsync();

        var accessTokenExpiresAt = DateTime.UtcNow.AddMinutes(await AccessTokenMinutesAsync());
        var accessToken = GenerateAccessToken(stored.User, roles, accessTokenExpiresAt);

        var newRefreshToken = await GenerateAndStoreRefreshTokenAsync(stored.UserId);
        stored.RevokedAt = DateTime.UtcNow;
        stored.ReplacedByToken = newRefreshToken.Token;
        await db.SaveChangesAsync();

        return new TokenPair(accessToken, newRefreshToken.Token, accessTokenExpiresAt);
    }

    public async Task RevokeAsync(string refreshToken)
    {
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.Token == refreshToken);
        if (stored is null || !stored.IsActive)
        {
            return;
        }

        stored.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private string GenerateAccessToken(ApplicationUser user, IList<string> roles, DateTime expiresAt)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(ClaimTypes.GivenName, user.FirstName),
            new(ClaimTypes.Surname, user.LastName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var key = new SymmetricSecurityKey(Convert.FromBase64String(_settings.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<RefreshToken> GenerateAndStoreRefreshTokenAsync(Guid userId)
    {
        var refreshToken = new RefreshToken
        {
            UserId = userId,
            Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            ExpiresAt = DateTime.UtcNow.AddDays(await RefreshTokenDaysAsync())
        };

        db.RefreshTokens.Add(refreshToken);
        await db.SaveChangesAsync();
        return refreshToken;
    }
}
