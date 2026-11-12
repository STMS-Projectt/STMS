using Microsoft.AspNetCore.Identity;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Security;

namespace SourcingTenderSystem.Data;

internal static class AdminSetup
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        var email = configuration["AdminSeed:Email"]?.Trim();
        var password = configuration["AdminSeed:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Configure AdminSeed:Email and AdminSeed:Password first.");

        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        var db = services.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        foreach (var role in Roles.EmployeeRoles.Append(Roles.Supplier))
            if (!await roles.RoleExistsAsync(role))
                Ensure(await roles.CreateAsync(new IdentityRole(role)));

        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new IdentityUser { UserName = email, Email = email };
            Ensure(await users.CreateAsync(user, password));
            Ensure(await users.AddToRoleAsync(user, Roles.Admin));
            db.EmployeeProfiles.Add(new EmployeeProfile
            {
                UserId = user.Id,
                FullName = "System administrator",
                JobTitle = "Administrator",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }
        else if (!await users.IsInRoleAsync(user, Roles.Admin))
        {
            throw new InvalidOperationException("The bootstrap email belongs to an existing account without the Admin role.");
        }

        await transaction.CommitAsync();
        Console.WriteLine("Administration roles and initial admin are ready.");
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(error => error.Description)));
    }
}
