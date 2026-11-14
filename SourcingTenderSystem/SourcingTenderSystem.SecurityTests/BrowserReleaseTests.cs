using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Services;
using Xunit;
using Xunit.Abstractions;

public sealed class BrowserReleaseTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Real_browser_identity_and_workspace()
    {
        var server = Environment.GetEnvironmentVariable("STMS_PHASE2_TEST_SQL");
        Assert.False(string.IsNullOrWhiteSpace(server));
        var chrome = Environment.GetEnvironmentVariable("STMS_BROWSER_EXE");
        Assert.True(File.Exists(chrome), "Set STMS_BROWSER_EXE to Chrome or Edge.");
        var catalog = "STMS_Phase4_" + Guid.NewGuid().ToString("N");
        var connection = new SqlConnectionStringBuilder(server) { InitialCatalog = catalog }.ConnectionString;
        using var factory = new LocalBrowserApp(connection);
        factory.UseKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0,
            listen => listen.Protocols = HttpProtocols.Http1));
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(catalog, db.Database.GetDbConnection().Database);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var script = Path.Combine(root, "qa-reports", "phase4-browser.mjs");
        Assert.True(File.Exists(script), script);
        try
        {
            await db.Database.MigrateAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var department = new Department { Name="Browser QA", IsActive=true };
            db.Add(department);
            await db.SaveChangesAsync();
            foreach (var role in new[] { "Admin", "RequestingDepartment", "SourcingOfficer", "Supplier",
                "SourcingManager", "TechnicalCommittee", "FinancialCommittee", "TopCommittee" })
            {
                Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
                var user = new IdentityUser { UserName=role+"@example.test", Email=role+"@example.test" };
                Assert.True((await users.CreateAsync(user, "Synthetic-Browser!2046")).Succeeded);
                Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
                if(role=="Supplier") db.Add(new SupplierProfile { UserId=user.Id, CompanyName="Browser supplier", Status="Approved",
                    TaxNumber="BROWSER", Address="Test", ContactPhone="000", CommercialRegNo="BROWSER", CreatedAt=DateTime.UtcNow });
                else db.Add(new EmployeeProfile { UserId=user.Id, FullName=role, JobTitle=role, IsActive=true, DepartmentId=department.Id });
            }
            var fallback = new IdentityUser { UserName="DepartmentSmoke@example.test", Email="DepartmentSmoke@example.test" };
            Assert.True((await users.CreateAsync(fallback, "Synthetic-Browser!2046")).Succeeded);
            Assert.True((await users.AddToRoleAsync(fallback, "RequestingDepartment")).Succeeded);
            db.Add(new EmployeeProfile { UserId=fallback.Id, FullName="Department QA", JobTitle="Test", IsActive=true, DepartmentId=department.Id });
            await db.SaveChangesAsync();
            foreach (var pair in new[] { ("Supplier2","Supplier"),("Supplier3","Supplier"),("Top2","TopCommittee"),("Top3","TopCommittee"),("Top4","TopCommittee") })
            {
                var u = new IdentityUser { UserName=pair.Item1+"@example.test", Email=pair.Item1+"@example.test" };
                Assert.True((await users.CreateAsync(u, "Synthetic-Browser!2046")).Succeeded);
                Assert.True((await users.AddToRoleAsync(u,pair.Item2)).Succeeded);
                if (pair.Item2=="Supplier") db.Add(new SupplierProfile { UserId=u.Id,CompanyName=pair.Item1,TaxNumber=pair.Item1,
                    CommercialRegNo=pair.Item1,ContactPhone="000",Address="Synthetic",Status="Approved",CreatedAt=DateTime.UtcNow });
                else db.Add(new EmployeeProfile { UserId=u.Id,FullName=pair.Item1,JobTitle="Member",IsActive=true });
            }
            await db.SaveChangesAsync();
            // The department account enrolls through actual WebAuthn; other roles are only provisioned
            // to exercise password login without individually onboarding another passkey.
            await db.Database.ExecuteSqlRawAsync("""
                INSERT dbo.AspNetUserPasskeys(UserId,CredentialId,Data)
                SELECT Id,CONVERT(varbinary(128),Id),'{{}}' FROM dbo.AspNetUsers WHERE UserName<>'RequestingDepartment@example.test'
                """);
            var node = new ProcessStartInfo("node") { UseShellExecute=false, RedirectStandardError=true, RedirectStandardOutput=true };
            foreach(var arg in new[]{script,new UriBuilder(client.BaseAddress!) { Host="localhost" }.Uri.ToString(),chrome!,Path.Combine(root,"qa-reports","phase4-browser-evidence")})
                node.ArgumentList.Add(arg);
            using var process=Process.Start(node)!;
            var stdout=process.StandardOutput.ReadToEndAsync();
            var stderr=process.StandardError.ReadToEndAsync();
            using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(8));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch(OperationCanceledException) { process.Kill(entireProcessTree:true); throw; }
            output.WriteLine(await stdout);
            var persisted = await db.Tenders.AsNoTracking().Where(t => t.Title.StartsWith("Phase 6 ")).Select(t => new {
                t.Id,t.Title,t.Status,t.PurchaseRequisitionId,
                Bids=t.Bids.Select(b=>new { b.Status,b.AwardOutcome,b.SubmittedAt,b.SubmittedRoundNumber }).ToList(),
                Invitations=t.Invitations.Select(i=>new { i.RoundNumber,i.Status,i.SentAt }).ToList(),
                Rounds=db.TenderRounds.Count(r=>r.TenderId==t.Id),
                Submissions=db.EvaluationSubmissions.Count(r=>r.TenderId==t.Id),
                Audit=db.AuditLogs.Where(a=>a.EntityName=="Tender" && a.EntityId==t.Id).Select(a=>a.Action).ToList(),
                Votes=t.EvaluationReport==null ? 0 : t.EvaluationReport.TenderApprovals.Count(a=>a.Decision.StartsWith("Top Committee")),
                Notifications=db.Notifications.Where(n=>t.Bids.Any(b=>n.Link=="/Bids/Details/"+b.Id)).Select(n=>n.Title).ToList()
            }).AsSplitQuery().ToListAsync();
            await File.WriteAllTextAsync(Path.Combine(root,"qa-reports","phase4-browser-evidence","phase6-sql.json"),
                System.Text.Json.JsonSerializer.Serialize(persisted,new System.Text.Json.JsonSerializerOptions { WriteIndented=true }));
            Assert.True(process.ExitCode==0,await stderr+"\n"+await stdout);
            var completed = await db.Tenders.Where(t=>t.Title.StartsWith("Phase 6 ")).ToListAsync();
            Assert.Equal("Awarded", completed.Single(t=>t.Title=="Phase 6 Award").Status);
            var award = completed.Single(t=>t.Title=="Phase 6 Award");
            Assert.Equal(4,await db.EvaluationSubmissions.CountAsync(s=>s.TenderId==award.Id));
            Assert.Equal(1,await db.Bids.CountAsync(b=>b.TenderId==award.Id && b.AwardOutcome=="Awarded"));
            Assert.Equal(2,await db.Bids.CountAsync(b=>b.TenderId==award.Id && b.AwardOutcome=="Unsuccessful"));
            var rounds = completed.Single(t=>t.Title=="Phase 6 Rounds");
            Assert.Equal(3,await db.TenderRounds.CountAsync(r=>r.TenderId==rounds.Id));
            Assert.Equal(2,await db.Bids.CountAsync(b=>b.TenderId==rounds.Id && b.SubmittedRoundNumber==1));
            Assert.Equal(5,await db.Invitations.CountAsync(i=>i.TenderId==rounds.Id));
            foreach(var count in new[]{1,0})
            {
                var variant=completed.Single(t=>t.Title=="Phase 6 Rounds "+count);
                Assert.Equal(count==0?"Closed":"Under Evaluation",variant.Status);
                Assert.Equal(3,await db.TenderRounds.CountAsync(r=>r.TenderId==variant.Id));
                Assert.Equal(count,await db.Bids.CountAsync(b=>b.TenderId==variant.Id && b.SubmittedRoundNumber==1));
            }
            var cancelled=completed.Single(t=>t.Title=="Phase 6 Cancel");
            var replacement=completed.Single(t=>t.Title=="Phase 6 Replacement");
            Assert.Equal("Cancelled",cancelled.Status);
            Assert.Equal("Draft",replacement.Status);
            Assert.Equal(cancelled.PurchaseRequisitionId,replacement.PurchaseRequisitionId);
            Assert.False(await db.Bids.AnyAsync(b=>b.TenderId==replacement.Id));
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }

    private sealed class LocalBrowserApp(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(options=>options.UseSqlServer(connection));
                foreach(var d in services.Where(d=>d.ServiceType==typeof(IHostedService) &&
                    d.ImplementationType==typeof(TenderClock)).ToList()) services.Remove(d);
            });
        }
    }
}
