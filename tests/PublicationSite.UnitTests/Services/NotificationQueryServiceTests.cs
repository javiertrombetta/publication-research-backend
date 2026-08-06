using FluentAssertions;
using PublicationSite.Api.Common.Exceptions;
using PublicationSite.Api.DTOs.Common;
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

    /// <summary>Everything of this person's, however long the list is, for the tests that predate paging.</summary>
    private Task<PagedResult<PublicationSite.Api.DTOs.Notifications.NotificationDto>> GetAll(
        Guid userId, bool? unreadOnly = null, string? search = null, int page = 1, int pageSize = 100) =>
        _sut.GetForUserAsync(userId, unreadOnly, search, new PageRequest { Page = page, PageSize = pageSize });

    private Notification AddNotification(
        ApplicationUser user, bool isRead = false, string title = "Title", string message = "Message")
    {
        var notification = new Notification
        {
            UserId = user.Id, Type = NotificationType.Generic, Title = title, Message = message, IsRead = isRead
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

        var result = await GetAll(user.Id);

        result.Items.Should().ContainSingle();
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetForUserAsync_unreadOnly_filters_read_notifications()
    {
        var user = TestDataBuilder.User(_fixture.Context);
        AddNotification(user, isRead: true);
        var unread = AddNotification(user, isRead: false);

        var result = await GetAll(user.Id, unreadOnly: true);

        result.Items.Should().ContainSingle(n => n.Id == unread.Id);
    }

    [Fact]
    public async Task MarkAsReadAsync_sets_is_read()
    {
        var user = TestDataBuilder.User(_fixture.Context);
        var notification = AddNotification(user);

        await _sut.MarkAsReadAsync(notification.Id, user.Id);

        (await GetAll(user.Id, unreadOnly: true)).Items.Should().BeEmpty();
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

    [Fact]
    public async Task GetForUserAsync_returns_one_page_and_says_how_many_there_are()
    {
        var user = TestDataBuilder.User(_fixture.Context);
        for (var i = 0; i < 25; i++)
        {
            AddNotification(user);
        }

        var page = await GetAll(user.Id, pageSize: 10, page: 3);

        page.Items.Should().HaveCount(5);
        page.TotalCount.Should().Be(25);
        page.Page.Should().Be(3);
    }

    [Fact]
    public async Task GetForUserAsync_searches_the_title_and_the_message()
    {
        var user = TestDataBuilder.User(_fixture.Context);
        var byTitle = AddNotification(user, title: "Ethics approval completed", message: "Nothing to do.");
        var byMessage = AddNotification(user, title: "A decision was made", message: "Ethics approval was granted for your study.");
        AddNotification(user, title: "A proposal was accepted", message: "Congratulations.");

        // Capitalised as the notifications are. Whether a lowercase term would find them is the
        // database's collation, not this code: MySQL is utf8mb4_0900_ai_ci and matches either way,
        // and these tests run on SQLite, which does not. Checked against the running MySQL rather
        // than asserted here, where the answer would be the wrong one.
        var result = await GetAll(user.Id, search: "Ethics");

        result.Items.Select(n => n.Id).Should().BeEquivalentTo(new[] { byTitle.Id, byMessage.Id });
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetForUserAsync_searches_within_the_unread_ones_only_when_asked()
    {
        var user = TestDataBuilder.User(_fixture.Context);
        AddNotification(user, isRead: true, title: "Ethics approval completed");
        var unread = AddNotification(user, isRead: false, title: "Ethics revision requested");

        var result = await GetAll(user.Id, unreadOnly: true, search: "Ethics");

        result.Items.Should().ContainSingle(n => n.Id == unread.Id);
    }

    [Fact]
    public async Task GetOneAsync_returns_the_notification_to_its_owner()
    {
        var user = TestDataBuilder.User(_fixture.Context);
        var notification = AddNotification(user);

        var result = await _sut.GetOneAsync(notification.Id, user.Id);

        result!.Id.Should().Be(notification.Id);
    }

    [Fact]
    public async Task GetOneAsync_says_nothing_to_anybody_else()
    {
        var owner = TestDataBuilder.User(_fixture.Context);
        var stranger = TestDataBuilder.User(_fixture.Context);
        var notification = AddNotification(owner);

        var result = await _sut.GetOneAsync(notification.Id, stranger.Id);

        result.Should().BeNull();
    }
}
