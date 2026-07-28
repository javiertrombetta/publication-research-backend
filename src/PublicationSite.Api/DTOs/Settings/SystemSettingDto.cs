namespace PublicationSite.Api.DTOs.Settings;

public record SystemSettingDto(Guid Id, string Key, string Value, string? Description, DateTime UpdatedAt);

public record SetSystemSettingRequest(string Key, string Value, string? Description);
