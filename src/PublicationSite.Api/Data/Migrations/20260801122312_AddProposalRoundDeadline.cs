using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PublicationSite.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProposalRoundDeadline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RespondBy",
                table: "ProposalSupervisorSelections",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RespondBy",
                table: "ProposalSupervisorSelections");
        }
    }
}
