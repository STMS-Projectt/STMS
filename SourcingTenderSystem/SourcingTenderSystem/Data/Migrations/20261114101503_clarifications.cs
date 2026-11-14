using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SourcingTenderSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class clarifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                table: "AspNetUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'dbo.AspNetUserPasskeys', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.AspNetUserPasskeys (
                        CredentialId varbinary(1024) NOT NULL,
                        UserId nvarchar(450) NOT NULL,
                        Data nvarchar(max) NOT NULL,
                        CONSTRAINT PK_AspNetUserPasskeys PRIMARY KEY NONCLUSTERED (CredentialId),
                        CONSTRAINT FK_AspNetUserPasskeys_AspNetUsers_UserId
                            FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE
                    );
                END;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.AspNetUserPasskeys') AND name = N'IX_AspNetUserPasskeys_UserId')
                    CREATE INDEX IX_AspNetUserPasskeys_UserId ON dbo.AspNetUserPasskeys(UserId);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This table may predate the migration and contain enrolled credentials.

            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256,
                oldNullable: true);
        }
    }
}
