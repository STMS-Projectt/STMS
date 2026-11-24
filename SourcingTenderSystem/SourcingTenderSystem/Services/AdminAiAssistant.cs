using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Security;

namespace SourcingTenderSystem.Services;

/// <summary>An item of a drop-down list in the confirmation card (department or role).</summary>
public record AdminAiOption(string Value, string Label);

/// <summary>
/// What the AI suggests the admin should create. NOTHING is saved here: the chat shows the fields in a
/// confirmation form that posts to the normal Departments / Categories / Employees "Create" pages,
/// so the usual validation, permissions and duplicate checks still apply.
/// </summary>
public class AdminAiAction
{
    public string Action { get; set; } = "";               // create_department | create_category | create_employee
    public string Url { get; set; } = "";                  // the Create page the confirmation form posts to (set by the controller)
    public Dictionary<string, string> Fields { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<AdminAiOption> Departments { get; set; } = [];
    public List<AdminAiOption> Roles { get; set; } = [];
}

/// <summary>
/// AI helper for the admin: turns a request such as "create a department called Procurement" or
/// "add employee Ahmad Ali, sourcing officer in Procurement, ahmad@x.com" into a suggested action.
/// Everything happens in ONE function: <see cref="GenerateAsync"/> (prompt in, suggestion out).
/// </summary>
public class AdminAiAssistant(ApplicationDbContext db, QwenChatbot model)
{

    private const string Instructions = """
        You help the ADMIN of a Sourcing & Tender Management System create records.
        The system already knows WHAT the admin wants to create (see ACTION). Your job is to read the details from the request.
        Reply with ONE JSON object only:
        {
          "message": "one short sentence for the admin",
          "fields": { ... }
        }
        Fields for each action (use only these names, all values are strings):
        - create_department: "name" (required), "description"
        - create_category:   "name" (required)
        - create_employee:   "fullName" (required), "email", "jobTitle", "department" (one name from DEPARTMENTS), "role" (one value from ROLES)
        Rules:
        - Copy names exactly as the admin wrote them. Do not invent emails or other details the admin did not give; leave them "".
        - Never include a password.
        - If the admin did not give a detail, leave it "". It is fine to return all fields empty: the admin fills them in.
        - Write "message" in the same language as the request.
        """;

    /// <summary>
    /// The admin's prompt in, a suggested action out (or null when the request is not a create request,
    /// or the model is not available). Nothing is written to the database.
    /// </summary>
    public async Task<(AdminAiAction Action, string Message)?> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        // 0) WHAT to create is decided here from the words of the request (reliable); the model only reads the details.
        if (DetectAction(prompt) is not { } name) return null;

        // 1) Give the model the lists it may choose from.
        var departments = await db.Departments.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.Name)
            .Select(d => new AdminAiOption(d.Id.ToString(), d.Name)).ToListAsync(cancellationToken);
        var roles = Roles.EmployeeRoles.Select(r => new AdminAiOption(r, r)).ToList();
        var context = $"ACTION: {name}\nDEPARTMENTS: {JsonSerializer.Serialize(departments.Select(d => d.Label))}\nROLES: {JsonSerializer.Serialize(Roles.EmployeeRoles)}\n\nREQUEST: {prompt}";

        // 2) Ask the model for the details. If it is off or gives no usable reply, the admin gets an empty form to fill in.
        var reply = await model.CompleteAsync(Instructions, context, cancellationToken);
        var (message, fields) = (reply is null ? null : Parse(reply)) ?? ("", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        if (message.Length == 0) message = "Fill in the details, then confirm.";

        // 3) Turn the reply into a safe suggestion: known fields only, department/role checked against the real lists.
        var action = new AdminAiAction { Action = name, Departments = departments, Roles = roles };
        string Get(string key) => fields.TryGetValue(key, out var v) ? v.Trim() : "";
        switch (name)
        {
            case "create_department":
                action.Fields["Name"] = Get("name");
                action.Fields["Description"] = Get("description");
                if (await db.Departments.AnyAsync(d => d.Name == action.Fields["Name"], cancellationToken))
                    action.Warnings.Add("A department with this name already exists.");
                break;
            case "create_category":
                action.Fields["name"] = Get("name");
                if (await db.Categories.AnyAsync(c => c.Name == action.Fields["name"], cancellationToken))
                    action.Warnings.Add("That category already exists.");
                break;
            case "create_employee":
                action.Fields["FullName"] = Get("fullName");
                action.Fields["Email"] = Get("email");
                action.Fields["JobTitle"] = Get("jobTitle");
                action.Fields["DepartmentId"] = departments.FirstOrDefault(d => string.Equals(d.Label, Get("department"), StringComparison.OrdinalIgnoreCase))?.Value ?? "";
                action.Fields["Role"] = Roles.EmployeeRoles.FirstOrDefault(r => string.Equals(r, Get("role"), StringComparison.OrdinalIgnoreCase)) ?? "";
                if (Get("department").Length > 0 && action.Fields["DepartmentId"] == "")
                    action.Warnings.Add("The department you wrote was not found. Choose one from the list.");
                if (action.Fields["Email"].Length > 0 && await db.Users.AnyAsync(u => u.Email == action.Fields["Email"], cancellationToken))
                    action.Warnings.Add("A user with this email already exists.");
                break;
        }
        return (action, message);
    }

    /// <summary>Reads the model's JSON reply: the message and the fields. Returns null when it is not JSON.</summary>
    public static (string Message, Dictionary<string, string> Fields)? Parse(string reply)
    {
        reply = Regex.Replace(reply, "<think>.*?</think>", "", RegexOptions.Singleline);
        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            if (JsonNode.Parse(reply[start..(end + 1)]) is not JsonObject json) return null;
            var fields = (json["fields"] as JsonObject ?? new JsonObject()).Where(p => p.Value is JsonValue)
                .ToDictionary(p => p.Key, p => p.Value!.ToString(), StringComparer.OrdinalIgnoreCase);
            fields.Remove("password");   // a password never comes from the AI
            return (json["message"]?.ToString() ?? "", fields);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Which record the admin wants to create, from the words of the request, or null for a normal question.
    /// It needs a verb ("create", "add", "اعمل", "ضيف" ...) AND a target ("employee", "category", "department", "موظف", "قسم" ...).
    /// The employee is checked first: "add an employee to the IT department" creates an employee.
    /// </summary>
    public static string? DetectAction(string prompt)
    {
        if (!Regex.IsMatch(prompt, @"\b(create|add|new|register|make)\b|اعمل|إعمل|انشئ|أنشئ|إنشاء|انشاء|ضيف|اضف|أضف|إضافة|اضافة|جديد|سجّل|سجل", RegexOptions.IgnoreCase))
            return null;
        if (Regex.IsMatch(prompt, @"\b(employees?|staff|users?)\b|موظف|مستخدم", RegexOptions.IgnoreCase)) return "create_employee";
        if (Regex.IsMatch(prompt, @"\b(categor(y|ies))\b|فئة|تصنيف|كاتيجوري|كاتيقوري", RegexOptions.IgnoreCase)) return "create_category";
        if (Regex.IsMatch(prompt, @"\b(departments?)\b|قسم|دائرة|إدارة|ادارة", RegexOptions.IgnoreCase)) return "create_department";
        return null;
    }
}
