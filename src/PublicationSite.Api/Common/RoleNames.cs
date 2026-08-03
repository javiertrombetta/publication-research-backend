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
    public const string Reviewer = "Reviewer";
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
        Reviewer, ExternalCommitteeMember, Student, Staff
    ];

    public static readonly string[] CommitteeRoles =
    [
        Reviewer, ExternalCommitteeMember
    ];

    /// <summary>
    /// The roles that mean a job here: everyone the system can choose for something.
    ///
    /// Two are missing and for different reasons. A student is the subject of the work rather than
    /// somebody it is handed to. And Staff is the placeholder an institutional address is given on
    /// the way in, before an administrator says what the person actually is: it is not a job, so
    /// there is nothing to offer whoever holds it and nothing about them worth asking.
    ///
    /// Named once because several questions turn on it, and answering them separately is how a
    /// placeholder account ends up being offered work in one place and not another.
    /// </summary>
    public static readonly string[] Operational =
    [
        Admin, HeadOfDepartment, Coordinator, Supervisor,
        Reviewer, ExternalCommitteeMember
    ];

    /// <summary>
    /// Everyone who may sit on an evaluation committee, and therefore everyone the committee
    /// screens have to let in.
    ///
    /// Two exclusions, for two different reasons. A committee judges a student's work, so its
    /// members cannot be drawn from the people whose work is being judged. And Staff is the
    /// placeholder an institutional account holds before an administrator says what it is: it is
    /// not a job, so there is nobody there to ask yet.
    ///
    /// Anyone else can be appointed. Holding a committee-member role is not the entry ticket:
    /// supervisors, coordinators and heads of department are exactly who an institution draws its
    /// evaluators from, and a committee they could be appointed to but never open or vote on was a
    /// committee that could never reach its required number of approvals.
    /// </summary>
    public static readonly string[] CommitteeEligible = Operational;

    /// <summary>The same list, for [Authorize(Roles = ...)], which takes one comma-separated string.</summary>
    public const string CommitteeEligibleRoles =
        $"{Admin},{HeadOfDepartment},{Coordinator},{Supervisor},{Reviewer},{ExternalCommitteeMember}";
}
