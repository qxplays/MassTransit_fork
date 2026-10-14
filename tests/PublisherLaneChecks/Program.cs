using MassTransit.ActiveMqTransport;
using System.Diagnostics;
using System.Text.Json;
using Apache.NMS;
using Apache.NMS.ActiveMQ.Commands;
using System.Reflection;

var groups=new Dictionary<string,List<int>>();int connections=0,closes=0,activeSends=0,peakSends=0;
var destination=new ActiveMQQueue("owned-test-destination");
// Cover every lane deterministically despite randomized process string hashing.
var groupKeys = Enumerable.Range(0, 16).Select(i => Enumerable.Range(0, 10000)
    .Select(n => "saga-" + i + "-" + n)
    .First(key => (uint)StringComparer.Ordinal.GetHashCode(key) % 4 == (uint)i % 4)).ToArray();
INMSContext CreateContext(){
 Interlocked.Increment(ref connections);var producer=DispatchProxy.Create<INMSProducer,NmsTestProxy>();int active=0;MsgDeliveryMode mode=default;
 async Task<INMSProducer> Send(IMessage message){
  if(Interlocked.Increment(ref active)!=1)throw new Exception("Lane overlapped producer operations");
  var count=Interlocked.Increment(ref activeSends);lock(groups)peakSends=Math.Max(peakSends,count);
  try{
   await Task.Yield();var text=((ITextMessage)message).Text;if(text=="fault")throw new InvalidOperationException("intentional send failure");
   var i=int.Parse(text);var expected=i%2==0?MsgDeliveryMode.Persistent:MsgDeliveryMode.NonPersistent;
   if(mode!=expected||message.NMSDeliveryMode!=expected||message.NMSTimeToLive!=TimeSpan.FromSeconds(6)||message.NMSPriority!=(MsgPriority)7||message.Properties.GetString("preserve")!="original")throw new Exception("Message metadata or mode changed");
   lock(groups){if(!groups.TryGetValue(message.NMSCorrelationID,out var list))groups[message.NMSCorrelationID]=list=new();list.Add(i);}
   return producer;
  }finally{Interlocked.Decrement(ref active);Interlocked.Decrement(ref activeSends);}
 }
 ((NmsTestProxy)(object)producer).Handler=(method,args)=>method.Name switch {
  "SetDeliveryMode"=>SetMode((MsgDeliveryMode)args[0]),
  "SendAsync"=>Send((IMessage)args[1]),
  _=>throw new Exception("Unexpected producer method "+method.Name)
 };
 object SetMode(MsgDeliveryMode value){mode=value;return producer;}
 var context=DispatchProxy.Create<INMSContext,NmsTestProxy>();
 ((NmsTestProxy)(object)context).Handler=(method,args)=>method.Name switch {
  "CreateProducerAsync"=>Task.FromResult(producer),
  "CloseAsync"=>Close(),
  "add_ExceptionListener"=>null,
  "remove_ExceptionListener"=>null,
  _=>throw new Exception("Unexpected context method "+method.Name)
 };
 Task Close(){Interlocked.Increment(ref closes);return Task.CompletedTask;}
 return context;
}
var pool=new NativeSendLanePool(CreateContext,4);var sendTasks=new List<Task>();
for(var i=0;i<1024;i++){
 var message=new ActiveMQTextMessage{Text=i.ToString(),NMSCorrelationID=groupKeys[i%16],NMSDeliveryMode=i%2==0?MsgDeliveryMode.Persistent:MsgDeliveryMode.NonPersistent,NMSTimeToLive=TimeSpan.FromSeconds(6),NMSPriority=(MsgPriority)7};
 message.Properties["preserve"]="original";sendTasks.Add(pool.Send(destination,message,CancellationToken.None));
}
var failed=pool.Send(destination,new ActiveMQTextMessage{Text="fault",NMSCorrelationID=groupKeys[0]},CancellationToken.None);
using(var canceled=new CancellationTokenSource()){canceled.Cancel();var task=pool.Send(destination,new ActiveMQTextMessage{Text="canceled"},canceled.Token);try{await task;throw new Exception("Expected cancellation");}catch(OperationCanceledException){}}
await pool.DisposeAsync();await Task.WhenAll(sendTasks);try{await failed;throw new Exception("Expected failure");}catch(InvalidOperationException){}
if(connections!=4||closes!=4||peakSends>4||activeSends!=0||groups.Count!=16||groups.Any(p=>!p.Value.SequenceEqual(Enumerable.Range(0,1024).Where(i=>groupKeys[i%16]==p.Key))))throw new Exception("Publisher FIFO, drain, routing or context ownership failed");
Console.WriteLine(JsonSerializer.Serialize(new{kind="publisher-lanes",connections,closes,peakSends,sent=sendTasks.Count,perSagaFifo=true,deliveryModeAndMetadataPreserved=true}));

var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var inFlight = 0;
var sendCalls = 0;
var cancellationProducer = DispatchProxy.Create<INMSProducer, NmsTestProxy>();
async Task<INMSProducer> ControlledSend()
{
    if (Interlocked.Increment(ref inFlight) != 1) throw new Exception("Cancellation overlapped sends");
    try
    {
        if (Interlocked.Increment(ref sendCalls) == 1)
        {
            started.SetResult();
            await finish.Task;
        }
        return cancellationProducer;
    }
    finally { Interlocked.Decrement(ref inFlight); }
}
((NmsTestProxy)(object)cancellationProducer).Handler = (method, args) => method.Name switch
{
    "SetDeliveryMode" => cancellationProducer,
    "SendAsync" => ControlledSend(),
    _ => throw new Exception("Unexpected producer method " + method.Name)
};
var cancellationContext = DispatchProxy.Create<INMSContext, NmsTestProxy>();
((NmsTestProxy)(object)cancellationContext).Handler = (method, args) => method.Name switch
{
    "CreateProducerAsync" => Task.FromResult(cancellationProducer),
    "CloseAsync" => Task.CompletedTask,
    "add_ExceptionListener" => null,
    _ => throw new Exception("Unexpected context method " + method.Name)
};
await using (var cancellationPool = new NativeSendLanePool(() => cancellationContext, 2))
using (var cancelInFlight = new CancellationTokenSource())
{
    var first = cancellationPool.Send(destination, new ActiveMQTextMessage { NMSCorrelationID = "same" }, cancelInFlight.Token);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    cancelInFlight.Cancel();
    var second = cancellationPool.Send(destination, new ActiveMQTextMessage { NMSCorrelationID = "same" }, CancellationToken.None);
    await Task.Delay(50);
    if (sendCalls != 1) throw new Exception("Next send started before the canceled native send completed");
    finish.SetResult();
    try { await first; } catch (OperationCanceledException) { }
    await second;
}
if (sendCalls != 2 || inFlight != 0) throw new Exception("Cancellation lost a send");
Console.WriteLine("In-flight cancellation preserves producer exclusion: PASS");

public class NmsTestProxy:DispatchProxy{
 public Func<MethodInfo,object[],object> Handler{get;set;}
 protected override object Invoke(MethodInfo method,object[] args)=>Handler(method,args);
}
