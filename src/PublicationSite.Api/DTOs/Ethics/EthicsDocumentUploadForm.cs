namespace PublicationSite.Api.DTOs.Ethics;

public class EthicsDocumentUploadForm
{
    public string DocumentType { get; set; } = string.Empty;
    public IFormFile File { get; set; } = null!;
}
