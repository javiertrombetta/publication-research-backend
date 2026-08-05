namespace PublicationSite.Api.Common.Options;

public class FileStorageSettings
{
    public const string SectionName = "FileStorage";

    /// <summary>Absolute or relative-to-content-root path where uploaded files are stored.</summary>
    public string RootPath { get; set; } = "App_Data/uploads";

    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024;

    public static readonly string[] DefaultDocumentExtensions = [".pdf", ".doc", ".docx", ".zip"];

    public static readonly string[] DefaultImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    /// <summary>
    /// Left empty deliberately. The configuration binder adds to a collection property instead of
    /// replacing it, so a default written here would be appended to whatever appsettings says:
    /// the same extension would be listed twice in the message a refused upload produces, and a
    /// deployment that narrowed the list would still accept everything it thought it had removed.
    /// Read <see cref="DocumentExtensions"/>, which supplies the defaults once binding is done.
    /// </summary>
    public string[] AllowedExtensions { get; set; } = [];

    /// <summary>
    /// Kept separate from AllowedExtensions so profile photos can be images without also
    /// letting an image be uploaded as an ethics document or a research paper. Empty for the same
    /// reason; read <see cref="ImageExtensions"/>.
    /// </summary>
    public string[] AllowedImageExtensions { get; set; } = [];

    /// <summary>What may be uploaded as a document, falling back to the built-in list.</summary>
    public IReadOnlyList<string> DocumentExtensions =>
        AllowedExtensions.Length > 0 ? AllowedExtensions : DefaultDocumentExtensions;

    /// <summary>What may be uploaded as a profile photo, falling back to the built-in list.</summary>
    public IReadOnlyList<string> ImageExtensions =>
        AllowedImageExtensions.Length > 0 ? AllowedImageExtensions : DefaultImageExtensions;

    public long MaxProfilePhotoBytes { get; set; } = 5 * 1024 * 1024;
}
