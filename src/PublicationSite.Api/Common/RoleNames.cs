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
