using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PublicationSite.Api.Data.Migrations
{
    /// <summary>
    /// The role's new name inside a setting that stores role names as its value.
    ///
    /// Renaming the role and the key it is counted under was not enough: which roles an
    /// institution draws its committees from is a comma-separated list of role names, and the
    /// reader drops any entry it does not recognise. So a deployment that had ever saved that
    /// setting came back with the reviewer quietly filtered out of it, and appointing one to a
    /// committee was refused with a message about a role the institution does not draw on.
    /// </summary>
    public partial class RenameReviewerInsideStoredSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE SystemSettings
                SET Value = REPLACE(Value, 'InternalCommitteeMember', 'Reviewer')
                WHERE `Key` = 'committee.candidate-roles';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE SystemSettings
                SET Value = REPLACE(Value, 'Reviewer', 'InternalCommitteeMember')
                WHERE `Key` = 'committee.candidate-roles';
                """);
        }
    }
}
