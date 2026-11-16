using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class SupplierSubmissionAndClarificationDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApplicationSubmittedAt",
                table: "SupplierProfiles",
                type: "datetime2",
                nullable: true);

            // Before this migration, uploading a document already made a pending application reviewable.
            migrationBuilder.Sql("""
                UPDATE supplier
                SET ApplicationSubmittedAt = COALESCE(documents.LastUpload, supplier.CreatedAt)
                FROM dbo.SupplierProfiles supplier
                OUTER APPLY (SELECT MAX(d.UploadedAt) AS LastUpload
                             FROM dbo.SupplierDocuments d WHERE d.SupplierProfileId = supplier.Id) documents
                WHERE supplier.Status <> 'Pending' OR documents.LastUpload IS NOT NULL;
                """);

            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "SupplierDocuments",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AttachmentAddedAt",
                table: "ClarificationRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AttachmentExpiryDate",
                table: "ClarificationRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentKey",
                table: "ClarificationRequests",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentName",
                table: "ClarificationRequests",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedDocumentType",
                table: "ClarificationRequests",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupplierApplicationId",
                table: "ClarificationRequests",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClarificationRequests_SupplierApplicationId",
                table: "ClarificationRequests",
                column: "SupplierApplicationId");

            migrationBuilder.AddForeignKey(
                name: "FK_ClarificationRequests_SupplierProfiles_SupplierApplicationId",
                table: "ClarificationRequests",
                column: "SupplierApplicationId",
                principalTable: "SupplierProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClarificationRequests_SupplierProfiles_SupplierApplicationId",
                table: "ClarificationRequests");

            migrationBuilder.DropIndex(
                name: "IX_ClarificationRequests_SupplierApplicationId",
                table: "ClarificationRequests");

            migrationBuilder.DropColumn(
                name: "ApplicationSubmittedAt",
                table: "SupplierProfiles");

            migrationBuilder.DropColumn(
                name: "FileName",
                table: "SupplierDocuments");

            migrationBuilder.DropColumn(
                name: "AttachmentAddedAt",
                table: "ClarificationRequests");

            migrationBuilder.DropColumn(
                name: "AttachmentExpiryDate",
                table: "ClarificationRequests");

            migrationBuilder.DropColumn(
                name: "AttachmentKey",
                table: "ClarificationRequests");

            migrationBuilder.DropColumn(
                name: "AttachmentName",
                table: "ClarificationRequests");

            migrationBuilder.DropColumn(
                name: "RequestedDocumentType",
                table: "ClarificationRequests");

            migrationBuilder.DropColumn(
                name: "SupplierApplicationId",
                table: "ClarificationRequests");
        }
    }
}
