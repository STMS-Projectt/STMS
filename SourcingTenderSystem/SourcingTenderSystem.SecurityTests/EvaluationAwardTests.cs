using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Services;
using SourcingTenderSystem.ViewModels;
using Xunit;

public sealed partial class BiddingRoundTests
{
    [Fact]
    public void Weighted_scores_normalize_then_round_once()
    {
        EvaluationCriterion[] criteria = [
            new() { Id=1, MaxScore=20, WeightPercent=60, Name="Quality", Type="Technical" },
            new() { Id=2, MaxScore=100, WeightPercent=40, Name="Cost", Type="Financial" }];
        EvaluationScore[] scores = [new(1, 1, 1, 10, null,null,null,null), new(2, 1, 1, 20, null,null,null,null),
            new(1, 1, 2, 73.12m, null,null,null,null), new(2, 1, 2, 20, null,null,null,null)];
        Assert.True(EvaluationRules.CriteriaValid(criteria));
        Assert.Equal(63.62m, EvaluationRules.Round(EvaluationRules.Weighted(criteria, scores)));
    }

    [Theory]
    [InlineData("award")]
    [InlineData("reject")]
    [InlineData("notification-failure")]
    [InlineData("race")]
    public async Task Evaluation_award_and_cancellation_http(string ending)
    {
        var template = Environment.GetEnvironmentVariable("STMS_PHASE2_TEST_SQL");
        Assert.False(string.IsNullOrWhiteSpace(template));
        var catalog = "STMS_Phase3_" + Guid.NewGuid().ToString("N");
        var connection = new SqlConnectionStringBuilder(template) { InitialCatalog = catalog }.ConnectionString;
        using var app = new BiddingApp(connection);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(catalog, db.Database.GetDbConnection().Database);
        string? reportFile = null;
        const string password = "OnlyForSynthetic!927";
        try
        {
            await db.GetService<IMigrator>().MigrateAsync("20261114210000_BiddingRoundHistory");
            // Seed without new columns using the same pre-upgrade SQL used by Phase 2 tests.
            var seed = await SeedLegacyAsync(db);
            await db.Database.MigrateAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            foreach (var id in new[] { "officer", "manager", "technical", "financial", "bidder" })
            {
                var account = (await users.FindByIdAsync(id))!;
                account.Email = id + "@example.test";
                var result = await users.AddPasswordAsync(account, password);
                Assert.True(result.Succeeded, string.Join(",",result.Errors.Select(e=>e.Code)));
            }
            Assert.True((await users.CreateAsync(new IdentityUser { Id="requester", UserName="requester", Email="requester@example.test" }, password)).Succeeded);
            var departmentId = await db.Departments.Select(d => d.Id).SingleAsync();
            var requester = new EmployeeProfile { UserId="requester", FullName="Requester", JobTitle="Synthetic", DepartmentId=departmentId, IsActive=true };
            db.Add(requester);
            var topRole = new IdentityRole { Name="TopCommittee", NormalizedName="TOPCOMMITTEE" };
            db.Roles.Add(topRole);
            await db.SaveChangesAsync();
            for (int i=1; i<=5; i++)
            {
                var id = "top" + i;
                Assert.True((await users.CreateAsync(new IdentityUser { Id=id, UserName=id, Email=id+"@example.test" }, password)).Succeeded);
                if (i<=4) Assert.True((await users.AddToRoleAsync((await users.FindByIdAsync(id))!, "TopCommittee")).Succeeded);
                db.Add(new EmployeeProfile { UserId=id, FullName=id, JobTitle="Synthetic", IsActive=true });
            }
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("""
                INSERT dbo.AspNetUserPasskeys(UserId, CredentialId, Data)
                SELECT Id, CONVERT(varbinary(64),Id), '{{}}' FROM dbo.AspNetUsers;
                """);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT dbo.AspNetRoles(Id,Name,NormalizedName) VALUES ('supplier-role','Supplier','SUPPLIER'),('officer-role','SourcingOfficer','SOURCINGOFFICER');
                INSERT dbo.AspNetUserRoles(UserId,RoleId) VALUES ('submitted','supplier-role'),('bidder','supplier-role'),('officer','officer-role');
                """);
            var request = await db.PurchaseRequisitions.SingleAsync();
            request.CreatedByEmployeeProfileId = requester.Id;
            var draft = new Tender { PurchaseRequisitionId=request.Id, Title="Publish check", Description="Test", Terms="Test",
                OpenDate=DateTime.UtcNow.AddMinutes(-1), CloseDate=DateTime.UtcNow.AddHours(1), Status="Draft",
                CreatedByEmployeeProfileId=(await db.EmployeeProfiles.SingleAsync(e=>e.UserId=="officer")).Id, CreatedAt=DateTime.UtcNow };
            // Keep the separate publication probe independent of the cancellation/retender scenario.
            var probe = new PurchaseRequisition { Title="Probe", Reason="Probe", Priority="Normal", Status="Converted to Tender", DepartmentId=departmentId,
                CreatedByEmployeeProfileId=requester.Id, TargetDate=DateTime.UtcNow, CreatedAt=DateTime.UtcNow };
            draft.PurchaseRequisition=probe;
            db.Add(draft);
            await db.SaveChangesAsync();
            using var officer = Client(app,"officer","SourcingOfficer");
            using var manager = Client(app,"manager","SourcingManager");
            using var technical = Client(app,"technical","TechnicalCommittee");
            using var financial = Client(app,"financial","FinancialCommittee");
            using var department = Client(app,"requester","RequestingDepartment");
            var officerToken = await Token(officer);
            var managerToken = await Token(manager);
            async Task<HttpStatusCode> Send(HttpClient c, string token, string path, Dictionary<string,string>? fields=null)
                => (await Post(c, token, path, fields ?? new())).StatusCode;
            await Send(officer,officerToken,$"/Tenders/Publish/{draft.Id}");
            Assert.Equal("Draft", await db.Tenders.Where(t=>t.Id==draft.Id).Select(t=>t.Status).SingleAsync());
            Dictionary<string,string> criterion = new() { ["Name"]="Quality",["Type"]="Technical",["MaxScore"]="100",["WeightPercent"]="100" };
            Assert.Equal(HttpStatusCode.Redirect, await Send(officer,officerToken,$"/Tenders/Criterion/{draft.Id}",criterion));
            await Send(officer,officerToken,$"/Tenders/Publish/{draft.Id}");
            Assert.Equal("Open", await db.Tenders.Where(t=>t.Id==draft.Id).Select(t=>t.Status).SingleAsync());
            Assert.Equal(HttpStatusCode.Conflict, await Send(officer,officerToken,$"/Tenders/Criterion/{draft.Id}",criterion));

            var tenderId = seed.Tender;
            // Set up a third round and two additional offers; controller evaluates all actual persisted bids.
            await db.Tenders.Where(t=>t.Id==tenderId).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.Counter,3));
            db.Add(new TenderRound { TenderId=tenderId, Number=3, ClosingOutcome="DeadlineReached" });
            db.Add(new EvaluationCriterion { TenderId=tenderId, Name="Quality", Type="Technical", MaxScore=100, WeightPercent=60 });
            db.Add(new EvaluationCriterion { TenderId=tenderId, Name="Costs", Type="Financial", MaxScore=100, WeightPercent=40 });
            var runnerUp = new Bid { TenderId=tenderId, SupplierProfileId=seed.Bidder, Status="Submitted", SubmittedAt=DateTime.UtcNow, Currency="JOD", TotalPrice=25 };
            var excluded = new Bid { TenderId=tenderId, SupplierProfileId=seed.NewSupplier, Status="Non-Compliant", ComplianceNotes="Missing evidence", SubmittedAt=DateTime.UtcNow, Currency="JOD", TotalPrice=5 };
            db.AddRange(runnerUp, excluded);
            await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.Redirect, await Send(officer,officerToken,"/Clarifications/Create",
                new() { ["Kind"]="bid", ["RecordId"]=runnerUp.Id.ToString(), ["Recipient"]="s"+seed.Bidder, ["Subject"]="Evidence", ["Description"]="Provide evidence." }));
            await db.Entry(runnerUp).ReloadAsync();
            Assert.Equal("Pending Clarification", runnerUp.Status);
            Assert.NotNull(runnerUp.ComplianceClarificationId);
            await Send(officer,officerToken,$"/Tenders/AssignCommittee/{tenderId}");
            Assert.False(await db.TenderCommitteeMembers.AnyAsync(m=>m.TenderId==tenderId));
            using var bidder = Client(app,"bidder","Supplier");
            var bidToken=await Token(bidder);
            var clarificationId=runnerUp.ComplianceClarificationId;
            Assert.Equal(HttpStatusCode.Redirect, await Send(bidder,bidToken,$"/Clarifications/Answer/{clarificationId}",new(){["Response"]="Attached evidence."}));
            Assert.Equal(HttpStatusCode.Redirect, await Send(officer,officerToken,$"/Clarifications/Resolve/{clarificationId}"));
            foreach(var id in new[]{seed.Bid,runnerUp.Id}) await Send(officer,officerToken,$"/Compliance/MarkCompliant/{id}");
            await Send(officer,officerToken,$"/Tenders/AssignCommittee/{tenderId}");
            Assert.Equal(4, await db.TenderCommitteeMembers.CountAsync(m=>m.TenderId==tenderId && m.CommitteeType=="Evaluation"));

            await Send(manager,managerToken,$"/CommitteeReviews/SubmitEvaluation/{tenderId}");
            Assert.False(await db.EvaluationSubmissions.AnyAsync(s=>s.TenderId==tenderId));
            var criteria = await db.EvaluationCriteria.Where(c=>c.TenderId==tenderId).ToListAsync();
            foreach (var c in new[] { manager,technical,financial,department })
            {
                var token = c==manager ? managerToken : await Token(c);
                foreach (var bid in new[]{seed.Bid,runnerUp.Id})
                foreach (var item in criteria)
                    Assert.Equal(HttpStatusCode.Redirect, await Send(c,token,$"/CommitteeReviews/SaveScore/{bid}?criterionId={item.Id}",
                        new(){["Score"]=bid==seed.Bid ? "90":"70",["Notes"]="Synthetic"}));
                if (c == manager)
                    Assert.False(await db.EvaluationSubmissions.AnyAsync(s=>s.TenderId==tenderId));
                await Send(c,token,$"/CommitteeReviews/SubmitEvaluation/{tenderId}");
                Assert.Equal(HttpStatusCode.Conflict, await Send(c,token,$"/CommitteeReviews/SaveScore/{seed.Bid}?criterionId={criteria[0].Id}", new(){["Score"]="1"}));
            }
            Assert.Equal(4, await db.EvaluationSubmissions.CountAsync(s=>s.TenderId==tenderId));
            Assert.Equal(4, await db.AuditLogs.CountAsync(a=>a.EntityId==tenderId && a.Action=="EvaluationSubmitted"));
            Assert.False(await db.TenderApprovals.AnyAsync());
            async Task<HttpStatusCode> Upload(int bid)
            {
                using var form=new MultipartFormDataContent();
                form.Add(new StringContent(managerToken),"__RequestVerificationToken");
                form.Add(new StringContent(bid.ToString()),"RecommendedBidId");
                form.Add(new StringContent("Justification"),"Summary");
                form.Add(new ByteArrayContent("%PDF-1.4 synthetic test"u8.ToArray()),"File","test.pdf");
                return (await manager.PostAsync($"/EvaluationWorkflow/Upload/{tenderId}",form)).StatusCode;
            }
            await Upload(excluded.Id);
            Assert.False(await db.EvaluationReports.AnyAsync(r=>r.TenderId==tenderId));
            Assert.Equal(HttpStatusCode.Redirect, await Upload(seed.Bid));
            var report = await db.EvaluationReports.AsNoTracking().SingleAsync(r=>r.TenderId==tenderId);
            reportFile = report.FileKey;
            Assert.Contains("Non-Compliant",report.SupplierComparison);
            Assert.Equal(90m, System.Text.Json.JsonSerializer.Deserialize<List<SupplierEvaluationResult>>(report.SupplierComparison)!.First().Total);
            var approve = $"/EvaluationWorkflow/Approve/{tenderId}";
            Dictionary<string,string> Decision(string stage,string? vote=null,string? reason=null)
            {
                Dictionary<string,string> data=new(){["expectedStage"]=stage,["confirmDecision"]="true",["password"]=password};
                if(vote!=null)data["vote"]=vote;
                if(reason!=null)data["comments"]=reason;
                return data;
            }
            Assert.Equal(HttpStatusCode.Conflict, await Send(manager,managerToken,approve,Decision("Pending Manager Approval")));
            Assert.Equal(HttpStatusCode.Redirect, await Send(officer,officerToken,approve,Decision("Pending Officer Approval")));
            Assert.Equal("Awaiting Manager Review",await State());
            Assert.Equal(HttpStatusCode.Redirect, await Send(manager,managerToken,approve,Decision("Pending Manager Approval")));
            Assert.Equal("Awaiting Committee Decision",await State());
            using var first = Client(app,"top1","TopCommittee");
            using var second = Client(app,"top2","TopCommittee");
            using var third = Client(app,"top3","TopCommittee");
            using var fourth = Client(app,"top4","TopCommittee");
            using var fifth = Client(app,"top5","TopCommittee");
            const string stage = "Pending Top Committee";
            async Task<HttpStatusCode> VoteOn(HttpClient client,string vote="approve",string? reason=null) =>
                await Send(client,await Token(client),approve,Decision(stage,vote,reason));
            Assert.Equal(HttpStatusCode.NotFound,await VoteOn(fifth));
            Assert.Equal(HttpStatusCode.BadRequest,await VoteOn(first,"reject","   "));
            foreach(var c in new[]{first,second,third})
            {
                Assert.Equal(HttpStatusCode.Redirect,await VoteOn(c));
                Assert.Equal("Awaiting Committee Decision",await State());
            }
            Assert.Equal(HttpStatusCode.Forbidden,await VoteOn(first));
            if (ending=="notification-failure")
            {
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE TRIGGER dbo.FailSyntheticAwardNotification ON dbo.Notifications AFTER INSERT AS
                    BEGIN
                      IF EXISTS (SELECT 1 FROM inserted WHERE Title=N'Tender awarded to you')
                        THROW 51099, 'Synthetic failure', 1;
                    END
                    """);
                try { Assert.Equal(HttpStatusCode.InternalServerError, await VoteOn(fourth)); }
                catch (Exception ex) when (ex.ToString().Contains("Synthetic failure")) { }
                Assert.Equal("Awaiting Committee Decision",await State());
                Assert.False(await db.Bids.AnyAsync(b=>b.TenderId==tenderId && b.AwardOutcome!=null));
                Assert.Equal(3,await db.TenderApprovals.CountAsync(a=>a.EvaluationReportId==report.Id && a.Decision=="Top Committee Approved"));
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER dbo.FailSyntheticAwardNotification");
            }
            if (ending=="reject")
            {
                Assert.Equal(HttpStatusCode.Redirect,await VoteOn(fourth,"reject","Review concerns"));
                Assert.Equal("Cancelled",await State());
                Assert.Equal(HttpStatusCode.Conflict,await VoteOn(fourth));
                Assert.True(await db.Notifications.AnyAsync(n=>n.Title=="Tender cancelled: handoff required" && n.Message.Contains("Review concerns")));
                Assert.Equal(HttpStatusCode.Redirect,await Send(manager,managerToken,$"/EvaluationWorkflow/ReturnToOfficer/{tenderId}"));
                var officerNotice=await db.Notifications.AsNoTracking().SingleAsync(n=>n.Title=="Tender cancelled: create a new tender");
                Assert.Equal($"/Tenders/Create?purchaseRequisitionId={request.Id}",officerNotice.Link);
                Assert.Contains("Review concerns",officerNotice.Message);
                Assert.Equal(1,await db.Tenders.CountAsync(t=>t.PurchaseRequisitionId==request.Id));
                var next = DateTime.UtcNow.AddDays(1);
                Assert.Equal(HttpStatusCode.Redirect,await Send(officer,officerToken,"/Tenders/Create",new(){
                    ["PurchaseRequisitionId"]=request.Id.ToString(),["Title"]="Replacement",["Description"]="Replacement",["Terms"]="Terms",
                    ["OpenDate"]=next.ToString("yyyy-MM-ddTHH:mm"),["CloseDate"]=next.AddDays(1).ToString("yyyy-MM-ddTHH:mm")}));
                var replacement=await db.Tenders.AsNoTracking().SingleAsync(t=>t.PurchaseRequisitionId==request.Id && t.Id!=tenderId);
                Assert.Equal("Draft",replacement.Status);
                Assert.False(await db.Bids.AnyAsync(b=>b.TenderId==replacement.Id));
                Assert.Equal(3,await db.Bids.CountAsync(b=>b.TenderId==tenderId));
                Assert.True(await db.Invitations.AnyAsync(i=>i.Id==seed.Invitation));
                Assert.True(await db.TenderRounds.AnyAsync(r=>r.TenderId==tenderId && r.Number==3));
                Assert.Equal(4,await db.EvaluationSubmissions.CountAsync(s=>s.TenderId==tenderId));
                Assert.True(await db.TenderApprovals.AnyAsync(a=>a.EvaluationReportId==report.Id && a.Comments=="Review concerns"));
                Assert.Equal("Cancelled",await State());
            }
            else
            {
                var results = ending=="race" ? await Task.WhenAll(VoteOn(fourth), VoteOn(fourth,"reject","Concurrent rejection")) : [await VoteOn(fourth)];
                Assert.Contains(HttpStatusCode.Redirect,results);
                if (ending=="race" && await State()=="Cancelled")
                {
                    Assert.Equal(1,await db.TenderApprovals.CountAsync(a=>a.EvaluationReportId==report.Id && a.Decision=="Top Committee Rejected"));
                    Assert.False(await db.Notifications.AnyAsync(n=>n.Title=="Tender awarded to you"));
                }
                else
                {
                    Assert.Equal("Awarded",await State());
                    Assert.Equal(4,await db.TenderApprovals.CountAsync(a=>a.EvaluationReportId==report.Id && a.Decision=="Top Committee Approved"));
                    Assert.Equal("Awarded",await db.Bids.Where(b=>b.Id==seed.Bid).Select(b=>b.AwardOutcome).SingleAsync());
                    Assert.Equal("Unsuccessful",await db.Bids.Where(b=>b.Id==runnerUp.Id).Select(b=>b.AwardOutcome).SingleAsync());
                    Assert.True(await db.Notifications.AnyAsync(n=>n.SupplierProfileId==seed.Submitted && n.Title=="Tender awarded to you"));
                    Assert.True(await db.Notifications.AnyAsync(n=>n.SupplierProfileId==seed.Bidder && n.Title=="Bid not selected"));
                    Assert.Contains("Result: Unsuccessful", await bidder.GetStringAsync($"/Bids/Details/{runnerUp.Id}"));
                }
                Assert.True(await db.EvaluationReports.AnyAsync(r=>r.Id==report.Id && r.FinalizedAt!=null));
            }
            Task<string> State()=>db.Tenders.Where(t=>t.Id==tenderId).Select(t=>t.Status).SingleAsync();
        }
        finally
        {
            if(reportFile!=null) app.Services.GetRequiredService<SupplierFileStore>().Delete(reportFile);
            await db.Database.EnsureDeletedAsync();
        }
    }
}
