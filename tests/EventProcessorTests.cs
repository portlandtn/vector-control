using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class EventProcessorTests : IAsyncLifetime
{
    private readonly string _db=Path.Combine(Path.GetTempPath(),$"vector-tests-{Guid.NewGuid():N}.db");
    private readonly NotifierOptions _options=new() { TimeZone="America/Chicago",QuietHoursStart=new(0,0),QuietHoursEnd=new(0,0) };
    private EventStore _store=null!; private EventProcessor _processor=null!;

    public async ValueTask InitializeAsync()
    { Environment.SetEnvironmentVariable("VECTOR_NOTIFIER_DB",_db); var wrapped=Options.Create(_options); _store=new(wrapped,TimeProvider.System,NullLogger<EventStore>.Instance); await _store.InitializeAsync(); _processor=new(_store,wrapped,TimeProvider.System,NullLogger<EventProcessor>.Instance); }
    public ValueTask DisposeAsync() { Environment.SetEnvironmentVariable("VECTOR_NOTIFIER_DB",null); try{File.Delete(_db);}catch{} return ValueTask.CompletedTask; }

    [Fact] public void ValidEventPassesValidation() => Assert.Empty(_processor.Validate(Event("valid")));
    [Fact] public void InvalidPayloadIsRejected() => Assert.NotEmpty(_processor.Validate(new(null,null,"loud","happy",null,null,null,null,null,null,null,null)));
    [Fact] public async Task DuplicateIsSuppressed() { Assert.True((await _processor.ProcessAsync(Event("same"),default)).Accepted); Assert.Equal("duplicate",(await _processor.ProcessAsync(Event("same"),default)).Reason); }
    [Fact] public async Task ExpiredEventIsSuppressed() { var e=Event("expired") with { ExpiresAt=DateTimeOffset.UtcNow.AddSeconds(-1) }; Assert.Equal("expired",(await _processor.ProcessAsync(e,default)).Reason); }
    [Fact] public async Task UnchangedStateIsSuppressed() { var e=Event("one") with { StateKey="svc",State="unhealthy" }; Assert.True((await _processor.ProcessAsync(e,default)).Accepted); Assert.Equal("unchanged_state",(await _processor.ProcessAsync(e with { DedupeKey="two" },default)).Reason); }
    [Fact] public async Task RecoveryIsAccepted() { await _processor.ProcessAsync(Event("down") with { StateKey="svc",State="unhealthy" },default); Assert.True((await _processor.ProcessAsync(Event("up") with { StateKey="svc",State="healthy" },default)).Accepted); }
    [Fact] public async Task SensitiveAgentMailIsSuppressed() { var e=Event("mail") with { Source="agentmail",EventType="email.priority",Message="Your verification code is 123456" }; Assert.Equal("sensitive_content",(await _processor.ProcessAsync(e,default)).Reason); }
    [Fact] public async Task PersistenceSurvivesStoreRestart() { await _processor.ProcessAsync(Event("persist"),default); var wrapped=Options.Create(_options); var restarted=new EventStore(wrapped,TimeProvider.System,NullLogger<EventStore>.Instance); await restarted.InitializeAsync(); var processor=new EventProcessor(restarted,wrapped,TimeProvider.System,NullLogger<EventProcessor>.Instance); Assert.Equal("duplicate",(await processor.ProcessAsync(Event("persist"),default)).Reason); }
    [Fact] public async Task QuietHoursQueuesNoncriticalAndBypassesCritical() { _options.QuietHoursStart=new(0,0); _options.QuietHoursEnd=new(23,59); Assert.Equal("quiet_hours",(await _processor.ProcessAsync(Event("quiet"),default)).Reason); Assert.Null((await _processor.ProcessAsync(Event("critical") with { Severity="critical" },default)).Reason); }

    [Fact]
    public async Task PlayModePersistsAndPausesNotificationIterations()
    {
        var wrapped=Options.Create(_options);
        var modes=new PlayModeCoordinator(_store,wrapped,TimeProvider.System,NullLogger<PlayModeCoordinator>.Instance);
        await modes.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.True((await modes.EnterPlayModeAsync(TestContext.Current.CancellationToken)).Changed);

        var ran=false;
        Assert.False(await modes.RunNotificationIterationAsync(() => { ran=true; return Task.CompletedTask; },TestContext.Current.CancellationToken));
        Assert.False(ran);

        var restarted=new PlayModeCoordinator(_store,wrapped,TimeProvider.System,NullLogger<PlayModeCoordinator>.Instance);
        await restarted.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.True(restarted.IsPlayMode);
        Assert.True((await restarted.EnterWorkModeAsync(TestContext.Current.CancellationToken)).Changed);
        Assert.True(await restarted.RunNotificationIterationAsync(() => { ran=true; return Task.CompletedTask; },TestContext.Current.CancellationToken));
        Assert.True(ran);
    }

    [Fact]
    public async Task PlayModeExpiresBackToWorkMode()
    {
        _options.PlayMode.SessionMinutes=1;
        var clock=new ManualTimeProvider(DateTimeOffset.Parse("2026-07-24T12:00:00Z"));
        var modes=new PlayModeCoordinator(_store,Options.Create(_options),clock,NullLogger<PlayModeCoordinator>.Instance);
        await modes.InitializeAsync(TestContext.Current.CancellationToken);
        await modes.EnterPlayModeAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal("work",(await modes.GetStatusAsync(TestContext.Current.CancellationToken)).Mode);
    }

    private static NotificationEventRequest Event(string key)=>new("test","test.event","warning","attention","Safe notification",null,key,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddHours(1),null,null,null);

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now=now;
        public override DateTimeOffset GetUtcNow()=>_now;
        public void Advance(TimeSpan duration)=>_now+=duration;
    }
}
