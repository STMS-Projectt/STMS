using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class BiddingRoundHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.Bids GROUP BY TenderId, SupplierProfileId HAVING COUNT_BIG(*) > 1)
                    THROW 51013, 'Duplicate bids exist. Resolve using the reviewed data remediation plan before upgrading.', 1;
                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes i
                    WHERE i.object_id = OBJECT_ID(N'dbo.Bids') AND i.name = N'IX_Bids_TenderId_SupplierProfileId'
                      AND i.is_unique = 1 AND i.is_disabled = 0 AND i.has_filter = 0
                      AND (SELECT COUNT(*) FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id AND c.key_ordinal>0)=2
                      AND INDEX_COL(N'dbo.Bids',i.index_id,1) = N'TenderId'
                      AND INDEX_COL(N'dbo.Bids',i.index_id,2) = N'SupplierProfileId')
                BEGIN
                    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Bids') AND name=N'IX_Bids_TenderId_SupplierProfileId')
                        THROW 51014, 'Unexpected bid index definition. Inspect the schema before upgrading.', 1;
                    CREATE UNIQUE INDEX IX_Bids_TenderId_SupplierProfileId ON dbo.Bids(TenderId,SupplierProfileId);
                END;
                IF EXISTS (SELECT 1 FROM dbo.Tenders WHERE [counter] NOT BETWEEN 1 AND 3)
                    THROW 51015, 'Legacy tender round number must be between 1 and 3. Inspect dbo.Tenders.counter before upgrading.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Invitations_Tenders_TenderId",
                table: "Invitations");

            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Invitations_TenderId] ON [dbo].[Invitations];");

            migrationBuilder.AddColumn<int>(
                name: "RoundNumber",
                table: "Invitations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubmittedRoundNumber",
                table: "Bids",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TenderRounds",
                columns: table => new
                {
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    OpensAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosesAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OfficerId = table.Column<int>(type: "int", nullable: true),
                    ReopenReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosingOutcome = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    EligibleSuppliersAtClose = table.Column<int>(type: "int", nullable: true),
                    Legacy = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenderRounds", x => new { x.TenderId, x.Number });
                    table.CheckConstraint("CK_TenderRounds_Number", "[Number] BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_TenderRounds_EmployeeProfiles_OfficerId",
                        column: x => x.OfficerId,
                        principalTable: "EmployeeProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TenderRounds_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_TenderId_RoundNumber_SupplierProfileId",
                table: "Invitations",
                columns: new[] { "TenderId", "RoundNumber", "SupplierProfileId" },
                unique: true,
                filter: "[RoundNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Bids_TenderId_SubmittedRoundNumber",
                table: "Bids",
                columns: new[] { "TenderId", "SubmittedRoundNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_TenderRounds_OfficerId",
                table: "TenderRounds",
                column: "OfficerId");

            migrationBuilder.Sql("""
                EXEC(N'INSERT dbo.TenderRounds(TenderId, Number, OpensAt, ClosesAt, CreatedAt, OfficerId, ClosingOutcome, Legacy)
                SELECT Id, [counter], OpenDate, CloseDate, NULL,
                       CASE WHEN [counter]=1 THEN CreatedByEmployeeProfileId END,
                       CASE WHEN Status NOT IN (''Draft'',''Published'',''Open'') THEN ''LegacyClosed'' END, 1
                FROM dbo.Tenders;
                INSERT dbo.TenderRounds(TenderId, Number, CreatedAt, OfficerId, ClosingOutcome, Legacy)
                SELECT Id, 1, NULL, CreatedByEmployeeProfileId, ''LegacyClosed'', 1
                FROM dbo.Tenders WHERE [counter] IN (2, 3);
                INSERT dbo.TenderRounds(TenderId, Number, ClosingOutcome, Legacy)
                SELECT Id, 2, ''LegacyClosed'', 1
                FROM dbo.Tenders WHERE [counter]=3;
                UPDATE i SET RoundNumber=1 FROM dbo.Invitations i JOIN dbo.Tenders t ON t.Id=i.TenderId
                WHERE t.[counter]=1 AND NOT EXISTS (
                    SELECT 1 FROM dbo.Invitations other WHERE other.TenderId=i.TenderId
                        AND other.SupplierProfileId=i.SupplierProfileId AND other.Id<>i.Id);
                UPDATE b SET SubmittedRoundNumber=1 FROM dbo.Bids b JOIN dbo.Tenders t ON t.Id=b.TenderId
                WHERE t.[counter]=1 AND b.SubmittedAt IS NOT NULL;');
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Bids_TenderRounds_TenderId_SubmittedRoundNumber",
                table: "Bids",
                columns: new[] { "TenderId", "SubmittedRoundNumber" },
                principalTable: "TenderRounds",
                principalColumns: new[] { "TenderId", "Number" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invitations_TenderRounds_TenderId_RoundNumber",
                table: "Invitations",
                columns: new[] { "TenderId", "RoundNumber" },
                principalTable: "TenderRounds",
                principalColumns: new[] { "TenderId", "Number" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invitations_Tenders_TenderId",
                table: "Invitations",
                column: "TenderId",
                principalTable: "Tenders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bids_TenderRounds_TenderId_SubmittedRoundNumber",
                table: "Bids");

            migrationBuilder.DropForeignKey(
                name: "FK_Invitations_TenderRounds_TenderId_RoundNumber",
                table: "Invitations");

            migrationBuilder.DropForeignKey(
                name: "FK_Invitations_Tenders_TenderId",
                table: "Invitations");

            migrationBuilder.DropTable(
                name: "TenderRounds");

            migrationBuilder.DropIndex(
                name: "IX_Invitations_TenderId_RoundNumber_SupplierProfileId",
                table: "Invitations");

            migrationBuilder.DropIndex(
                name: "IX_Bids_TenderId_SubmittedRoundNumber",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "RoundNumber",
                table: "Invitations");

            migrationBuilder.DropColumn(
                name: "SubmittedRoundNumber",
                table: "Bids");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_TenderId",
                table: "Invitations",
                column: "TenderId");

            migrationBuilder.AddForeignKey(
                name: "FK_Invitations_Tenders_TenderId",
                table: "Invitations",
                column: "TenderId",
                principalTable: "Tenders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
