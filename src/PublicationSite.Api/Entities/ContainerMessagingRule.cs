namespace PublicationSite.Api.Entities;

/// <summary>
/// An administrator's decision about messaging on one publication, overriding what the institution
/// has decided in general.
///
/// One publication sometimes needs different handling from the rest: a supervision that has gone
/// wrong, a complaint being investigated, a student who has asked not to be contacted by somebody
/// in particular. Changing the institution's settings for that is changing them for everybody.
///
/// Three kinds of rule, told apart by which of the two targets is set:
///
///   both null      the whole publication. Nobody writes anything on it.
///   TargetRole     everybody holding that role on this publication.
///   TargetUserId   one named person.
///
/// A rule is symmetrical: somebody it silences neither writes nor is written to here. A one-way
/// rule would leave the other party sending into nothing, which reads as the site being broken.
/// </summary>
public class ContainerMessagingRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PublicationContainerId { get; set; }
    public PublicationContainer PublicationContainer { get; set; } = null!;

    /// <summary>A role name from RoleNames, or null when this rule is about one person or the whole publication.</summary>
    public string? TargetRole { get; set; }

    /// <summary>One person, or null when this rule is about a role or the whole publication.</summary>
    public Guid? TargetUserId { get; set; }
    public ApplicationUser? TargetUser { get; set; }

    /// <summary>
    /// What the rule says. False is the usual case, since the institution's settings already say
    /// who may write; true exists so one publication can let somebody through whom the institution
    /// has generally shut out.
    /// </summary>
    public bool Allowed { get; set; }

    /// <summary>
    /// Why. Not optional: a rule that silences somebody on a publication is a decision another
    /// administrator will find later and need to understand, and "no reason given" is not an
    /// answer anybody can act on.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    public Guid SetByUserId { get; set; }
    public ApplicationUser SetByUser { get; set; } = null!;
    public DateTime SetAt { get; set; } = DateTime.UtcNow;
}
