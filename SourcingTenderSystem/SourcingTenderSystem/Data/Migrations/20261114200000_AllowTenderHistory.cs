using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SourcingTenderSystem.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261114200000_AllowTenderHistory")]
public sealed class AllowTenderHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Tenders')
            AND name = N'IX_Tenders_PurchaseRequisitionId' AND is_unique = 1)
            DROP INDEX IX_Tenders_PurchaseRequisitionId ON dbo.Tenders;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Tenders')
            AND name = N'IX_Tenders_PurchaseRequisitionId')
            CREATE INDEX IX_Tenders_PurchaseRequisitionId ON dbo.Tenders(PurchaseRequisitionId);
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_Tenders_PurchaseRequisitionId", "Tenders");
        migrationBuilder.CreateIndex("IX_Tenders_PurchaseRequisitionId", "Tenders", "PurchaseRequisitionId", unique: true);
    }
}
