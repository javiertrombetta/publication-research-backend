using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PublicationSite.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // true, not the false EF scaffolded. It reads the CLR default of the type rather than
            // the property initialiser, and taking that literally here would have marked every
            // existing account unavailable the moment this ran: no supervisor in the dispatch
            // chooser, no coordinator for a new publication to be allocated to, and nothing on
            // screen to explain why. Everybody already here is available until they say otherwise,
            // which is what the column means.
            migrationBuilder.AddColumn<bool>(
                name: "IsAvailable",
                table: "Users",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAvailable",
                table: "Users");
        }
    }
}
