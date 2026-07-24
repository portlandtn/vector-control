using System.Text.Json;
using Microsoft.Extensions.Options;

public sealed class PlayModeCoordinator(
    EventStore store,
    IOptions<NotifierOptions> options,
    TimeProvider clock,
    ILogger<PlayModeCoordinator> logger)
{
    private const string StateKey = "robot_mode";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PlayModeOptions _options = options.Value.PlayMode;
    private RobotModeStatus _status = WorkStatus(DateTimeOffset.MinValue);

    public bool IsPlayMode => Volatile.Read(ref _status).IsPlayMode;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var json = await store.GetRuntimeStateAsync(StateKey, ct);
            var restored = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<RobotModeStatus>(json);
            _status = restored is { IsPlayMode: true, ExpiresAt: not null } && restored.ExpiresAt > clock.GetUtcNow()
                ? restored
                : WorkStatus(clock.GetUtcNow());
            await PersistAsync(ct);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Invalid persisted robot mode; resetting to work mode");
            _status = WorkStatus(clock.GetUtcNow());
            await PersistAsync(ct);
        }
        finally { _gate.Release(); }
    }

    public async Task<RobotModeStatus> GetStatusAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { await ExpireIfNeededAsync(ct); return _status; }
        finally { _gate.Release(); }
    }

    public async Task<ModeChangeResult> EnterPlayModeAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await ExpireIfNeededAsync(ct);
            if (!_options.Enabled) return new(_status, false);
            if (_status.IsPlayMode) return new(_status, false);

            var now = clock.GetUtcNow();
            _status = new("play", Guid.NewGuid().ToString("N"), now, now.AddMinutes(_options.SessionMinutes));
            await PersistAsync(ct);
            logger.LogInformation("Robot entered Izzy play mode until {ExpiresAt}", _status.ExpiresAt);
            return new(_status, true);
        }
        finally { _gate.Release(); }
    }

    public async Task<ModeChangeResult> EnterWorkModeAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var changed = _status.IsPlayMode;
            _status = WorkStatus(clock.GetUtcNow());
            await PersistAsync(ct);
            if (changed) logger.LogInformation("Robot returned to work mode");
            return new(_status, changed);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> RunNotificationIterationAsync(Func<Task> iteration, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await ExpireIfNeededAsync(ct);
            if (_status.IsPlayMode) return false;
            await iteration();
            return true;
        }
        finally { _gate.Release(); }
    }

    private async Task ExpireIfNeededAsync(CancellationToken ct)
    {
        if (!_status.IsPlayMode || _status.ExpiresAt > clock.GetUtcNow()) return;
        _status = WorkStatus(clock.GetUtcNow());
        await PersistAsync(ct);
        logger.LogInformation("Izzy play mode session expired; robot returned to work mode");
    }

    private Task PersistAsync(CancellationToken ct) =>
        store.SetRuntimeStateAsync(StateKey, JsonSerializer.Serialize(_status), ct);

    private static RobotModeStatus WorkStatus(DateTimeOffset now) => new("work", null, now, null);
}

public sealed class PlayModeWorker(
    PlayModeCoordinator modes,
    VectorClient vector,
    IOptions<NotifierOptions> options,
    TimeProvider clock,
    ILogger<PlayModeWorker> logger) : BackgroundService
{
    private readonly PlayModeOptions _options = options.Value.PlayMode;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        string? sessionId = null;
        var nextPromptAt = DateTimeOffset.MaxValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var status = await modes.GetStatusAsync(ct);
                if (!status.IsPlayMode)
                {
                    sessionId = null;
                    nextPromptAt = DateTimeOffset.MaxValue;
                }
                else if (status.SessionId != sessionId)
                {
                    sessionId = status.SessionId;
                    nextPromptAt = clock.GetUtcNow().AddSeconds(_options.PromptIntervalSeconds);
                }
                else if (clock.GetUtcNow() >= nextPromptAt)
                {
                    await SpeakAsync(Random.Shared.GetItems(_options.Prompts.ToArray(), 1)[0], ct);
                    nextPromptAt = clock.GetUtcNow().AddSeconds(_options.PromptIntervalSeconds);
                }
                await Task.Delay(500, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Play mode worker iteration failed");
                await Task.Delay(1000, ct);
            }
        }
    }

    private async Task SpeakAsync(string phrase, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return;
        var envelope = new NotificationEnvelope(0, "play-mode", "play.prompt", "info", "curious", phrase, $"play-{Guid.NewGuid():N}", null, null, clock.GetUtcNow());
        await vector.DeliverAsync(envelope, ct);
    }
}
