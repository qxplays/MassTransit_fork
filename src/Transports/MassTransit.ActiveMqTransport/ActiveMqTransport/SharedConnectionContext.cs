namespace MassTransit.ActiveMqTransport
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Apache.NMS;
    using MassTransit.Middleware;


    public class SharedConnectionContext :
        ProxyPipeContext,
        ConnectionContext
    {
        readonly ConnectionContext _context;

        public SharedConnectionContext(ConnectionContext context, CancellationToken cancellationToken)
            : base(context)
        {
            _context = context;
            CancellationToken = cancellationToken;
        }

        public override CancellationToken CancellationToken { get; }

        INMSContext ConnectionContext.Context => _context.Context;
        public string Description => _context.Description;
        public Uri HostAddress => _context.HostAddress;
        public IActiveMqBusTopology Topology => _context.Topology;


        public bool IsVirtualTopicConsumer(string name)
        {
            return _context.IsVirtualTopicConsumer(name);
        }

        public IQueue GetTemporaryQueue(string topicName)
        {
            return _context.GetTemporaryQueue(topicName);
        }

        public ITopic GetTemporaryTopic(string topicName)
        {
            return _context.GetTemporaryTopic( topicName);
        }

        public bool TryGetTemporaryEntity(string name, out IDestination destination)
        {
            return _context.TryGetTemporaryEntity(name, out destination);
        }

        public bool TryRemoveTemporaryEntity(string name)
        {
            return _context.TryRemoveTemporaryEntity(name);
        }

        public Task<IDestination> GetDestinationAsync(string destinationName, DestinationType destinationType)
        {
            return _context.GetDestinationAsync(destinationName, destinationType);
        }

        public IDestination GetDestination(string destinationName, DestinationType destinationType)
        {
            return _context.GetDestination(destinationName, destinationType);
        }
    }
}
