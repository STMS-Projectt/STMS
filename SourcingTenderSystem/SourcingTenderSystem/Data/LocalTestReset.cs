using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Security;

namespace SourcingTenderSystem.Data;

internal static class LocalTestReset
{
    public static async Task RunAsync(IServiceProvider services, IConfiguration configuration)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var target = new SqlConnectionStringBuilder(db.Database.GetConnectionString());
        if (target.DataSource != "localhost" || target.InitialCatalog != "SourcingTenderSystem" ||
            configuration["LocalTestReset:Confirm"] != "localhost/SourcingTenderSystem")
            throw new InvalidOperationException("The reset must explicitly target localhost/SourcingTenderSystem.");

        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        var roster = new (string Role, string Email, string Name, string? Department)[]
        {
            (Roles.Admin, "admin@stms.example.test", "Test Administrator", null),
            ("RequestingDepartment", "request.it@stms.example.test", "IT Requester", "IT"),
            ("RequestingDepartment", "request.hr@stms.example.test", "HR Requester", "HR"),
            (Roles.SourcingOfficer, "officer@stms.example.test", "Sourcing Officer", "Sourcing"),
            (Roles.SourcingManager, "manager@stms.example.test", "Sourcing Manager", "Sourcing"),
            (Roles.TechnicalCommittee, "technical@stms.example.test", "Technical Reviewer", "IT"),
            (Roles.FinancialCommittee, "financial@stms.example.test", "Financial Reviewer", "Finance"),
            (Roles.TopCommittee, "top@stms.example.test", "Top Committee Member", null),
            (Roles.EvaluationCommittee, "evaluation@stms.example.test", "Legacy Evaluation Account", null),
            (Roles.Supplier, "supplier1@example.test", "Demo Supplier 1", null),
            (Roles.Supplier, "supplier2@example.test", "Demo Supplier 2", null),
            (Roles.Supplier, "supplier3@example.test", "Demo Supplier 3", null)
        };

        var credentials = new List<(string Role, string Email, string Password)>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("""
            IF DB_NAME() <> N'SourcingTenderSystem' OR OBJECT_ID(N'dbo.PurchaseRequisitions') IS NULL
                THROW 50001, 'Unexpected test database.', 1;
            DECLARE @disable nvarchar(max), @clear nvarchar(max), @enable nvarchar(max), @verify nvarchar(max);
            SELECT @disable = STRING_AGG(CONVERT(nvarchar(max), N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) +
                N'.' + QUOTENAME(OBJECT_NAME(parent_object_id)) + N' NOCHECK CONSTRAINT ' + QUOTENAME(name)), N';'),
                @enable = STRING_AGG(CONVERT(nvarchar(max), N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) +
                N'.' + QUOTENAME(OBJECT_NAME(parent_object_id)) + CASE WHEN is_not_trusted = 1 THEN N' CHECK CONSTRAINT ' ELSE N' WITH CHECK CHECK CONSTRAINT ' END + QUOTENAME(name)), N';')
            FROM sys.foreign_keys WHERE is_disabled = 0;
            SELECT @clear = STRING_AGG(CONVERT(nvarchar(max), N'DELETE FROM ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name)), N';'),
                @verify = STRING_AGG(CONVERT(nvarchar(max), N'IF EXISTS (SELECT 1 FROM ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) +
                    N') THROW 50002, ''A table was not emptied.'', 1;'), N' ')
            FROM sys.tables WHERE is_ms_shipped = 0 AND name <> N'__EFMigrationsHistory';
            EXEC sys.sp_executesql @disable;
            EXEC sys.sp_executesql @clear;
            EXEC sys.sp_executesql @verify;
            EXEC sys.sp_executesql @enable;
            IF COL_LENGTH('dbo.EvaluationReports', 'FileKey') IS NULL
                ALTER TABLE [dbo].[EvaluationReports] ADD [FileKey] nvarchar(max) NULL;
            IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20261113190000_AddEvaluationReportFile')
                INSERT dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
                    VALUES (N'20261113190000_AddEvaluationReportFile', N'10.0.14');
            """);

        foreach (var role in Roles.EmployeeRoles.Append(Roles.Supplier))
            Ensure(await roles.CreateAsync(new IdentityRole(role)));

        var departments = roster.Where(x => x.Department != null).Select(x => x.Department!).Distinct()
            .ToDictionary(name => name, name => new Department { Name = name, IsActive = true });
        db.Departments.AddRange(departments.Values);
        await db.SaveChangesAsync();

        foreach (var (role, email, name, department) in roster)
        {
            var password = "Qa!" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(7)) + "z9";
            var user = new IdentityUser { Email = email, UserName = email, EmailConfirmed = true };
            Ensure(await users.CreateAsync(user, password));
            Ensure(await users.AddToRoleAsync(user, role));
            if (role == Roles.Supplier)
                db.SupplierProfiles.Add(new SupplierProfile { UserId = user.Id, CompanyName = name,
                    TaxNumber = "TEST-TAX-" + email[8], CommercialRegNo = "TEST-REG-" + email[8],
                    ContactPhone = "0000000000", Address = "Test address", Status = SupplierStatus.Approved, CreatedAt = DateTime.UtcNow });
            else
                db.EmployeeProfiles.Add(new EmployeeProfile { UserId = user.Id, FullName = name,
                    JobTitle = role, IsActive = true, DepartmentId = department is null ? null : departments[department].Id });
            await db.SaveChangesAsync();
            if (!await users.CheckPasswordAsync(user, password) || !await users.IsInRoleAsync(user, role))
                throw new InvalidOperationException($"Could not verify the new {role} account.");
            credentials.Add((role, email, password));
        }

        await transaction.CommitAsync();
        Console.WriteLine("ROLE | EMAIL | PASSWORD");
        foreach (var entry in credentials)
            Console.WriteLine($"{entry.Role} | {entry.Email} | {entry.Password}");
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
