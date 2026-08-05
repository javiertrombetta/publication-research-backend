using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PublicationSite.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WatchTheDeadlinesAndWarnBeforeThem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DueSoonWarnedAt",
                table: "ProposalSupervisorSelections",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueSoonWarnedAt",
                table: "EthicsApprovals",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OverdueReportedAt",
                table: "EthicsApprovals",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StepEnteredAt",
                table: "EthicsApprovals",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueSoonWarnedAt",
                table: "Committees",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OverdueReportedAt",
                table: "Committees",
                type: "datetime(6)",
                nullable: true);

            // Ethics reviews already under way get a clock, or nothing would ever be watched for
            // them: the sweep only looks at approvals that know when they started, and these would
            // never learn it without moving on to another step first.
            //
            // The best answer available is the newest thing that happened to the approval, since
            // that is when the step it is sitting on began. GREATEST is null if any argument is,
            // so each mark falls back to the day the approval was opened, which is also the answer
            // for an approval nothing has happened to yet.
            migrationBuilder.Sql("""
                UPDATE EthicsApprovals
                SET StepEnteredAt = GREATEST(
                    CreatedAt,
                    COALESCE(SupervisorDecisionAt, CreatedAt),
                    COALESCE(SupervisorDocumentsReviewedAt, CreatedAt),
                    COALESCE(CoordinatorDecisionAt, CreatedAt),
                    COALESCE(HeadOfDepartmentReviewedAt, CreatedAt))
                WHERE FinalDecisionAt IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DueSoonWarnedAt",
                table: "ProposalSupervisorSelections");

            migrationBuilder.DropColumn(
                name: "DueSoonWarnedAt",
                table: "EthicsApprovals");

            migrationBuilder.DropColumn(
                name: "OverdueReportedAt",
                table: "EthicsApprovals");

            migrationBuilder.DropColumn(
                name: "StepEnteredAt",
                table: "EthicsApprovals");

            migrationBuilder.DropColumn(
                name: "DueSoonWarnedAt",
                table: "Committees");

            migrationBuilder.DropColumn(
                name: "OverdueReportedAt",
                table: "Committees");
        }
    }
}
