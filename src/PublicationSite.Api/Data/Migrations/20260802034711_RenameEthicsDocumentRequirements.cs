using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PublicationSite.Api.Data.Migrations
{
    /// <summary>
    /// Names the three ethics documents after the forms students are actually given.
    ///
    /// The upload slots and the templates offered on the same screen had different names for the
    /// same three documents, so somebody who downloaded a Proposal Form had to work out for
    /// themselves that it belonged in the slot labelled Ethics Application Form. They read as one
    /// list now.
    ///
    /// A rename rather than new rows: every document already uploaded points at one of these by id,
    /// and each approval carries its own snapshot of what it was asked for. Replacing them would
    /// leave that history attached to nothing.
    /// </summary>
    public partial class RenameEthicsDocumentRequirements : Migration
    {
        private const string ParticipantInformationSheetId = "7d3f0a1e-4c62-4a9b-9f21-0c5b8e3a1d10";
        private const string ProposalFormId = "7d3f0a1e-4c62-4a9b-9f21-0c5b8e3a1d11";
        private const string ConsentFormGuidanceId = "7d3f0a1e-4c62-4a9b-9f21-0c5b8e3a1d12";

        private static void Rename(
            MigrationBuilder migrationBuilder, string id, string name, string description, int sortOrder) =>
            migrationBuilder.Sql(
                $"""
                 UPDATE EthicsDocumentRequirements
                 SET Name = '{name}',
                     Description = '{description.Replace("'", "''")}',
                     SortOrder = {sortOrder},
                     UpdatedAt = UTC_TIMESTAMP(6)
                 WHERE Id = '{id}';
                 """);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Rename(migrationBuilder, ParticipantInformationSheetId, "Participant Information Sheet",
                "The sheet given to participants explaining what taking part involves.", 1);

            Rename(migrationBuilder, ProposalFormId, "Proposal Form",
                "The completed proposal submitted to the ethics committee.", 2);

            Rename(migrationBuilder, ConsentFormGuidanceId, "Consent Form Guidance",
                "The consent form participants sign, prepared from the institutional guidance.", 3);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Rename(migrationBuilder, ParticipantInformationSheetId, "Ethics Approval Certificate",
                "The certificate issued by the ethics committee once approval is granted.", 1);

            Rename(migrationBuilder, ProposalFormId, "Ethics Application Form",
                "The completed application submitted to the ethics committee.", 2);

            Rename(migrationBuilder, ConsentFormGuidanceId, "Participant Consent Form",
                "The consent form given to participants.", 3);
        }
    }
}
