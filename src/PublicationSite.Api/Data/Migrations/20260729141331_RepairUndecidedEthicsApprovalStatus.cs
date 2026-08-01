using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PublicationSite.Api.Data.Migrations
{
    /// <summary>
    /// Data repair, no schema change. EthicsApproval.Status used to be created as NotRequired
    /// simply because that is the enum's zero, so every approval claimed a decision that no
    /// Supervisor had made. A student who declared that their research DOES need ethics approval
    /// was shown "Not Required". EthicsStatus.PendingSupervisorDecision now exists for that state;
    /// this moves the already-stored rows onto it.
    /// </summary>
    public partial class RepairUndecidedEthicsApprovalStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0 = NotRequired, 4 = PendingSupervisorDecision.
            // Only rows where nobody actually ruled: no Supervisor decision and no final
            // Coordinator decision. An approval that legitimately reached NotRequired through
            // the workflow carries at least one of those timestamps and is left alone.
            migrationBuilder.Sql(@"
                UPDATE EthicsApprovals
                SET Status = 4
                WHERE Status = 0
                  AND SupervisorDecisionAt IS NULL
                  AND FinalDecisionAt IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to the old (wrong) representation, so the migration is reversible.
            migrationBuilder.Sql(@"
                UPDATE EthicsApprovals
                SET Status = 0
                WHERE Status = 4;");
        }
    }
}
