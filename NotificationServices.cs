using System.Diagnostics;
using Microsoft.Extensions.Options;

public sealed class EventProcessor(EventStore store, IOptions<NotifierOptions> options, TimeProvider clock, ILogger<EventProcessor> logger)
{
    private static readonly HashSet<string> Severities = ["info", "warning", "critical"];
    private static readonly HashSet<string> Emotions = ["quiet","attention","curious","excited","celebrate","annoyed","urgent","relieved","judgmental"];
    private readonly NotifierOptions _options = options.Value;

    public IReadOnlyList<string> Validate(NotificationEventRequest e)
    {
        var errors = new List<string>();
        Required(e.Source,"source"); Required(e.EventType,"eventType"); Required(e.Severity,"severity"); Required(e.Emotion,"emotion"); Required(e.Message,"message"); Required(e.DedupeKey,"dedupeKey");
        if (e.Severity is not null && !Severities.Contains(e.Severity.ToLowerInvariant())) errors.Add("severity must be info, warning, or critical.");
        if (e.Emotion is not null && !Emotions.Contains(e.Emotion.ToLowerInvariant())) errors.Add("emotion is not supported.");
        if (e.Message?.Length > 500) errors.Add("message must be 500 characters or fewer.");
        if ((e.StateKey is null) != (e.State is null)) errors.Add("stateKey and state must be supplied together.");
        return errors;
        void Required(string? value,string name) { if(string.IsNullOrWhiteSpace(value)) errors.Add($"{name} is required."); }
    }

    public async Task<EventDecision> ProcessAsync(NotificationEventRequest e, CancellationToken ct)
    {
        var now=clock.GetUtcNow();
        if (e.ExpiresAt <= now) return Log(new(false,"suppressed","expired"),e);
        if (e.OccurredAt > now.AddMinutes(5)) return Log(new(false,"suppressed","occurred_at_in_future"),e);
        if (_options.Events.TryGetValue(e.EventType!,out var enabled) && !enabled) return Log(new(false,"suppressed","event_disabled"),e);
        if (e.EventType == "package.shipped" && !_options.PackageShipmentAnnouncementsEnabled) return Log(new(false,"suppressed","event_disabled"),e);
        if (e.EventType == "calendar.meeting_imminent" && !_options.MeetingImminentRemindersEnabled) return Log(new(false,"suppressed","event_disabled"),e);
        if (e.Source!.Equals("agentmail",StringComparison.OrdinalIgnoreCase) && ContainsSensitiveContent(e.Message!)) return Log(new(false,"suppressed","sensitive_content"),e);
        var quiet=IsQuiet(now) && !(e.Severity!.Equals("critical",StringComparison.OrdinalIgnoreCase)&&_options.CriticalBypassesQuietHours);
        if (quiet && !_options.QuietHoursBehavior.Equals("queue",StringComparison.OrdinalIgnoreCase)) return Log(new(false,"suppressed","quiet_hours"),e);
        var result=await store.AddAsync(e,quiet?"queued_quiet":"pending",ct); return Log(result,e);
    }

    public bool IsQuiet(DateTimeOffset now)
    { var zone=TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZone); var local=TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,zone).DateTime); var start=_options.QuietHoursStart; var end=_options.QuietHoursEnd; return start<=end ? local>=start&&local<end : local>=start||local<end; }
    private static bool ContainsSensitiveContent(string text) => new[]{"verification code","one-time code","password","credential","account number","routing number","social security","diagnosis","medical record","loan balance"}.Any(x=>text.Contains(x,StringComparison.OrdinalIgnoreCase));
    private EventDecision Log(EventDecision d,NotificationEventRequest e) { logger.LogInformation("Notification event {Source} {EventType} {Severity} {DedupeKey} emotion {Emotion}: {Outcome} {Reason}",e.Source,e.EventType,e.Severity,e.DedupeKey,e.Emotion,d.Outcome,d.Reason); return d; }
}

public sealed class VectorClient(HttpClient client, IOptions<NotifierOptions> options, ILogger<VectorClient> logger)
{
    private readonly SemaphoreSlim _activityLock = new(1,1);
    private readonly NotifierOptions _options=options.Value;
    public async Task<bool> IsReachableAsync(CancellationToken ct) { try { using var r=await client.GetAsync("/",ct); return (int)r.StatusCode<500; } catch { return false; } }
    public async Task<DeliveryResult> DeliverAsync(NotificationEnvelope e,CancellationToken ct)
    {
        await _activityLock.WaitAsync(ct);
        var serial=Uri.EscapeDataString(_options.WirePod.Serial); var acquired=false;
        try {
            using var assume=await client.PostAsync($"/api-sdk/assume_behavior_control?priority=high&serial={serial}",null,ct); assume.EnsureSuccessStatusCode(); acquired=true;
            await Task.Delay(_options.WirePod.AssumeDelayMilliseconds,ct);
            if (_options.EmotionAnimations.TryGetValue(e.Emotion,out var animation)&&!string.IsNullOrWhiteSpace(animation))
            {
                try { using var animated=await client.PostAsync($"/api-sdk/play_animation?animation={Uri.EscapeDataString(animation)}&serial={serial}",null,ct); if(!animated.IsSuccessStatusCode) logger.LogWarning("Animation unavailable for emotion {Emotion}; using speech fallback",e.Emotion); }
                catch(HttpRequestException) { logger.LogWarning("Animation unavailable for emotion {Emotion}; using speech fallback",e.Emotion); }
            }
            using var speech=await client.PostAsync($"/api-sdk/say_text?text={Uri.EscapeDataString(e.Message)}&serial={serial}",null,ct); speech.EnsureSuccessStatusCode();
            await Task.Delay(_options.WirePod.SpeechDelayMilliseconds,ct); return new(true);
        } catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException) { logger.LogWarning("Vector delivery unavailable for {Source} {EventType}",e.Source,e.EventType); return new(false,ex.GetType().Name); }
        finally { if(acquired) try { using var _=await client.PostAsync($"/api-sdk/release_behavior_control?serial={serial}",null,CancellationToken.None); } catch { } _activityLock.Release(); }
    }
}

public sealed class NotificationWorker(EventStore store,VectorClient vector,EventProcessor processor,ILogger<NotificationWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while(!ct.IsCancellationRequested) {
            try { var item=await store.ClaimNextAsync(!processor.IsQuiet(TimeProvider.System.GetUtcNow()),ct); if(item is null) { await Task.Delay(500,ct); continue; } var sw=Stopwatch.StartNew(); var result=await vector.DeliverAsync(item,ct); await store.CompleteAsync(item.Id,result,ct); logger.LogInformation("Notification {Source} {EventType} {DedupeKey} delivery {Outcome} in {ElapsedMs}ms",item.Source,item.EventType,item.DedupeKey,result.Success?"delivered":"queued_vector_unavailable",sw.ElapsedMilliseconds); if(!result.Success) await Task.Delay(5000,ct); }
            catch(OperationCanceledException) when(ct.IsCancellationRequested) { break; } catch(Exception ex) { logger.LogError(ex,"Notification worker iteration failed"); await Task.Delay(1000,ct); }
        }
    }
}

public sealed class CleanupWorker(EventStore store):BackgroundService
{ protected override async Task ExecuteAsync(CancellationToken ct) { while(!ct.IsCancellationRequested) { await store.CleanupAsync(ct); await Task.Delay(TimeSpan.FromHours(1),ct); } } }
