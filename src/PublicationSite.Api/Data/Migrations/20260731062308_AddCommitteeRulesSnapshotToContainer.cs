using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PublicationSite.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCommitteeRulesSnapshotToContainer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RequiredCommitteeApprovals",
                table: "PublicationContainers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequiredExternalCommitteeMembers",
                table: "PublicationContainers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequiredInternalCommitteeMembers",
                table: "PublicationContainers",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiredCommitteeApprovals",
                table: "PublicationContainers");

            migrationBuilder.DropColumn(
                name: "RequiredExternalCommitteeMembers",
                table: "PublicationContainers");

            migrationBuilder.DropColumn(
                name: "RequiredInternalCommitteeMembers",
                table: "PublicationContainers");
        }
    }
}
