using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Security;

namespace SourcingTenderSystem.Data;

internal static class SourcingDemoSetup
{
    internal static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        var password = configuration["SourcingDemo:Password"];
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Set SourcingDemo:Password before initializing the demo accounts.");

        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        var db = services.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var role in SourcingRoster.Seats.Select(x => x.Role).Append(Roles.Supplier))
            if (!await roles.RoleExistsAsync(role)) Ensure(await roles.CreateAsync(new IdentityRole(role)));

        foreach (var (seat, email, name) in SourcingRoster.FixedMembers)
        {
            var role = SourcingRoster.Seats.Single(x => x.Seat == seat).Role;
            var user = await EnsureUser(email, role);
            var profile = await db.EmployeeProfiles.SingleOrDefaultAsync(e => e.UserId == user.Id);
            if (profile is null)
                db.EmployeeProfiles.Add(new EmployeeProfile { UserId = user.Id, FullName = name, JobTitle = seat, IsActive = true });
            else if (!profile.IsActive)
                throw new InvalidOperationException($"The configured committee member {email} is inactive.");
        }

        for (var i = 1; i <= 3; i++)
        {
            var email = $"supplier{i}@example.test";
            var existing = await users.FindByEmailAsync(email);
            if (existing is not null)
            {
                if (await db.SupplierProfiles.AnyAsync(s => s.UserId == existing.Id)) continue;
                throw new InvalidOperationException($"The demo supplier email {email} is already in use.");
            }
            var user = await EnsureUser(email, Roles.Supplier);
            db.SupplierProfiles.Add(new SupplierProfile
            {
                UserId = user.Id, CompanyName = $"Demo Supplier {i}", TaxNumber = $"DEMO-SOURCING-{i}",
                CommercialRegNo = $"DEMO-REG-{i}", ContactPhone = $"000000000{i}", Address = "Demo address",
                Status = SupplierStatus.Approved, CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        Console.WriteLine("Sourcing demo committee and suppliers are ready.");

        async Task<IdentityUser> EnsureUser(string email, string role)
        {
            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                user = new IdentityUser { UserName = email, Email = email };
                Ensure(await users.CreateAsync(user, password));
            }
            if (!await users.IsInRoleAsync(user, role)) Ensure(await users.AddToRoleAsync(user, role));
            return user;
        }
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
