using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PublicationSite.Api.Data.Migrations
{
    /// <summary>
    /// Where somebody's own arrangement of the sidebar is kept.
    ///
    /// It was kept in the browser, which is a machine and not a person: one user's arrangement was
    /// handed to whoever signed in on that machine next. On the account it belongs to the person,
    /// follows them to another machine, and leaves everybody else's menu alone.
    ///
    /// Null for everybody until they rearrange something, which is the menu in the order it is
    /// written in.
    /// </summary>
    public partial class RememberEachPersonsSidebarOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SidebarOrder",
                table: "Users",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SidebarOrder",
                table: "Users");
        }
    }
}
