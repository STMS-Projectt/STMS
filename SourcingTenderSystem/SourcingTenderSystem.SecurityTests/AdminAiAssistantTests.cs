using SourcingTenderSystem.Services;
using Xunit;

public sealed class AdminAiAssistantTests
{
    [Theory]
    [InlineData("اعمل قسم جديد اسمه المشتريات", true)]
    [InlineData("ضيف موظف اسمه أحمد علي", true)]
    [InlineData("Create a category called Laptops", true)]
    [InlineData("add employee Sara, sourcing officer", true)]
    [InlineData("كم مناقصة جديدة عنا؟", false)]
    [InlineData("Who offered the lowest prices?", false)]
    public void Only_create_requests_call_the_admin_assistant(string prompt, bool expected) =>
        Assert.Equal(expected, AdminAiAssistant.LooksLikeCreateRequest(prompt));

    [Fact]
    public void Parse_reads_the_action_and_never_keeps_a_password()
    {
        var parsed = AdminAiAssistant.Parse("""
            <think>x</think>```json
            { "action": "create_employee", "message": "Review the new employee.",
              "fields": { "fullName": "Ahmad Ali", "email": "ahmad@example.com", "role": "SourcingOfficer", "password": "123456", "nested": { "x": 1 } } }
            ```
            """);
        var (action, message, fields) = parsed!.Value;
        Assert.Equal("create_employee", action);
        Assert.Equal("Review the new employee.", message);
        Assert.Equal("Ahmad Ali", fields["FULLNAME"]);
        Assert.False(fields.ContainsKey("password"));
        Assert.False(fields.ContainsKey("nested"));
    }

    [Theory]
    [InlineData("{ \"action\": \"none\", \"message\": \"hi\" }")]
    [InlineData("{ \"action\": \"delete_department\", \"fields\": { \"name\": \"IT\" } }")]
    [InlineData("not json")]
    public void Parse_ignores_anything_that_is_not_a_create_action(string reply) =>
        Assert.Null(AdminAiAssistant.Parse(reply));
}
