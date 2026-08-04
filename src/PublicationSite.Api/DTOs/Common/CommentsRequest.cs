namespace PublicationSite.Api.DTOs.Common;

/// <summary>Generic request body for actions whose only input is a justification comment.</summary>
public record CommentsRequest(string Comments);
