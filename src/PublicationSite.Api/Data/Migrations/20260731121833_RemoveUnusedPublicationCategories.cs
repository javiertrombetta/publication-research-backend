using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PublicationSite.Api.Data.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Drops the PublicationCategories table and the nullable foreign key on Publications.
    ///
    /// EF warns that this may lose data. It cannot: nothing in the application has ever written to
    /// either. No endpoint exposed categories, no service read them, and the column was never set,
    /// so every row's value is null and the table is empty on every deployment. What it removes is
    /// a question, not information: the first thing anyone asked on finding it was what it was for.
    ///
    /// The job it looks like it was meant to do is already done by ResearchArea, which is wired end
    /// to end: on student profiles, on a paper's metadata, and as a filter in the public catalogue.
    /// Two ways to group a publication, one of them real, is worse than one.
    /// </summary>
    public partial class RemoveUnusedPublicationCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Publications_PublicationCategories_PublicationCategoryId",
                table: "Publications");

            migrationBuilder.DropTable(
                name: "PublicationCategories");

            migrationBuilder.DropIndex(
                name: "IX_Publications_PublicationCategoryId",
                table: "Publications");

            migrationBuilder.DropColumn(
                name: "PublicationCategoryId",
                table: "Publications");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PublicationCategoryId",
                table: "Publications",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateTable(
                name: "PublicationCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicationCategories", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Publications_PublicationCategoryId",
                table: "Publications",
                column: "PublicationCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PublicationCategories_Name",
                table: "PublicationCategories",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Publications_PublicationCategories_PublicationCategoryId",
                table: "Publications",
                column: "PublicationCategoryId",
                principalTable: "PublicationCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
