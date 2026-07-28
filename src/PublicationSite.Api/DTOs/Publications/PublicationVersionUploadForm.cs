namespace PublicationSite.Api.DTOs.Publications;

public class PublicationVersionUploadForm
{
    public IFormFile File { get; set; } = null!;
    public IFormFile? SupplementaryFile { get; set; }
    public string? ReviewerNotes { get; set; }
}
