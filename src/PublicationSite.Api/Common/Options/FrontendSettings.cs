namespace PublicationSite.Api.Common.Options;

public class FrontendSettings
{
    public const string SectionName = "Frontend";

    /// <summary>Base URL of the client application, used to build email links (verification, password reset).</summary>
    public string BaseUrl { get; set; } = "http://localhost:3000";
}
