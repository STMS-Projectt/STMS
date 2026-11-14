using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Services;
using Xunit;

public sealed partial class BiddingRoundTests
{
    [Theory]
    [InlineData("R01", 1, 0, false, true)]
    [InlineData("R02", 1, 1, false, true)]
    [InlineData("R03", 1, 2, false, true)]
    [InlineData("R04", 1, 3, true, false)]
    [InlineData("R05", 2, 2, false, true)]
    [InlineData("R06", 3, 1, true, false)]
    [InlineData("R07", 3, 2, true, false)]
    [InlineData("R08/R09", 3, 0, false, false)]
    public void Closed_round_threshold(string scenario, int round, int bids, bool evaluate, bool reopen)
    {
        var tender = new Tender { Status = "Closed", Counter = round };
        Assert.True(TenderRules.CanEvaluate(tender, bids) == evaluate, scenario);
        Assert.True(TenderRules.CanReopen(tender, bids) == reopen, scenario);
    }

    [Fact]
    public void R17_R20_R21_Deadline_and_stage_are_exclusive()
    {
        var now = DateTime.UtcNow;
        var tender = new Tender { Counter = 2, Status = "Open", OpenDate = now.AddHours(-1), CloseDate = now };
        Assert.True(TenderRules.IsOpen(tender, now.AddTicks(-1)));
        Assert.False(TenderRules.IsOpen(tender, now));
        tender.Status = "Under Evaluation";
        Assert.False(TenderRules.CanReopen(tender, 0));
        Assert.False(TenderRules.IsOpen(tender, now.AddTicks(-1)));
    }

    [Fact]
    public async Task SqlServer_upgrade_and_HTTP_bidding_lifecycle()
    {
        var template = Environment.GetEnvironmentVariable("STMS_PHASE2_TEST_SQL");
        Assert.False(string.IsNullOrWhiteSpace(template), "Set STMS_PHASE2_TEST_SQL to a disposable SQL Server instance.");
        var connection = new SqlConnectionStringBuilder(template) { InitialCatalog = "STMS_Phase2_" + Guid.NewGuid().ToString("N") }.ConnectionString;
        using var app = new BiddingApp(connection);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(new SqlConnectionStringBuilder(connection).InitialCatalog, db.Database.GetDbConnection().Database);
        var created = false;
        try
        {
            await db.GetService<IRelationalDatabaseCreator>().CreateAsync();
            created = true;
            await db.GetService<IMigrator>().MigrateAsync("20261114200000_AllowTenderHistory");
            var seed = await SeedLegacyAsync(db);
            // Verify distinct counts and migration safety against a drifted schema.
            await db.Database.ExecuteSqlRawAsync("DROP INDEX IX_Bids_TenderId_SupplierProfileId ON dbo.Bids");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT dbo.Bids(TenderId,SupplierProfileId,TotalPrice,Currency,DeliveryDays,ValidityDays,Status,SubmittedAt)
                VALUES ({seed.Tender}, {seed.Submitted}, 10, 'JOD', 1, 1, 'Submitted', SYSUTCDATETIME())
                """);
            Assert.Equal(1, await TenderRules.EligibleSuppliers(db, seed.Tender).CountAsync());
            var blocked = await Assert.ThrowsAsync<SqlException>(() => db.Database.MigrateAsync());
            Assert.Equal(51013, blocked.Number);
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.Bids WHERE TenderId={seed.Tender} AND Id<>{seed.Bid}");
            await db.Database.MigrateAsync();
            await db.Database.ExecuteSqlRawAsync("INSERT dbo.AspNetUserPasskeys(UserId, CredentialId, Data) VALUES ('officer', 0x01, '{{}}'), ('bidder', 0x02, '{{}}')");
            db.ChangeTracker.Clear();
            Assert.Equal(1, (await db.Bids.FindAsync(seed.Bid))!.SubmittedRoundNumber);
            Assert.Equal("Declined", (await db.Invitations.FindAsync(seed.Invitation))!.Status);
            Assert.Equal(1, (await db.Invitations.FindAsync(seed.Invitation))!.RoundNumber);
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT dbo.Bids(TenderId,SupplierProfileId,TotalPrice,Currency,DeliveryDays,ValidityDays,Status)
                VALUES ({seed.Tender}, {seed.Submitted}, 0, 'JOD', 1, 1, 'Draft')
                """));

