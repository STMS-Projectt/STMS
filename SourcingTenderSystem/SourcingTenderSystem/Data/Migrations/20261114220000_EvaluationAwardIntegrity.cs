using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class EvaluationAwardIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.TenderApprovals WHERE DATALENGTH(Decision) > 900)
                    THROW 51030, 'Legacy approval decision exceeds supported length. Review before migration.', 1;
                IF EXISTS (SELECT 1 FROM dbo.TenderApprovals
                    WHERE Decision IN ('Top Committee Approved', 'Top Committee Rejected')
                    GROUP BY EvaluationReportId, ApproverEmployeeProfileId HAVING COUNT(*) > 1)
                    THROW 51031, 'Duplicate historical Top Committee votes require review before migration.', 1;
                """);

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_TenderApprovals_EvaluationReportId] ON [dbo].[TenderApprovals];");

            migrationBuilder.AlterColumn<string>(
                name: "Decision",
                table: "TenderApprovals",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalizedAt",
                table: "EvaluationReports",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultsSnapshot",
                table: "EvaluationReports",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AwardOutcome",
                table: "Bids",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ComplianceClarificationId",
                table: "Bids",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("""
                EXEC(N'UPDATE b SET AwardOutcome = CASE WHEN t.Status=''Cancelled'' THEN ''Cancelled''
                    WHEN b.Id=r.RecommendedBidId THEN ''Awarded'' ELSE ''Unsuccessful'' END
                FROM dbo.Bids b JOIN dbo.Tenders t ON t.Id=b.TenderId
                JOIN dbo.EvaluationReports r ON r.TenderId=t.Id
                WHERE t.Status IN (''Awarded'',''Cancelled'') AND b.SubmittedAt IS NOT NULL AND b.Status<>''Withdrawn'';
                UPDATE r SET FinalizedAt = a.FinalizedAt
                FROM dbo.EvaluationReports r JOIN dbo.Tenders t ON t.Id=r.TenderId
                CROSS APPLY (SELECT MAX(DecidedAt) AS FinalizedAt FROM dbo.TenderApprovals a
                    WHERE a.EvaluationReportId=r.Id AND a.Decision=CASE WHEN t.Status=''Awarded''
                        THEN ''Top Committee Approved'' ELSE ''Top Committee Rejected'' END) a
                WHERE t.Status IN (''Awarded'',''Cancelled'');');
                """);

            migrationBuilder.CreateTable(
                name: "EvaluationSubmissions",
                columns: table => new
                {
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    EmployeeProfileId = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationSubmissions", x => new { x.TenderId, x.EmployeeProfileId });
                    table.ForeignKey(
                        name: "FK_EvaluationSubmissions_EmployeeProfiles_EmployeeProfileId",
                        column: x => x.EmployeeProfileId,
                        principalTable: "EmployeeProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvaluationSubmissions_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenderApprovals_EvaluationReportId_ApproverEmployeeProfileId",
                table: "TenderApprovals",
                columns: new[] { "EvaluationReportId", "ApproverEmployeeProfileId" },
                unique: true,
                filter: "[Decision] IN ('Top Committee Approved', 'Top Committee Rejected')");

            migrationBuilder.CreateIndex(
                name: "IX_Bids_ComplianceClarificationId",
                table: "Bids",
                column: "ComplianceClarificationId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationSubmissions_EmployeeProfileId",
                table: "EvaluationSubmissions",
                column: "EmployeeProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_Bids_ClarificationRequests_ComplianceClarificationId",
                table: "Bids",
                column: "ComplianceClarificationId",
                principalTable: "ClarificationRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bids_ClarificationRequests_ComplianceClarificationId",
                table: "Bids");

            migrationBuilder.DropTable(
                name: "EvaluationSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_TenderApprovals_EvaluationReportId_ApproverEmployeeProfileId",
                table: "TenderApprovals");

            migrationBuilder.DropIndex(
                name: "IX_Bids_ComplianceClarificationId",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "FinalizedAt",
                table: "EvaluationReports");

            migrationBuilder.DropColumn(
                name: "ResultsSnapshot",
                table: "EvaluationReports");

            migrationBuilder.DropColumn(
                name: "AwardOutcome",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "ComplianceClarificationId",
                table: "Bids");

            migrationBuilder.AlterColumn<string>(
                name: "Decision",
                table: "TenderApprovals",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450);

            migrationBuilder.CreateIndex(
                name: "IX_TenderApprovals_EvaluationReportId",
                table: "TenderApprovals",
                column: "EvaluationReportId");
        }
    }
}
