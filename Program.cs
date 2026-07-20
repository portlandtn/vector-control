using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
builder.Services.Configure<NotifierOptions>(builder.Configuration.GetSection(NotifierOptions.Section));
builder.Services.PostConfigure<NotifierOptions>(o =>
{
    o.ApiKey = Environment.GetEnvironmentVariable("VECTOR_NOTIFIER_API_KEY") ?? o.ApiKey;
    o.WirePod.BaseUrl = Environment.GetEnvironmentVariable("WIREPOD_BASE_URL") ?? o.WirePod.BaseUrl;
    o.WirePod.Serial = Environment.GetEnvironmentVariable("VECTOR_SERIAL") ?? o.WirePod.Serial;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<EventStore>();
builder.Services.AddSingleton<EventProcessor>();
builder.Services.AddHttpClient<VectorClient>((sp,client) => client.BaseAddress=new Uri(sp.GetRequiredService<IOptions<NotifierOptions>>().Value.WirePod.BaseUrl));
builder.Services.AddHostedService<NotificationWorker>();
builder.Services.AddHostedService<CleanupWorker>();

var app=builder.Build();
var options=app.Services.GetRequiredService<IOptions<NotifierOptions>>().Value;
if(string.IsNullOrWhiteSpace(options.ApiKey)) throw new InvalidOperationException("VECTOR_NOTIFIER_API_KEY or Notifier:ApiKey is not configured.");
var githubSecret=Environment.GetEnvironmentVariable("GITHUB_WEBHOOK_SECRET") ?? builder.Configuration["GitHub:WebhookSecret"];
if(string.IsNullOrWhiteSpace(githubSecret)) throw new InvalidOperationException("GITHUB_WEBHOOK_SECRET or GitHub:WebhookSecret is not configured.");
await app.Services.GetRequiredService<EventStore>().InitializeAsync();

bool Authorized(HttpRequest r) => r.Headers.TryGetValue("X-Api-Key",out var supplied) && FixedEquals(supplied.ToString(),options.ApiKey);

app.MapPost("/vector/events",async (HttpRequest http,NotificationEventRequest request,EventProcessor processor,CancellationToken ct) =>
{
    if(!Authorized(http)) return Results.Unauthorized();
    var errors=processor.Validate(request); if(errors.Count>0) return Results.BadRequest(new { error="Invalid event.", errors });
    var decision=await processor.ProcessAsync(request,ct);
    return decision.Accepted ? Results.Accepted(value:new { success=true,outcome=decision.Outcome,reason=decision.Reason,eventId=decision.EventId }) : Results.Ok(new { success=true,outcome=decision.Outcome,reason=decision.Reason });
});

app.MapPost("/vector/say",async (HttpRequest http,SayRequest request,VectorClient vector,CancellationToken ct) =>
{
    if(!Authorized(http)) return Results.Unauthorized();
    if(string.IsNullOrWhiteSpace(request.Text)) return Results.BadRequest(new { error="Text is required." });
    var envelope=new NotificationEnvelope(0,"manual","vector.say","info","quiet",request.Text,"manual",null,null,DateTimeOffset.UtcNow);
    var result=await vector.DeliverAsync(envelope,ct);
    return result.Success ? Results.Ok(new { success=true,spoken=request.Text }) : Results.Problem(title:"Vector communication failed",detail:"Vector is currently unavailable.",statusCode:502);
});

app.MapPost("/github/webhook",async (HttpRequest request,EventProcessor processor,CancellationToken ct) =>
{
    request.EnableBuffering(); using var reader=new StreamReader(request.Body,Encoding.UTF8,false,leaveOpen:true); var payload=await reader.ReadToEndAsync(ct); request.Body.Position=0;
    if(!request.Headers.TryGetValue("X-Hub-Signature-256",out var signature)||!VerifyGitHubSignature(payload,signature.ToString(),githubSecret)) return Results.Unauthorized();
    var eventName=request.Headers["X-GitHub-Event"].ToString(); var delivery=request.Headers["X-GitHub-Delivery"].ToString();
    try {
        using var document=JsonDocument.Parse(payload); var normalized=GitHubEventFactory.Build(eventName,delivery,document.RootElement);
        if(normalized is null) return Results.Ok(new { success=true,ignored=true,eventName });
        var decision=await processor.ProcessAsync(normalized,ct);
        return Results.Accepted(value:new { success=true,eventName,outcome=decision.Outcome,reason=decision.Reason });
    } catch(JsonException) { return Results.BadRequest(new { error="Invalid JSON payload." }); }
});

app.MapGet("/health/live",()=>Results.Ok(new { status="live" }));
app.MapGet("/health/ready",async (EventStore store,VectorClient vector,CancellationToken ct) =>
{ var storage=await store.PingAsync(ct); var reachable=await vector.IsReachableAsync(ct); return storage&&reachable ? Results.Ok(new { status="ready",api=true,storage=true,vector=true }) : Results.Json(new { status="degraded",api=true,storage,vector=reachable },statusCode:503); });

app.Run();

static bool FixedEquals(string supplied,string expected)
{ var a=Encoding.UTF8.GetBytes(supplied); var b=Encoding.UTF8.GetBytes(expected); return a.Length==b.Length&&CryptographicOperations.FixedTimeEquals(a,b); }
static bool VerifyGitHubSignature(string payload,string supplied,string secret)
{ using var hmac=new HMACSHA256(Encoding.UTF8.GetBytes(secret)); var expected=$"sha256={Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant()}"; return FixedEquals(supplied,expected); }

public partial class Program { }
