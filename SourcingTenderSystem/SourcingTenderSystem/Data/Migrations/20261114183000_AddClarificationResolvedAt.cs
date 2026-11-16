using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;
namespace SourcingTenderSystem.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261114183000_AddClarificationResolvedAt")]
public sealed class AddClarificationResolvedAt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.ClarificationRequests', 'ResolvedAt') IS NULL
                ALTER TABLE dbo.ClarificationRequests ADD ResolvedAt datetime2 NULL;
            """);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("ResolvedAt", "ClarificationRequests");
}
