namespace PublicationSite.Api.Entities;

public class ResearchArea
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    public ICollection<StudentProfile> Students { get; set; } = [];
    public ICollection<Publication> Publications { get; set; } = [];
}
