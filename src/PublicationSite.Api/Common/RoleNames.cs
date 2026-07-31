namespace PublicationSite.Api.Common;

/// <summary>
/// Canonical role names used with ASP.NET Core Identity and [Authorize(Roles = ...)].
/// "Staff" is a transitional role: assigned automatically to @ais.ac.nz accounts until
/// an Admin grants one of the operational staff roles below.
/// </summary>
public static class RoleNames
{
    public const string Admin = "Admin";
    public const string HeadOfDepartment = "HeadOfDepartment";
    public const string Coordinator = "Coordinator";
    public const string Supervisor = "Supervisor";
    public const string InternalCommitteeMember = "InternalCommitteeMember";
    public const string ExternalCommitteeMember = "ExternalCommitteeMember";
    public const string Student = "Student";
    public const string Staff = "Staff";

    /// <summary>
    /// Not an Identity role: the answer to "whose turn is it" when the turn belongs to the
    /// evaluation committee as a body rather than to any one person. Two roles sit on a committee,
    /// so neither of their names would be the truthful answer.
    /// </summary>
    public const string EvaluationCommittee = "EvaluationCommittee";

    public static readonly string[] All =
    [
        Admin, HeadOfDepartment, Coordinator, Supervisor,
        InternalCommitteeMember, ExternalCommitteeMember, Student, Staff
    ];

    public static readonly string[] CommitteeRoles =
    [
        InternalCommitteeMember, ExternalCommitteeMember
    ];
}
