namespace PublicationSite.Api.Common.Options;

public class MailSettings
{
    public const string SectionName = "Mail";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "AIS Research Publication Site";
    public bool UseSsl { get; set; } = true;
}
