namespace PublicationSite.Api.DTOs.Ethics;

public class EthicsDocumentUploadForm
{
    public string DocumentType { get; set; } = string.Empty;
    public IFormFile File { get; set; } = null!;
}

/// <summary>The same, plus the reason an administrator has to give for doing it themselves.</summary>
public class AdminEthicsDocumentUploadForm : EthicsDocumentUploadForm
{
    public string Comments { get; set; } = string.Empty;
}

/// <summary>A paper version put on by an administrator, with the reason.</summary>
public class AdminPaperVersionUploadForm
{
    public IFormFile File { get; set; } = null!;
    public string Comments { get; set; } = string.Empty;
}
