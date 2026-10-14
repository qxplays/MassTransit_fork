namespace MassTransit.ActiveMqTransport
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Apache.NMS;
    using Apache.NMS.ActiveMQ.Commands;
    using MassTransit.Util;

    // Experimental host-wide publisher pool. Consumer connections are unchanged.
    // Each lane is FIFO and has its own physical connection/session/producer.
    internal sealed class NativeSendLanePool : IAsyncDisposable
    {
        readonly Lane[] _lanes;
        int _next;
        public event Action<Exception> ExceptionListener;
        public NativeSendLanePool(Func<INMSContext> factory, int count)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (count < 2 || count > 8) throw new ArgumentOutOfRangeException(nameof(count));
            _lanes=Enumerable.Range(0,count).Select(_=>new Lane(factory,e=>ExceptionListener?.Invoke(e))).ToArray();
        }
        public Task Send(IDestination destination,IMessage message,CancellationToken token)
        {
            var group=(message as Message)?.GroupID;
            var key=string.IsNullOrWhiteSpace(group)?message.NMSCorrelationID:group;
            var index=string.IsNullOrWhiteSpace(key)?(uint)Interlocked.Increment(ref _next):(uint)StringComparer.Ordinal.GetHashCode(key);
            return _lanes[index%(uint)_lanes.Length].Send(destination,message,token);
        }
        public async ValueTask DisposeAsync()
        {await Task.WhenAll(_lanes.Select(lane=>lane.DisposeAsync().AsTask())).ConfigureAwait(false);}
        sealed class Lane : IAsyncDisposable
        {
            readonly Func<INMSContext> _factory;
            readonly ChannelExecutor _executor=new ChannelExecutor(1);
            readonly Action<Exception> _onException;
            INMSContext _context;
            INMSProducer _producer;
            public Lane(Func<INMSContext> factory,Action<Exception> onException){_factory=factory;_onException=onException;}
            public Task Send(IDestination destination,IMessage message,CancellationToken token)
            {
                return _executor.Run(async()=>{
                    if(_context==null){_context=_factory();_context.ExceptionListener+=e=>_onException(e);}
                    if(_producer==null)_producer=await _context.CreateProducerAsync().ConfigureAwait(false);
                    // Set per-message mode in the same serialized operation as send.
                    // TTL, priority, IDs, headers and payload are already on the message.
                    _producer.SetDeliveryMode(message.NMSDeliveryMode);
                    token.ThrowIfCancellationRequested();
                    await _producer.SendAsync(destination,message).ConfigureAwait(false);
                },token);
            }
            public async ValueTask DisposeAsync()
            {
                try{await _executor.DisposeAsync().ConfigureAwait(false);}
                finally{if(_context!=null)await _context.CloseAsync().ConfigureAwait(false);}
            }
        }
    }
}
