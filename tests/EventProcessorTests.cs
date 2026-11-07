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

    [Theory]
    [InlineData("other-producer","verification-code: 123456")]
    [InlineData("AGENTMAIL","p\u200bassword: synthetic")]
    [InlineData("github","MEDICAL record: synthetic")]
    public async Task SensitiveTextSuppressedAcrossProducers(string source,string message)
    { Assert.Equal("sensitive_content",(await _processor.ProcessAsync(Event("synthetic") with {Source=source,Message=message},default)).Reason); }
    [Fact] public async Task UntrustedFieldsNeverReachStoredEnvelope()
    {
        var e=Event("private-key-fixture") with {Source="private-source-fixture",EventType="private-type-fixture",Message="private-message-fixture",Summary="private-summary-fixture",StateKey="private-state-key",State="private-state",Metadata=new(){{"sensitive",System.Text.Json.JsonSerializer.SerializeToElement("private-metadata-fixture")}}};
        Assert.True((await _processor.ProcessAsync(e,default)).Accepted);
        var saved=await _store.ClaimNextAsync(true,default);Assert.NotNull(saved);
        Assert.Equal(NotificationPrivacy.Generic,saved.Message);Assert.Equal("external",saved.Source);Assert.Equal("notification.update",saved.EventType);
        foreach(var canary in new[]{"private-key-fixture","private-source-fixture","private-type-fixture","private-message-fixture","private-summary-fixture","private-state-key","private-state","private-metadata-fixture"}) Assert.DoesNotContain(canary,System.Text.Json.JsonSerializer.Serialize(saved));
    }
    private static NotificationEventRequest Event(string key)=>new("test","test.event","warning","attention","Safe notification",null,key,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddHours(1),null,null,null);
}
