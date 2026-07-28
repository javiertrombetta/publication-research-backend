namespace PublicationSite.Api.Common.Options;

public class FileStorageSettings
{
    public const string SectionName = "FileStorage";

    /// <summary>Absolute or relative-to-content-root path where uploaded files are stored.</summary>
    public string RootPath { get; set; } = "App_Data/uploads";

    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024;

    public string[] AllowedExtensions { get; set; } =
        [".pdf", ".doc", ".docx", ".zip"];
}
