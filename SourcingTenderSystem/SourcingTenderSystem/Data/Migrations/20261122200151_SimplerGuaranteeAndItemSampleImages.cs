using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class SimplerGuaranteeAndItemSampleImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BankGuaranteeNumber",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "BankGuaranteeType",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "OfferNature",
                table: "Bids");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SampleImageName",
                table: "BidItems");

            migrationBuilder.DropColumn(
                name: "SampleImagePath",
                table: "BidItems");

            migrationBuilder.AddColumn<string>(
                name: "BankGuaranteeNumber",
                table: "Bids",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankGuaranteeType",
                table: "Bids",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfferNature",
                table: "Bids",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Product");
        }
    }
}
