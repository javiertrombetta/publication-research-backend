using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PublicationSite.Api.Data.Migrations
{
    /// <summary>
    /// The twenty screening questions were asked and thrown away.
    ///
    /// A student worked through them, declared, and everybody who then ruled on that declaration
    /// saw the one word without any of the working behind it. They are kept with the declaration
    /// now, question and answer together, so a supervisor deciding whether ethics documentation is
    /// required can read what the student actually said.
    ///
    /// Null for every declaration made before this, which is the truth about them: nothing was
    /// recorded, so nothing is shown.
    /// </summary>
    public partial class KeepTheScreeningAnswersBehindADeclaration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScreeningAnswers",
                table: "EthicsDeclarations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScreeningAnswers",
                table: "EthicsDeclarations");
        }
    }
}
