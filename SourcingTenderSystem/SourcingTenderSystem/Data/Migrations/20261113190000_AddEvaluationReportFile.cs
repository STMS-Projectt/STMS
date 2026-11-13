using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SourcingTenderSystem.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261113190000_AddEvaluationReportFile")]
public sealed class AddEvaluationReportFile : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>("FileKey", "EvaluationReports", type: "nvarchar(max)", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("FileKey", "EvaluationReports");
}
