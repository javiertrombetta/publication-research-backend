using PublicationSite.Api.Enums;

namespace PublicationSite.Api.Entities;

/// <summary>
/// The student's Yes/No/Unsure ethics declaration for a Container. One-to-one:
/// the student may revisit "Unsure" repeatedly but only one final response is stored.
/// </summary>
public class EthicsDeclaration
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PublicationContainerId { get; set; }
    public PublicationContainer PublicationContainer { get; set; } = null!;

    public EthicsStudentResponse StudentResponse { get; set; }
    public DateTime DecidedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The twenty screening questions and what the student answered to each, as JSON.
    ///
    /// They were asked and thrown away: the student worked through them and declared, and the
    /// people who then rule on that declaration saw the one-word answer with none of the working.
    ///
    /// The question is stored beside its answer rather than a number pointing at a list. A list
    /// edited later would silently re-file every answer ever given against a different sentence,
    /// and a decision has to still read as the questions read when it was made.
    ///
    /// JSON in a column because it is one thing: a form as filled in, read back whole, never
    /// queried by part. Null for declarations made before this was kept.
    /// </summary>
    public string? ScreeningAnswers { get; set; }
}
