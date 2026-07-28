using PublicationSite.Api.DTOs.Users;

namespace PublicationSite.Api.Services.Interfaces;

public interface IUserService
{
    Task<IReadOnlyList<UserListItemDto>> GetAllAsync(string? role, string? status, string? search, CancellationToken cancellationToken = default);
    Task<UserDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<UserDetailDto> CreateAsync(CreateUserRequest request, Guid actingAdminId, CancellationToken cancellationToken = default);
    Task<UserDetailDto> UpdateAsync(Guid id, UpdateUserRequest request, Guid actingAdminId, CancellationToken cancellationToken = default);
    Task ChangeRoleAsync(Guid id, ChangeUserRoleRequest request, Guid actingAdminId, CancellationToken cancellationToken = default);
    Task EnableAsync(Guid id, string comments, Guid actingAdminId, CancellationToken cancellationToken = default);
    Task DisableAsync(Guid id, string comments, Guid actingAdminId, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(Guid id, string comments, Guid actingAdminId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, string comments, Guid actingAdminId, CancellationToken cancellationToken = default);

    Task<UserDetailDto> GetOwnProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<UserDetailDto> UpdateOwnProfileAsync(Guid userId, UpdateMyProfileRequest request, CancellationToken cancellationToken = default);
}
