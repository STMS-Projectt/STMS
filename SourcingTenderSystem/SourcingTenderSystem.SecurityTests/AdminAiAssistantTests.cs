using SourcingTenderSystem.Services;
using Xunit;

public sealed class AdminAiAssistantTests
{
    [Theory]
    [InlineData("i want to add new department", "create_department")]
    [InlineData("اعمل قسم جديد اسمه المشتريات", "create_department")]
    [InlineData("Create a category called Laptops", "create_category")]
    [InlineData("ضيف تصنيف جديد", "create_category")]
    [InlineData("add employee Sara to the IT department", "create_employee")]
    [InlineData("ضيف موظف اسمه أحمد علي بقسم المشتريات", "create_employee")]
    [InlineData("كم مناقصة جديدة عنا؟", null)]
    [InlineData("Who offered the lowest prices?", null)]
    [InlineData("Supplier participation", null)]
    public void The_action_is_decided_from_the_words_of_the_request(string prompt, string? expected) =>
        Assert.Equal(expected, AdminAiAssistant.DetectAction(prompt));

    [Fact]
    public void Parse_reads_the_fields_and_never_keeps_a_password()
    {
        var parsed = AdminAiAssistant.Parse("""
            <think>x</think>```json
            { "message": "Review the new employee.",
              "fields": { "fullName": "Ahmad Ali", "email": "ahmad@example.com", "role": "SourcingOfficer", "password": "123456", "nested": { "x": 1 } } }
            ```
            """);
        var (message, fields) = parsed!.Value;
        Assert.Equal("Review the new employee.", message);
        Assert.Equal("Ahmad Ali", fields["FULLNAME"]);
        Assert.False(fields.ContainsKey("password"));
        Assert.False(fields.ContainsKey("nested"));
        Assert.Null(AdminAiAssistant.Parse("not json"));
    }
}
