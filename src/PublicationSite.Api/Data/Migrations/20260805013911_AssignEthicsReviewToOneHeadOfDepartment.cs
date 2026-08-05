using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PublicationSite.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AssignEthicsReviewToOneHeadOfDepartment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "HeadOfDepartmentUserId",
                table: "EthicsApprovals",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_EthicsApprovals_HeadOfDepartmentUserId",
                table: "EthicsApprovals",
                column: "HeadOfDepartmentUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_EthicsApprovals_Users_HeadOfDepartmentUserId",
                table: "EthicsApprovals",
                column: "HeadOfDepartmentUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EthicsApprovals_Users_HeadOfDepartmentUserId",
                table: "EthicsApprovals");

            migrationBuilder.DropIndex(
                name: "IX_EthicsApprovals_HeadOfDepartmentUserId",
                table: "EthicsApprovals");

            migrationBuilder.DropColumn(
                name: "HeadOfDepartmentUserId",
                table: "EthicsApprovals");
        }
    }
}
