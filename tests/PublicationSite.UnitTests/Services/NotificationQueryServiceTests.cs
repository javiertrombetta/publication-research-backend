using FluentAssertions;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.Entities;
using PublicationSite.Api.Enums;
using PublicationSite.Api.Services.Implementations;
using PublicationSite.UnitTests.TestSupport;
using Xunit;

namespace PublicationSite.UnitTests.Services;

public class NotificationQueryServiceTests : IDisposable
{
    private readonly SqliteDbContextFactory _fixture = new();
    private readonly NotificationQueryService _sut;

    public NotificationQueryServiceTests()
    {
        _sut = new NotificationQueryService(_fixture.ServiceContext);
    }

    public void Dispose() => _fixture.Dispose();

    private Notification AddNotification(ApplicationUser user, bool isRead = false)
    {
        var notification = new Notification
        {
            UserId = user.Id, Type = NotificationType.Generic, Title = "Title", Message = "Message", IsRead = isRead
        };
        _fixture.Context.Notifications.Add(notification);
        _fixture.Context.SaveChanges();
        return notification;
    }

    [Fact]
    public async Task GetForUserAsync_returns_only_that_users_notifications()
    {
        var user = TestDataBuilder.User(_fixture.Context);
        var otherUser = TestDataBuilder.User(_fixture.Context);
        AddNotification(user);
        AddNotification(otherUser);

        var result = await _sut.GetForUserAsync(user.Id, unreadOnly: null);

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task GetForUserAsync_unreadOnly_filters_read_notifications()
    {
        var user = TestDataBuilder.User(_fixture.Context);
        AddNotification(user, isRead: true);
        var unread = AddNotification(user, isRead: false);

        var result = await _sut.GetForUserAsync(user.Id, unreadOnly: true);

        result.Should().ContainSingle(n => n.Id == unread.Id);
    }

    [Fact]
    public async Task MarkAsReadAsync_sets_is_read()
    {
        var user = TestDataBuilder.User(_fixture.Context);
        var notification = AddNotification(user);

        await _sut.MarkAsReadAsync(notification.Id, user.Id);

        (await _sut.GetForUserAsync(user.Id, unreadOnly: true)).Should().BeEmpty();
    }

    [Fact]
    public async Task MarkAsReadAsync_rejects_non_owner()
    {
        var owner = TestDataBuilder.User(_fixture.Context);
        var stranger = TestDataBuilder.User(_fixture.Context);
        var notification = AddNotification(owner);

        var act = () => _sut.MarkAsReadAsync(notification.Id, stranger.Id);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task MarkAsReadAsync_throws_when_notification_missing()
    {
        var user = TestDataBuilder.User(_fixture.Context);

        var act = () => _sut.MarkAsReadAsync(Guid.NewGuid(), user.Id);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
