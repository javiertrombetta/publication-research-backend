using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PublicationSite.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DepartmentMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DepartmentMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DepartmentId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepartmentMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DepartmentMemberships_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DepartmentMemberships_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // The head-of-department index stops being unique, because a department may have more
            // than one head. Four steps rather than two: MySQL keeps the foreign key on that
            // column pointed at this index, so it refuses to drop it while the key stands, and the
            // replacement carries the same name so both cannot exist at once.
            migrationBuilder.DropForeignKey(
                name: "FK_HeadOfDepartmentProfiles_Departments_DepartmentId",
                table: "HeadOfDepartmentProfiles");

            migrationBuilder.DropIndex(
                name: "IX_HeadOfDepartmentProfiles_DepartmentId",
                table: "HeadOfDepartmentProfiles");

            migrationBuilder.CreateIndex(
                name: "IX_HeadOfDepartmentProfiles_DepartmentId",
                table: "HeadOfDepartmentProfiles",
                column: "DepartmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_HeadOfDepartmentProfiles_Departments_DepartmentId",
                table: "HeadOfDepartmentProfiles",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentMemberships_DepartmentId",
                table: "DepartmentMemberships",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentMemberships_UserId_DepartmentId",
                table: "DepartmentMemberships",
                columns: new[] { "UserId", "DepartmentId" },
                unique: true);

            // Every supervisor keeps the department they had, as their first membership. Done
            // before the column goes, or the answer would be gone by the time anything asked for
            // it and every supervisor in the institution would come back belonging nowhere.
            //
            // Reviewers get nothing here because they never had a department to keep: the role
            // carried none until now, so an administrator places them.
            migrationBuilder.Sql(
                """
                INSERT INTO DepartmentMemberships (Id, UserId, DepartmentId, CreatedAt)
                SELECT UUID(), s.UserId, s.DepartmentId, UTC_TIMESTAMP()
                FROM SupervisorProfiles s;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_SupervisorProfiles_Departments_DepartmentId",
                table: "SupervisorProfiles");

            migrationBuilder.DropIndex(
                name: "IX_SupervisorProfiles_DepartmentId",
                table: "SupervisorProfiles");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "SupervisorProfiles");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DepartmentMemberships");

            migrationBuilder.DropIndex(
                name: "IX_HeadOfDepartmentProfiles_DepartmentId",
                table: "HeadOfDepartmentProfiles");

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "SupervisorProfiles",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_SupervisorProfiles_DepartmentId",
                table: "SupervisorProfiles",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_HeadOfDepartmentProfiles_DepartmentId",
                table: "HeadOfDepartmentProfiles",
                column: "DepartmentId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SupervisorProfiles_Departments_DepartmentId",
                table: "SupervisorProfiles",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
