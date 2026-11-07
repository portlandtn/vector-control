using System.Security.Cryptography;
using System.Text;
public static class NotificationPrivacy
{
    public const string Generic = "An update is available. Check your private dashboard.";
    public static string Speech(string? eventType) => eventType switch
    {
        "github.workflow.failed" => "A GitHub workflow failed. Check your private dashboard.",
        "github.workflow.succeeded" => "A GitHub workflow succeeded.",
        "github.pull_request.opened" => "A pull request is ready for review.",
        "github.pull_request.merged" => "A pull request was merged.",
        "github.issue.opened" => "A new GitHub issue is available.",
        "github.issue.closed" => "A GitHub issue was closed.",
        "email.priority" => "An email needs your attention. Check your private inbox.",
        "calendar.meeting_imminent" => "A calendar event is approaching. Check your private calendar.",
        _ => Generic
    };
    public static bool Sensitive(string text)
    {
        var normalized = new string(text.Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return new[]{"verificationcode","onetimecode","password","credential","accountnumber","routingnumber","socialsecurity","diagnosis","medicalrecord","loanbalance"}.Any(normalized.Contains);
    }
    private static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static NotificationEventRequest Minimize(NotificationEventRequest e)
    {
        var type = Speech(e.EventType) == Generic ? "notification.update" : e.EventType!;
        return e with {
            Source = e.Source?.ToLowerInvariant() is "github" or "agentmail" or "calendar" ? e.Source.ToLowerInvariant() : "external",
            EventType = type, Message = Speech(type), Summary = null, Metadata = null,
            DedupeKey = Digest(e.Source + "\0" + e.DedupeKey),
            StateKey = e.StateKey is null ? null : Digest(e.Source + "\0" + e.StateKey),
            State = e.State is null ? null : Digest(e.State)
        };
    }
}
