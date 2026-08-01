using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PublicationSite.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConfigurableEthicsDocumentRequirements : Migration
    {
        // Fixed rather than generated so the seeded rows have the same identity in every
        // environment, and so the data backfill below can refer to them.
        private const string ApprovalCertificateId = "7d3f0a1e-4c62-4a9b-9f21-0c5b8e3a1d10";
        private const string ApplicationFormId = "7d3f0a1e-4c62-4a9b-9f21-0c5b8e3a1d11";
        private const string ConsentFormId = "7d3f0a1e-4c62-4a9b-9f21-0c5b8e3a1d12";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ordered so no data is lost. The three document types were a C# enum; they become
            // rows, every upload already made is re-pointed at the matching row, and only then is
            // the old column dropped. Doing it in the order EF scaffolds, dropping first, would
            // discard which document each submitted file actually was.

            migrationBuilder.CreateTable(
                name: "EthicsDocumentRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EthicsDocumentRequirements", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EthicsDocumentRequirements_Name",
                table: "EthicsDocumentRequirements",
                column: "Name",
                unique: true);

            // The three that were hard-coded, with fixed ids so every environment agrees on
            // which row is which and the backfill below can name them.
            migrationBuilder.Sql("""
                INSERT INTO EthicsDocumentRequirements
                    (Id, Name, Description, SortOrder, IsActive, CreatedAt, UpdatedAt)
                VALUES
                    (@ApprovalCertificateId, 'Ethics Approval Certificate',
                     'The certificate issued by the ethics committee once approval is granted.',
                     1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
                    (@ApplicationFormId, 'Ethics Application Form',
                     'The completed application submitted to the ethics committee.',
                     2, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
                    (@ConsentFormId, 'Participant Consent Form',
                     'The consent form given to participants.',
                     3, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6));
                """
                .Replace("@ApprovalCertificateId", $"'{ApprovalCertificateId}'")
                .Replace("@ApplicationFormId", $"'{ApplicationFormId}'")
                .Replace("@ConsentFormId", $"'{ConsentFormId}'"));

            migrationBuilder.AddColumn<Guid>(
                name: "EthicsDocumentRequirementId",
                table: "EthicsDocuments",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            // Enum ordinals, in the order they were declared: 0 certificate, 1 application,
            // 2 consent form.
            migrationBuilder.Sql($"""
                UPDATE EthicsDocuments
                SET EthicsDocumentRequirementId = CASE DocumentType
                    WHEN 0 THEN '{ApprovalCertificateId}'
                    WHEN 1 THEN '{ApplicationFormId}'
                    WHEN 2 THEN '{ConsentFormId}'
                END
                WHERE DocumentType IN (0, 1, 2);
                """);

            migrationBuilder.DropColumn(
                name: "DocumentType",
                table: "EthicsDocuments");

            migrationBuilder.CreateTable(
                name: "EthicsApprovalRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    EthicsApprovalId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    EthicsDocumentRequirementId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EthicsApprovalRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EthicsApprovalRequirements_EthicsApprovals_EthicsApprovalId",
                        column: x => x.EthicsApprovalId,
                        principalTable: "EthicsApprovals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EthicsApprovalRequirements_EthicsDocumentRequirements_Ethics~",
                        column: x => x.EthicsDocumentRequirementId,
                        principalTable: "EthicsDocumentRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // Approvals that were already asking for documentation get the list they were asking
            // for: all three, since that is what the old code required unconditionally. Without
            // this they would have an empty list and could never be completed.
            //
            // Approvals not yet at that point are left alone: their list is taken when
            // documentation is first requested, which is the behaviour from here on.
            migrationBuilder.Sql($"""
                INSERT INTO EthicsApprovalRequirements
                    (Id, EthicsApprovalId, EthicsDocumentRequirementId, SortOrder)
                SELECT UUID(), a.Id, r.Id, r.SortOrder
                FROM EthicsApprovals a
                CROSS JOIN EthicsDocumentRequirements r
                WHERE a.Status IN (1, 2)
                   OR EXISTS (SELECT 1 FROM EthicsDocuments d WHERE d.EthicsApprovalId = a.Id);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_EthicsDocuments_EthicsDocumentRequirementId",
                table: "EthicsDocuments",
                column: "EthicsDocumentRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_EthicsApprovalRequirements_EthicsApprovalId_EthicsDocumentRe~",
                table: "EthicsApprovalRequirements",
                columns: new[] { "EthicsApprovalId", "EthicsDocumentRequirementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EthicsApprovalRequirements_EthicsDocumentRequirementId",
                table: "EthicsApprovalRequirements",
                column: "EthicsDocumentRequirementId");

            migrationBuilder.AddForeignKey(
                name: "FK_EthicsDocuments_EthicsDocumentRequirements_EthicsDocumentReq~",
                table: "EthicsDocuments",
                column: "EthicsDocumentRequirementId",
                principalTable: "EthicsDocumentRequirements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <summary>
        /// Restores the schema but not the meaning: any document uploaded against a requirement
        /// an administrator added after this migration has no enum value to go back to, and its
        /// DocumentType becomes 0. Reverting is therefore a development convenience, not a
        /// supported downgrade for a database in real use.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EthicsDocuments_EthicsDocumentRequirements_EthicsDocumentReq~",
                table: "EthicsDocuments");

            migrationBuilder.DropTable(
                name: "EthicsApprovalRequirements");

            migrationBuilder.DropTable(
                name: "EthicsDocumentRequirements");

            migrationBuilder.DropIndex(
                name: "IX_EthicsDocuments_EthicsDocumentRequirementId",
                table: "EthicsDocuments");

            migrationBuilder.DropColumn(
                name: "EthicsDocumentRequirementId",
                table: "EthicsDocuments");

            migrationBuilder.AddColumn<int>(
                name: "DocumentType",
                table: "EthicsDocuments",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
