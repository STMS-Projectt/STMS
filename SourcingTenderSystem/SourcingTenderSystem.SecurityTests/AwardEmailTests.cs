using SourcingTenderSystem.Services;
using Xunit;

public sealed class AwardEmailTests
{
    private static AwardEmailData Data() => new(29, "Mobile phones", "eyad mansur", 13050m, 2088m, 15138m, "JOD", 14,
        12, null, null, null, "https://stms.test/EvaluationWorkflow/Details/29", null, "https://stms.test/upload", []);

    [Fact]
    public void Unsuccessful_supplier_email_says_not_selected_and_hides_the_winning_price()
    {
        var html = AwardEmailService.BuildUnsuccessfulHtml(Data(),
            new AwardRecipient("ahmad@test.com", "ahmad ali", false, Unsuccessful: true));
        Assert.Contains("Result of your bid", html);
        Assert.Contains("Dear ahmad ali", html);
        Assert.Contains("not selected", html);
        Assert.DoesNotContain("View your bid", html);
        Assert.DoesNotContain("<a ", html);
        Assert.DoesNotContain("15,138", html);
        Assert.DoesNotContain("eyad mansur", html);
    }
}
