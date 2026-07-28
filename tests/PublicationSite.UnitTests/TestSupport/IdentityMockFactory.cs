using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PublicationSite.Api.Entities;

namespace PublicationSite.UnitTests.TestSupport;

/// <summary>
/// UserManager/SignInManager expose their members as virtual specifically so they can be
/// mocked directly (the store/dependencies passed to the constructor are never exercised
/// as long as tests only Setup/Verify the virtual members they call).
/// </summary>
public static class IdentityMockFactory
{
    public static Mock<UserManager<ApplicationUser>> MockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    public static Mock<SignInManager<ApplicationUser>> MockSignInManager(UserManager<ApplicationUser> userManager)
    {
        var contextAccessor = new Mock<IHttpContextAccessor>();
        contextAccessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext());

        var claimsFactory = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        var options = Options.Create(new IdentityOptions());
        var schemes = new Mock<IAuthenticationSchemeProvider>();

        return new Mock<SignInManager<ApplicationUser>>(
            userManager, contextAccessor.Object, claimsFactory.Object, options,
            NullLogger<SignInManager<ApplicationUser>>.Instance, schemes.Object, null!);
    }

    public static Mock<RoleManager<ApplicationRole>> MockRoleManager()
    {
        var store = new Mock<IRoleStore<ApplicationRole>>();
        return new Mock<RoleManager<ApplicationRole>>(store.Object, null!, null!, null!, null!);
    }
}
