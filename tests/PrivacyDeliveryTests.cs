using System.Net;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
public class PrivacyDeliveryTests
{
 [Fact] public async Task LegacyQueuedTextIsNeverSentToSpeech()
 {
  var handler=new Capture();var opts=new NotifierOptions();opts.WirePod.AssumeDelayMilliseconds=0;opts.WirePod.SpeechDelayMilliseconds=0;opts.WirePod.Serial="synthetic-device";
  var client=new VectorClient(new HttpClient(handler){BaseAddress=new Uri("http://synthetic.invalid")},Options.Create(opts),NullLogger<VectorClient>.Instance);
  var result=await client.DeliverAsync(new(1,"arbitrary","unknown","info","quiet","private-canary-123456","dedupe",null,null,DateTimeOffset.UtcNow),default);
  Assert.True(result.Success);Assert.DoesNotContain(handler.Paths,x=>x.Contains("private-canary"));
  Assert.Contains(handler.Paths,x=>Uri.UnescapeDataString(x).Contains(NotificationPrivacy.Generic));
 }
 sealed class Capture:HttpMessageHandler
 {
  public List<string> Paths {get;}=[];
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){Paths.Add(r.RequestUri!.ToString());return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));}
 }
}
