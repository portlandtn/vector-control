using System.Text.Json;
using Xunit;

public sealed class GitHubEventFactoryTests
{
    [Theory]
    [InlineData("success", "github.workflow.succeeded", "info", "celebrate")]
    [InlineData("failure", "github.workflow.failed", "warning", "annoyed")]
    public void CompletedBuildsHaveTwentyResponsesAndNoStateSuppression(
        string conclusion, string expectedType, string expectedSeverity, string expectedEmotion)
    {
        using var payload = JsonDocument.Parse(
            $"{{\"action\":\"completed\",\"repository\":{{\"name\":\"widget-factory\"}},\"workflow_run\":{{\"name\":\"CI\",\"conclusion\":\"{conclusion}\"}}}}");

        var events = Enumerable.Range(0, 20)
            .Select(index => GitHubEventFactory.Build("workflow_run", $"delivery-{index}", payload.RootElement, new IndexedRandom(index))!)
            .ToArray();

        Assert.Equal(20, events.Select(e => e.Message).Distinct().Count());
        Assert.All(events, e =>
        {
            Assert.Equal(expectedType, e.EventType);
            Assert.Equal(expectedSeverity, e.Severity);
            Assert.Equal(expectedEmotion, e.Emotion);
            Assert.Contains("widget-factory", e.Message);
            Assert.Null(e.StateKey);
            Assert.Null(e.State);
        });
    }

    [Theory]
    [MemberData(nameof(OtherGitHubEvents))]
    public void EveryOtherHandledEventHasAtLeastFiveResponses(string eventName, string json, string expectedType)
    {
        using var payload = JsonDocument.Parse(json);
        var events = Enumerable.Range(0, 5)
            .Select(index => GitHubEventFactory.Build(eventName, $"delivery-{index}", payload.RootElement, new IndexedRandom(index))!)
            .ToArray();

        Assert.All(events, e => Assert.Equal(expectedType, e.EventType));
        Assert.Equal(5, events.Select(e => e.Message).Distinct().Count());
    }

    public static TheoryData<string, string, string> OtherGitHubEvents => new()
    {
        { "ping", RepoOnly, "github.connected" },
        { "push", RepoOnly, "github.push" },
        { "pull_request", WithAction("opened", "\"pull_request\":{\"number\":42,\"merged\":false}"), "github.pull_request.opened" },
        { "pull_request", WithAction("closed", "\"pull_request\":{\"number\":42,\"merged\":true}"), "github.pull_request.merged" },
        { "pull_request_review", WithAction("submitted", "\"pull_request\":{\"number\":42},\"review\":{\"state\":\"approved\"}"), "github.pull_request.approved" },
        { "pull_request_review", WithAction("submitted", "\"pull_request\":{\"number\":42},\"review\":{\"state\":\"changes_requested\"}"), "github.pull_request.changes_requested" },
        { "issues", WithAction("opened", "\"issue\":{\"number\":42}"), "github.issue.opened" },
        { "issues", WithAction("assigned", "\"issue\":{\"number\":42}"), "github.issue.assigned" },
        { "issues", WithAction("closed", "\"issue\":{\"number\":42}"), "github.issue.closed" },
        { "workflow_run", WithAction("completed", "\"workflow_run\":{\"name\":\"CI\",\"conclusion\":\"cancelled\"}"), "github.workflow.cancelled" },
        { "deployment", WithAction("created", "\"deployment\":{}"), "github.deployment.started" },
        { "deployment_status", WithAction("created", "\"deployment_status\":{\"state\":\"success\"}"), "github.deployment.succeeded" },
        { "deployment_status", WithAction("created", "\"deployment_status\":{\"state\":\"failure\"}"), "github.deployment.failed" },
        { "repository_vulnerability_alert", RepoOnly, "github.security_alert" }
    };

    private const string RepoOnly = "{\"repository\":{\"name\":\"widget-factory\"}}";
    private static string WithAction(string action, string body) =>
        $"{{\"action\":\"{action}\",\"repository\":{{\"name\":\"widget-factory\"}},{body}}}";

    private sealed class IndexedRandom(int index) : Random
    {
        public override int Next(int maxValue) => index % maxValue;
    }
}
