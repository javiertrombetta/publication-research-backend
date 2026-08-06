using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PublicationSite.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class LetAnAdministratorStopMessagesOnOnePublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContainerMessagingRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PublicationContainerId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TargetRole = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TargetUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Allowed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SetByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SetAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContainerMessagingRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContainerMessagingRules_PublicationContainers_PublicationCon~",
                        column: x => x.PublicationContainerId,
                        principalTable: "PublicationContainers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContainerMessagingRules_Users_SetByUserId",
                        column: x => x.SetByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContainerMessagingRules_Users_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ContainerMessagingRules_PublicationContainerId",
                table: "ContainerMessagingRules",
                column: "PublicationContainerId");

            migrationBuilder.CreateIndex(
                name: "IX_ContainerMessagingRules_SetByUserId",
                table: "ContainerMessagingRules",
                column: "SetByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContainerMessagingRules_TargetUserId",
                table: "ContainerMessagingRules",
                column: "TargetUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContainerMessagingRules");
        }
    }
}
