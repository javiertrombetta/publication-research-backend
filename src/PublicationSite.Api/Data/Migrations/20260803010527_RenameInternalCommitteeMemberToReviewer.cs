using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PublicationSite.Api.Data.Migrations
{
    /// <summary>
    /// The internal committee member is called a Reviewer.
    ///
    /// Three things carry that name and only one of them is a column, so the other two are written
    /// out by hand. The role is a row in Identity's own table and every account's membership points
    /// at it by id, so renaming the row renames the role for everybody at once and no membership
    /// has to move. The committee size lives under a settings key, and a key nobody writes to is a
    /// setting that quietly reverts to its default: an institution that had asked for three
    /// reviewers would have found itself back on two.
    ///
    /// The member type on a committee seat is stored as a number, so renaming it in the enum costs
    /// nothing here.
    /// </summary>
    public partial class RenameInternalCommitteeMemberToReviewer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RequiredInternalCommitteeMembers",
                table: "PublicationContainers",
                newName: "RequiredReviewerMembers");

            migrationBuilder.Sql(
                """
                UPDATE Roles
                SET Name = 'Reviewer', NormalizedName = 'REVIEWER'
                WHERE NormalizedName = 'INTERNALCOMMITTEEMEMBER';
                """);

            migrationBuilder.Sql(
                """
                UPDATE SystemSettings
                SET `Key` = 'committee.reviewer-members'
                WHERE `Key` = 'committee.internal-members';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RequiredReviewerMembers",
                table: "PublicationContainers",
                newName: "RequiredInternalCommitteeMembers");

            migrationBuilder.Sql(
                """
                UPDATE Roles
                SET Name = 'InternalCommitteeMember', NormalizedName = 'INTERNALCOMMITTEEMEMBER'
                WHERE NormalizedName = 'REVIEWER';
                """);

            migrationBuilder.Sql(
                """
                UPDATE SystemSettings
                SET `Key` = 'committee.internal-members'
                WHERE `Key` = 'committee.reviewer-members';
                """);
        }
    }
}