            using var officer = Client(app, "officer", "SourcingOfficer");
            using var supplier = Client(app, "bidder", "Supplier");
            var officerToken = await Token(officer);
            var supplierToken = await Token(supplier);
            var reopen = $"/Tenders/Reopen/{seed.Tender}";
            var deadline = DateTime.UtcNow.AddHours(2).ToString("yyyy-MM-ddTHH:mm:ss");
            var reopenFields = new Dictionary<string, string> { ["closeDate"] = deadline, ["reason"] = "Need more bids" };
            var denied = await Post(supplier, supplierToken, reopen, reopenFields);
            Assert.True(denied.StatusCode == HttpStatusCode.Forbidden ||
                denied.Headers.Location?.OriginalString.Contains("AccessDenied") == true);
            var reopenResponses = await Task.WhenAll(Post(officer, officerToken, reopen, reopenFields),
                Post(officer, officerToken, reopen, reopenFields));
            Assert.All(reopenResponses, r => Assert.Equal(HttpStatusCode.Redirect, r.StatusCode));
            Assert.Equal(2, await db.TenderRounds.CountAsync(r => r.TenderId == seed.Tender));
            Assert.Contains("Round 2 of 3", await officer.GetStringAsync($"/Tenders/Details/{seed.Tender}"));
            var inviteForm = await officer.GetStringAsync($"/Invitations/Create?tenderId={seed.Tender}");
            Assert.DoesNotContain(">Submitted</span>", inviteForm);
            Assert.Contains("Round 2", inviteForm);
            Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.EntityId == seed.Tender && a.Action == "Reopen"));

            var inviteUrl = "/Invitations/Create";
            var invalid = await Invite(officer, officerToken, inviteUrl, seed.Tender, seed.Submitted, deadline);
            Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
            Assert.Contains("already submitted", await invalid.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Redirect, (await Invite(officer, officerToken, inviteUrl, seed.Tender, seed.Bidder, deadline)).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await Invite(officer, officerToken, inviteUrl, seed.Tender, seed.NewSupplier, deadline)).StatusCode);
            var invitations = await db.Invitations.AsNoTracking().Where(i => i.TenderId == seed.Tender).ToListAsync();
            Assert.Equal(3, invitations.Count);
            Assert.Contains(invitations, i => i.Id == seed.Invitation && i.Status == "Declined" && i.RoundNumber == 1);
            Assert.Equal("Submitted", (await db.Bids.AsNoTracking().SingleAsync(b => b.Id == seed.Bid)).Status);
            var current = invitations.Single(i => i.SupplierProfileId == seed.Bidder && i.RoundNumber == 2);
            Assert.Equal(HttpStatusCode.Redirect, (await Post(supplier, supplierToken, $"/SupplierWorkspace/Respond/{current.Id}",
                new() { ["decision"] = "Accepted" })).StatusCode);

            var create = $"/Bids/Create?tenderId={seed.Tender}";
            var fields = new Dictionary<string,string> { ["Currency"] = "JOD", ["DeliveryDays"] = "3", ["ValidityDays"] = "7" };
            var drafts = await Task.WhenAll(Post(supplier, supplierToken, create, fields), Post(supplier, supplierToken, create, fields));
            Assert.All(drafts, r => Assert.Equal(HttpStatusCode.Redirect, r.StatusCode));
            var draft = await db.Bids.SingleAsync(b => b.TenderId == seed.Tender && b.SupplierProfileId == seed.Bidder);
            db.Add(new BidItem { BidId = draft.Id, PurchaseRequisitionItemId = seed.Item, UnitPrice = 20, Quantity = 1 });
            db.Add(new BidDocument { BidId = draft.Id, DocumentType = "Proposal", FileName = "synthetic.pdf", FilePath = "synthetic.pdf", UploadedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            // A draft cannot be submitted when the round is inactive.
            await db.Tenders.Where(t => t.Id == seed.Tender).ExecuteUpdateAsync(set => set.SetProperty(t => t.Status, "Closed"));
            Assert.Equal(HttpStatusCode.Redirect, (await Post(supplier, supplierToken, $"/Bids/Submit/{draft.Id}", new())).StatusCode);
            await db.Entry(draft).ReloadAsync();
            Assert.Equal("Draft", draft.Status);
            await db.Tenders.Where(t => t.Id == seed.Tender).ExecuteUpdateAsync(set => set.SetProperty(t => t.Status, "Open"));
            var submissions = await Task.WhenAll(Post(supplier, supplierToken, $"/Bids/Submit/{draft.Id}", new()),
                Post(supplier, supplierToken, $"/Bids/Submit/{draft.Id}", new()));
            Assert.All(submissions, r => Assert.Equal(HttpStatusCode.Redirect, r.StatusCode));
            await db.Entry(draft).ReloadAsync();
            Assert.Equal("Submitted", draft.Status);
            Assert.Equal(2, draft.SubmittedRoundNumber);
            Assert.Equal(2, await TenderRules.EligibleSuppliers(db, seed.Tender).CountAsync());
            await db.Tenders.Where(t => t.Id == seed.Tender).ExecuteUpdateAsync(s => s.SetProperty(t => t.CloseDate, DateTime.UtcNow.AddSeconds(-1)));
            await TenderClock.AdvanceAsync(db);
            await TenderClock.AdvanceAsync(db);
            Assert.Equal(2, (await db.TenderRounds.AsNoTracking().SingleAsync(r => r.TenderId == seed.Tender && r.Number == 2)).EligibleSuppliersAtClose);
            Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.EntityId == seed.Tender && a.Action == "CloseAtDeadline"));
            Assert.Equal(HttpStatusCode.Redirect, (await Post(officer, officerToken, reopen, reopenFields)).StatusCode);
            Assert.Equal(3, await db.TenderRounds.CountAsync(r => r.TenderId == seed.Tender));
            await db.Tenders.Where(t => t.Id == seed.Tender).ExecuteUpdateAsync(s => s.SetProperty(t => t.CloseDate, DateTime.UtcNow.AddSeconds(-1)));
            db.ChangeTracker.Clear();
            await TenderClock.AdvanceAsync(db);
            await Post(officer, officerToken, reopen, reopenFields);
            Assert.Equal(3, await db.TenderRounds.CountAsync(r => r.TenderId == seed.Tender));
            Assert.Equal("Closed", (await db.Tenders.AsNoTracking().SingleAsync(t => t.Id == seed.Tender)).Status);

            db.Add(new EvaluationCriterion { TenderId = seed.Tender, Name = "Legacy published quality", Type = "Technical", WeightPercent = 100, MaxScore = 100 });
            await db.SaveChangesAsync();
            foreach (var id in new[] { seed.Bid, draft.Id })
                Assert.Equal(HttpStatusCode.Redirect, (await Post(officer, officerToken, $"/Compliance/MarkCompliant/{id}", new())).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await Post(officer, officerToken,
                $"/Tenders/AssignCommittee/{seed.Tender}", new())).StatusCode);
            Assert.Equal(4, await db.TenderCommitteeMembers.CountAsync(m => m.TenderId == seed.Tender));
            Assert.Equal("Under Evaluation", (await db.Tenders.AsNoTracking().SingleAsync(t => t.Id == seed.Tender)).Status);
            await Post(officer, officerToken, reopen, reopenFields);
            Assert.Equal(3, await db.TenderRounds.CountAsync(r => r.TenderId == seed.Tender));
        }
        finally
        {
            if (created) await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<(int Tender, int Submitted, int Bidder, int NewSupplier, int Invitation, int Bid, int Item)> SeedLegacyAsync(ApplicationDbContext db)
    {
        // Use SQL for pre-upgrade rows: the current EF model contains columns absent in this version.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT dbo.AspNetUsers(Id,UserName,AccessFailedCount,EmailConfirmed,LockoutEnabled,PhoneNumberConfirmed,TwoFactorEnabled)
            SELECT u,u,0,0,0,0,0 FROM (VALUES ('officer'),('submitted'),('bidder'),('new'),('manager'),('technical'),('financial')) users(u);
            INSERT dbo.AspNetRoles(Id,Name,NormalizedName) VALUES ('manager','SourcingManager','SOURCINGMANAGER'),('technical','TechnicalCommittee','TECHNICALCOMMITTEE'),('financial','FinancialCommittee','FINANCIALCOMMITTEE');
            INSERT dbo.AspNetUserRoles(UserId,RoleId) VALUES ('manager','manager'),('technical','technical'),('financial','financial');
            INSERT dbo.Departments(Name,IsActive) VALUES ('Synthetic',1);
            DECLARE @dep int = SCOPE_IDENTITY();
            INSERT dbo.EmployeeProfiles(UserId,FullName,DepartmentId,IsActive,JobTitle) VALUES ('officer','Officer',@dep,1,'Synthetic');
            DECLARE @officer int = SCOPE_IDENTITY();
            INSERT dbo.EmployeeProfiles(UserId,FullName,DepartmentId,IsActive,JobTitle)
            SELECT u,u,@dep,1,'Synthetic' FROM (VALUES ('manager'),('technical'),('financial')) staff(u);
            INSERT dbo.SupplierProfiles(UserId,CompanyName,TaxNumber,CommercialRegNo,ContactPhone,Address,Status,CreatedAt)
            VALUES ('submitted','Submitted','S','S','0','Synthetic','Approved',SYSUTCDATETIME()),
                   ('bidder','Bidder','B','B','0','Synthetic','Approved',SYSUTCDATETIME()),
                   ('new','New','N','N','0','Synthetic','Approved',SYSUTCDATETIME());
            INSERT dbo.PurchaseRequisitions(Title,Reason,Priority,TargetDate,Status,DepartmentId,CreatedByEmployeeProfileId,CreatedAt)
            VALUES ('Test request','Testing','Normal',SYSUTCDATETIME(),'Converted to Tender',@dep,@officer,SYSUTCDATETIME());
            DECLARE @request int = SCOPE_IDENTITY();
            INSERT dbo.PurchaseRequisitionItems(PurchaseRequisitionId,ItemName,Quantity,Unit,Specifications)
            VALUES (@request,'Test item',1,'each','Synthetic');
            INSERT dbo.Tenders(PurchaseRequisitionId,Title,Description,Terms,OpenDate,CloseDate,Status,CreatedByEmployeeProfileId,CreatedAt,[counter])
            VALUES (@request,'Test tender','Synthetic','Synthetic',DATEADD(day,-2,SYSUTCDATETIME()),DATEADD(day,-1,SYSUTCDATETIME()),'Closed',@officer,SYSUTCDATETIME(),1);
            DECLARE @tender int = SCOPE_IDENTITY();
            INSERT dbo.Invitations(TenderId,SupplierProfileId,Status,SentAt,ExpiresAt,RespondedAt,DeclineReason)
            SELECT @tender,Id,'Declined',DATEADD(day,-2,SYSUTCDATETIME()),DATEADD(day,-1,SYSUTCDATETIME()),DATEADD(day,-2,SYSUTCDATETIME()),'Synthetic'
            FROM dbo.SupplierProfiles WHERE UserId='bidder';
            INSERT dbo.Bids(TenderId,SupplierProfileId,TotalPrice,Currency,DeliveryDays,ValidityDays,Status,SubmittedAt)
            SELECT @tender,Id,10,'JOD',1,1,'Submitted',DATEADD(day,-2,SYSUTCDATETIME()) FROM dbo.SupplierProfiles WHERE UserId='submitted';
            """);
        return (await db.Tenders.Select(t=>t.Id).SingleAsync(),
            await db.SupplierProfiles.Where(s=>s.UserId=="submitted").Select(s=>s.Id).SingleAsync(),
            await db.SupplierProfiles.Where(s=>s.UserId=="bidder").Select(s=>s.Id).SingleAsync(),
            await db.SupplierProfiles.Where(s=>s.UserId=="new").Select(s=>s.Id).SingleAsync(),
            await db.Invitations.Select(i=>i.Id).SingleAsync(),
            await db.Bids.Select(b=>b.Id).SingleAsync(),
            await db.PurchaseRequisitionItems.Select(i=>i.Id).SingleAsync());
    }

    private static Task<HttpResponseMessage> Invite(HttpClient client, string token, string url, int tender, int supplier, string deadline) =>
        Post(client, token, url, new() { ["TenderId"] = tender.ToString(), ["ExpiresAt"] = deadline, ["selectedSuppliers"] = supplier.ToString() });

    private static Task<HttpResponseMessage> Post(HttpClient client, string token, string url, Dictionary<string,string> values)
    {
        var fields = new Dictionary<string,string>(values) { ["__RequestVerificationToken"] = token };
        return client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    private static HttpClient Client(BiddingApp app, string user, string role)
    {
        var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-User", user);
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private static async Task<string> Token(HttpClient client) =>
        await client.GetStringAsync("/__bidding-token");

    private sealed class BiddingApp(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string,string?> {
                ["ConnectionStrings:DefaultConnection"] = connection,
                // Keep integration tests offline: exchange rates become "unavailable" instead of calling the internet.
                ["ExchangeRates:BaseUrl"] = "http://127.0.0.1:9/" }));
            builder.ConfigureLogging(l => l.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connection));
                services.AddAuthentication(o => { o.DefaultAuthenticateScheme = "Synthetic"; o.DefaultForbidScheme = "Synthetic"; })
                    .AddScheme<AuthenticationSchemeOptions, SyntheticAuth>("Synthetic", _ => { });
                services.AddSingleton<IStartupFilter, AuthenticatedToken>();
                foreach (var d in services.Where(d => d.ServiceType == typeof(IHostedService) &&
                    d.ImplementationType == typeof(TenderClock)).ToList()) services.Remove(d);
            });
        }
    }

    private sealed class AuthenticatedToken : IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) =>
            app =>
            {
                app.Use(nextRequest => async ctx =>
                {
                    if (ctx.Request.Path != "/__bidding-token") { await nextRequest(ctx); return; }
                    ctx.User = (await ctx.AuthenticateAsync("Synthetic")).Principal!;
                    var token = ctx.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(ctx);
                    await ctx.Response.WriteAsync(token.RequestToken!);
                });
                next(app);
            };
    }

    private sealed class SyntheticAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var user))
                return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, user.ToString()),
                new Claim(ClaimTypes.Role, Request.Headers["X-Test-Role"].ToString())], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
