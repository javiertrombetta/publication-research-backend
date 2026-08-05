using FluentAssertions;
using Moq;
using PublicationSite.Api.Common;
using PublicationSite.Api.Controllers;
using PublicationSite.Api.DTOs.Common;
using PublicationSite.Api.DTOs.Containers;
using PublicationSite.Api.Services.Interfaces;
using Xunit;

namespace PublicationSite.UnitTests.Controllers;

/// <summary>
/// Who the listing answers for.
///
/// The endpoint is open to an administrator and to a coordinator, and they are owed different
/// answers: an administrator oversees the institution, a coordinator has a queue. The filter that
/// separated them used to be one the caller supplied, so a request that simply left it out came
/// back with every publication in every department, including the ones the same account is refused
/// when it opens one.
/// </summary>
public class ContainersControllerTests
{
    private static (ContainersController Controller, Mock<IContainerService> Service) Build(
        Guid userId, params string[] roles)
    {
        var service = new Mock<IContainerService>();
        service
            .Setup(s => s.GetAllAsync(It.IsAny<ContainerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<PublicationContainerDto>([], 0, 1, 10));

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(userId);
        currentUser.Setup(u => u.IsInRole(It.IsAny<string>()))
            .Returns((string role) => roles.Contains(role));

        return (new ContainersController(service.Object, currentUser.Object), service);
    }

    [Fact]
    public async Task GetAll_asked_by_a_coordinator_returns_only_their_own()
    {
        var me = Guid.NewGuid();
        var (controller, service) = Build(me, RoleNames.Coordinator);

        await controller.GetAll(new ContainerQuery());

        service.Verify(s => s.GetAllAsync(
            It.Is<ContainerQuery>(q => q.CoordinatorId == me), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Naming somebody else is not refused, it is ignored. A 400 would answer the question the
    /// filter was being used to ask: whether that coordinator exists and what they are holding.
    /// </summary>
    [Fact]
    public async Task GetAll_ignores_the_coordinator_a_coordinator_names()
    {
        var me = Guid.NewGuid();
        var somebodyElse = Guid.NewGuid();
        var (controller, service) = Build(me, RoleNames.Coordinator);

        await controller.GetAll(new ContainerQuery { CoordinatorId = somebodyElse });

        service.Verify(s => s.GetAllAsync(
            It.Is<ContainerQuery>(q => q.CoordinatorId == me), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAll_asked_by_an_administrator_returns_the_institution()
    {
        var (controller, service) = Build(Guid.NewGuid(), RoleNames.Admin);

        await controller.GetAll(new ContainerQuery());

        service.Verify(s => s.GetAllAsync(
            It.Is<ContainerQuery>(q => q.CoordinatorId == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// An administrator who is also a coordinator keeps the wider answer, and can still narrow it
    /// to one coordinator by asking. Both of those are things the administrator's screens do.
    /// </summary>
    [Fact]
    public async Task GetAll_leaves_an_administrators_own_filter_alone()
    {
        var somebodyElse = Guid.NewGuid();
        var (controller, service) = Build(Guid.NewGuid(), RoleNames.Admin, RoleNames.Coordinator);

        await controller.GetAll(new ContainerQuery { CoordinatorId = somebodyElse });

        service.Verify(s => s.GetAllAsync(
            It.Is<ContainerQuery>(q => q.CoordinatorId == somebodyElse), It.IsAny<CancellationToken>()), Times.Once);
    }
}
