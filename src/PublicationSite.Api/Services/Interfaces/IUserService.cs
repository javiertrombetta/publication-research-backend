using PublicationSite.Api.DTOs.Users;
using PublicationSite.Api.DTOs.Common;

namespace PublicationSite.Api.Services.Interfaces;

public interface IUserService
{
    /// <summary>
    /// The directory, one page at a time. Paged like every other listing: an institution's whole
    /// user table is not a screenful, and it was the last list here still returned entire.
    /// </summary>
    Task<PagedResult<UserListItemDto>> GetAllAsync(
        string? role, string? status, string? search, PageRequest paging, bool availableOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>Sets whether this person is currently taking work on. Theirs alone to change.</summary>
    Task SetAvailabilityAsync(Guid userId, bool isAvailable, CancellationToken cancellationToken = default);
    /// <summary>Records which theme this person prefers, so it follows them to another machine.</summary>
    Task SetThemeAsync(Guid userId, string theme, CancellationToken cancellationToken = default);

    Task<UserDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>
    /// Creates an account and emails its owner a link to set a password.
    /// </summary>
    /// <returns>
    /// The account, and whether that email went out. A false flag leaves a usable account that
    /// nobody can sign in to yet, so the administrator has to be told, but it is not a failure of
    /// the creation itself, and reporting it as one would leave the address taken by an account the
    /// caller believed had not been made.
    /// </returns>
    Task<(UserDetailDto User, bool PasswordEmailSent)> CreateAsync(
        CreateUserRequest request, Guid actingAdminId, CancellationToken cancellationToken = default);
    Task<UserDetailDto> UpdateAsync(Guid id, UpdateUserRequest request, Guid actingAdminId, CancellationToken cancellationToken = default);
    Task ChangeRoleAsync(Guid id, ChangeUserRoleRequest request, Guid actingAdminId, CancellationToken cancellationToken = default);
    Task EnableAsync(Guid id, string comments, Guid actingAdminId, CancellationToken cancellationToken = default);
    Task DisableAsync(Guid id, string comments, Guid actingAdminId, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(Guid id, string comments, Guid actingAdminId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Admin-only account deletion. The row itself is kept and stripped rather than removed: every
    /// reference to a user is a Restrict foreign key, so removing the row is refused by the
    /// database, and forcing it through would mean detaching published research from its author.
    /// The account is instead emptied of personal data and locked out, and the deletion is recorded
    /// in the audit trail with the administrator's reason.
    /// </summary>
    Task DeleteAsync(Guid id, string comments, Guid actingAdminId, CancellationToken cancellationToken = default);

    Task<UserDetailDto> GetOwnProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<UserDetailDto> UpdateOwnProfileAsync(Guid userId, UpdateMyProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>Replaces the user's own profile photo, discarding any previous one.</summary>
    Task<UserDetailDto> SetOwnProfilePhotoAsync(Guid userId, Stream content, string fileName, long lengthBytes, CancellationToken cancellationToken = default);

    /// <summary>Removes the user's own profile photo. No-op when they don't have one.</summary>
    Task<UserDetailDto> RemoveOwnProfilePhotoAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Opens any user's profile photo for streaming. Throws NotFoundException when there is none.</summary>
    Task<(Stream Content, string ContentType)> OpenProfilePhotoAsync(Guid userId, CancellationToken cancellationToken = default);
}
