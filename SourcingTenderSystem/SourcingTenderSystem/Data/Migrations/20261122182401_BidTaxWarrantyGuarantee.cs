using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class BidTaxWarrantyGuarantee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BankGuaranteeAmount",
                table: "Bids",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BankGuaranteeExpiry",
                table: "Bids",
                type: "datetime2",
                nullable: true);

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
                name: "BankName",
                table: "Bids",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GuaranteeVerificationCode",
                table: "Bids",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.AddColumn<bool>(
                name: "IncludesSalesTax",
                table: "Bids",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LegalWarrantyMonths",
                table: "Bids",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfferNature",
                table: "Bids",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Product");

            migrationBuilder.CreateIndex(
                name: "IX_Bids_GuaranteeVerificationCode",
                table: "Bids",
                column: "GuaranteeVerificationCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bids_GuaranteeVerificationCode",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "BankGuaranteeAmount",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "BankGuaranteeExpiry",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "BankGuaranteeNumber",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "BankGuaranteeType",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "BankName",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "GuaranteeVerificationCode",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "IncludesSalesTax",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "LegalWarrantyMonths",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "OfferNature",
                table: "Bids");
        }
    }
}
