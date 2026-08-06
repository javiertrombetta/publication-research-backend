using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PublicationSite.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class GuardTheWorkflowRowsAgainstTwoWritersAtOnce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyStamp",
                table: "ResearchProposals",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyStamp",
                table: "Publications",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyStamp",
                table: "PublicationContainers",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyStamp",
                table: "ProposalSupervisorSelections",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyStamp",
                table: "EthicsApprovals",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyStamp",
                table: "Committees",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyStamp",
                table: "CommitteeMembers",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "ResearchProposals");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "Publications");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "PublicationContainers");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "ProposalSupervisorSelections");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "EthicsApprovals");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "Committees");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "CommitteeMembers");
        }
    }
}
