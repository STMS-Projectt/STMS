using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderSystem.Data.Migrations;

public partial class PublicContactInbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ContactMessages",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Receipt = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                Topic = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                Message = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                Reply = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                RepliedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                RepliedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ContactMessages", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_ContactMessages_Receipt", table: "ContactMessages",
            column: "Receipt", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(name: "ContactMessages");
}
