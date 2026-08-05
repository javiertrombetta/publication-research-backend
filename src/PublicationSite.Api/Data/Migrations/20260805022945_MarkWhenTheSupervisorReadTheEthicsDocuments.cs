using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PublicationSite.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MarkWhenTheSupervisorReadTheEthicsDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SupervisorDocumentsReviewedAt",
                table: "EthicsApprovals",
                type: "datetime(6)",
                nullable: true);

            // Approvals already past the supervisor keep that fact. The column is new, so every
            // row starts null, and null now means "the supervisor has not read it": without this
            // every set already with the coordinator or the head of department would be handed
            // back to the supervisor to read again.
            migrationBuilder.Sql(
                """
                UPDATE EthicsApprovals
                SET SupervisorDocumentsReviewedAt = COALESCE(CoordinatorDecisionAt, SupervisorDecisionAt, CreatedAt)
                WHERE Status = 2
                  AND NOT EXISTS (
                      SELECT 1 FROM EthicsDocuments d
                      WHERE d.EthicsApprovalId = EthicsApprovals.Id AND d.Status = 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SupervisorDocumentsReviewedAt",
                table: "EthicsApprovals");
        }
    }
}
