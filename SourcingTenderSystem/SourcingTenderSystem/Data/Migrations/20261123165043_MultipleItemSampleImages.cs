using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class MultipleItemSampleImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BidItemImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BidItemId = table.Column<int>(type: "int", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BidItemImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BidItemImages_BidItems_BidItemId",
                        column: x => x.BidItemId,
                        principalTable: "BidItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BidItemImages_BidItemId",
                table: "BidItemImages",
                column: "BidItemId");

            // Keep the photo each item already had: it becomes the item's first photo
            migrationBuilder.Sql(@"
                INSERT INTO BidItemImages (BidItemId, FilePath, FileName, UploadedAt)
                SELECT Id, SampleImagePath, COALESCE(SampleImageName, SampleImagePath), SYSUTCDATETIME()
                FROM BidItems WHERE SampleImagePath IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "SampleImageName",
                table: "BidItems");

            migrationBuilder.DropColumn(
                name: "SampleImagePath",
                table: "BidItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SampleImageName",
                table: "BidItems",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SampleImagePath",
                table: "BidItems",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            // Going back keeps only the first photo of each item
            migrationBuilder.Sql(@"
                UPDATE b SET SampleImagePath = i.FilePath, SampleImageName = i.FileName
                FROM BidItems b
                CROSS APPLY (SELECT TOP 1 FilePath, FileName FROM BidItemImages WHERE BidItemId = b.Id ORDER BY Id) i;");

            migrationBuilder.DropTable(
                name: "BidItemImages");
        }
    }
}
