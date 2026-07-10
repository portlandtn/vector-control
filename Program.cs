using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("wirepod", client =>
{
    client.BaseAddress = new Uri("http://192.168.50.205:8080");
});

var app = builder.Build();

var expectedApiKey =
    Environment.GetEnvironmentVariable("VECTOR_NOTIFIER_API_KEY");

var githubWebhookSecret =
    Environment.GetEnvironmentVariable("GITHUB_WEBHOOK_SECRET");

if (string.IsNullOrWhiteSpace(expectedApiKey))
{
    throw new InvalidOperationException(
        "VECTOR_NOTIFIER_API_KEY is not configured.");
}

if (string.IsNullOrWhiteSpace(githubWebhookSecret))
{
    throw new InvalidOperationException(
        "GITHUB_WEBHOOK_SECRET is not configured.");
}

app.MapPost("/vector/say", async (
    HttpRequest httpRequest,
    SayRequest request,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    if (!httpRequest.Headers.TryGetValue("X-Api-Key", out var providedApiKey) ||
        providedApiKey != expectedApiKey)
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest(new
        {
            error = "Text is required."
        });
    }

    var result = await SpeakAsync(
        request.Text,
        httpClientFactory,
        cancellationToken);

    return result.Success
        ? Results.Ok(new { success = true, spoken = request.Text })
        : Results.Problem(
            title: "Vector communication failed",
            detail: result.Error,
            statusCode: StatusCodes.Status502BadGateway);
});

app.MapPost("/github/webhook", async (
    HttpRequest request,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    request.EnableBuffering();

    using var reader = new StreamReader(
        request.Body,
        Encoding.UTF8,
        detectEncodingFromByteOrderMarks: false,
        leaveOpen: true);

    var payload = await reader.ReadToEndAsync(cancellationToken);
    request.Body.Position = 0;

    if (!request.Headers.TryGetValue(
            "X-Hub-Signature-256",
            out var signatureHeader))
    {
        return Results.Unauthorized();
    }

    var expectedSignature = CreateGitHubSignature(
        payload,
        githubWebhookSecret);

    if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(signatureHeader.ToString()),
            Encoding.UTF8.GetBytes(expectedSignature)))
    {
        return Results.Unauthorized();
    }

    var eventName =
        request.Headers["X-GitHub-Event"].ToString();

    using var document = JsonDocument.Parse(payload);

    var message = BuildGitHubMessage(
        eventName,
        document.RootElement);

    if (message is null)
    {
        return Results.Ok(new
        {
            success = true,
            ignored = true,
            eventName
        });
    }

    var result = await SpeakAsync(
        message,
        httpClientFactory,
        cancellationToken);

    return result.Success
        ? Results.Ok(new
        {
            success = true,
            eventName,
            spoken = message
        })
        : Results.Problem(
            title: "Vector communication failed",
            detail: result.Error,
            statusCode: StatusCodes.Status502BadGateway);
});

app.Run();

static string CreateGitHubSignature(
    string payload,
    string secret)
{
    var keyBytes = Encoding.UTF8.GetBytes(secret);
    var payloadBytes = Encoding.UTF8.GetBytes(payload);

    using var hmac = new HMACSHA256(keyBytes);
    var hash = hmac.ComputeHash(payloadBytes);

    return $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
}

static string? BuildGitHubMessage(
    string eventName,
    JsonElement payload)
{
    var repository = payload
        .GetProperty("repository")
        .GetProperty("name")
        .GetString() ?? "a repository";

    return eventName switch
    {
        "ping" =>
            $"Jed, GitHub is connected to Vector for {repository}.",

        "push" =>
            $"Jed, new code was pushed to {repository}.",

        "pull_request" =>
            BuildPullRequestMessage(payload, repository),

        "issues" =>
            BuildIssueMessage(payload, repository),

        "workflow_run" =>
            BuildWorkflowMessage(payload, repository),

        _ => null
    };
}

static string? BuildPullRequestMessage(
    JsonElement payload,
    string repository)
{
    var action = payload.GetProperty("action").GetString();
    var pullRequest = payload.GetProperty("pull_request");
    var number = pullRequest.GetProperty("number").GetInt32();

    if (action == "closed" &&
        pullRequest.GetProperty("merged").GetBoolean())
    {
        return $"Jed, pull request {number} was merged in {repository}.";
    }

    if (action == "opened")
    {
        return $"Jed, pull request {number} was opened in {repository}.";
    }

    return null;
}

static string? BuildIssueMessage(
    JsonElement payload,
    string repository)
{
    var action = payload.GetProperty("action").GetString();
    var issue = payload.GetProperty("issue");
    var number = issue.GetProperty("number").GetInt32();

    return action switch
    {
        "opened" =>
            $"Jed, issue {number} was created in {repository}.",

        "assigned" =>
            $"Jed, issue {number} was assigned in {repository}.",

        "closed" =>
            $"Jed, issue {number} was closed in {repository}.",

        _ => null
    };
}

static string? BuildWorkflowMessage(
    JsonElement payload,
    string repository)
{
    var action = payload.GetProperty("action").GetString();

    if (action != "completed")
    {
        return null;
    }

    var workflowRun = payload.GetProperty("workflow_run");
    var workflowName =
        workflowRun.GetProperty("name").GetString()
        ?? "A GitHub workflow";

    var conclusion =
        workflowRun.GetProperty("conclusion").GetString();

    return conclusion switch
    {
        "success" =>
            $"{workflowName} succeeded in {repository}. Against all odds, the computers behaved.",

        "failure" =>
            $"Jed, {workflowName} failed in {repository}. The build server has chosen violence.",

        "cancelled" =>
            $"{workflowName} was cancelled in {repository}. Probably for the best.",

        _ => null
    };
}

static async Task<SpeakResult> SpeakAsync(
    string text,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken)
{
    var client = httpClientFactory.CreateClient("wirepod");
    const string serial = "007064c9";

    var assumeUrl =
        $"/api-sdk/assume_behavior_control?priority=high&serial={serial}";

    var speakUrl =
        $"/api-sdk/say_text?text={Uri.EscapeDataString(text)}&serial={serial}";

    var releaseUrl =
        $"/api-sdk/release_behavior_control?serial={serial}";

    try
    {
        using var assumeResponse =
            await client.PostAsync(
                assumeUrl,
                content: null,
                cancellationToken);

        assumeResponse.EnsureSuccessStatusCode();

        await Task.Delay(2000, cancellationToken);

        using var speakResponse =
            await client.PostAsync(
                speakUrl,
                content: null,
                cancellationToken);

        speakResponse.EnsureSuccessStatusCode();

        await Task.Delay(5000, cancellationToken);

        using var releaseResponse =
            await client.PostAsync(
               releaseUrl,
                content: null,
                cancellationToken);

        //releaseResponse.EnsureSuccessStatusCode();

        return new SpeakResult(true, null);
    }
    catch (HttpRequestException ex)
    {
        return new SpeakResult(false, ex.Message);
    }
}

public sealed record SayRequest(string Text);

public sealed record SpeakResult(
    bool Success,
    string? Error);
