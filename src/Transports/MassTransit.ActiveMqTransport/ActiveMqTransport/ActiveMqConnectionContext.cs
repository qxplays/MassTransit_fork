namespace MassTransit.ActiveMqTransport
{
    using System;
    using System.Collections.Concurrent;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Apache.NMS;
    using Apache.NMS.Util;
    using Configuration;
    using MassTransit.Middleware;
    using Transports;
    using Util;


    public class ActiveMqConnectionContext :
        BasePipeContext,
        ConnectionContext,
        IAsyncDisposable
    {
        readonly INMSContext _context;
        readonly ChannelExecutor _executor;
        readonly ConcurrentDictionary<string, IDestination> _temporaryEntities;

        /// <summary>
        /// Regular expression to distinguish if a destination is not for consuming data from a VirtualTopic. If yes we must get a standard destination because the name of
        /// the destination must match specific
        /// pattern. A temporary destination has generated name.
        /// </summary>
        /// <seealso href="https://activemq.apache.org/virtual-destinations">Virtual Destinations</seealso>
        readonly Regex _virtualTopicConsumerPattern;

        public ActiveMqConnectionContext(INMSContext context, IActiveMqHostConfiguration hostConfiguration, CancellationToken cancellationToken)
            : base(cancellationToken)
        {
            _context = context;

            Description = hostConfiguration.Settings.ToDescription();
            HostAddress = hostConfiguration.HostAddress;

            Topology = hostConfiguration.Topology;

            _executor = new ChannelExecutor(1);
            _temporaryEntities = new ConcurrentDictionary<string, IDestination>();

            _virtualTopicConsumerPattern = new Regex(hostConfiguration.Topology.PublishTopology.VirtualTopicConsumerPattern, RegexOptions.Compiled);
        }

        public INMSContext Context => _context;
        public string Description { get; }
        public Uri HostAddress { get; }
        public IActiveMqBusTopology Topology { get; }


        public bool IsVirtualTopicConsumer(string name)
        {
            return _virtualTopicConsumerPattern.IsMatch(name);
        }

        public IQueue GetTemporaryQueue(string topicName)
        {
            return (IQueue)_temporaryEntities.GetOrAdd(topicName, x => (IQueue)GetDestination(topicName, DestinationType.TemporaryQueue));
        }

        public ITopic GetTemporaryTopic(string topicName)
        {
            return (ITopic)_temporaryEntities.GetOrAdd(topicName, x => (ITopic)GetDestination(topicName, DestinationType.TemporaryTopic));
        }

        public bool TryGetTemporaryEntity(string name, out IDestination destination)
        {
            return _temporaryEntities.TryGetValue(name, out destination);
        }

        public bool TryRemoveTemporaryEntity(string name)
        {
            if (_temporaryEntities.TryGetValue(name, out var destination))
            {
                //TODO: fix somehow
                //session.DeleteDestination(destination);
                return true;
            }

            return false;
        }

        public async Task<IDestination> GetDestinationAsync(string destinationName, DestinationType destinationType)
        {
            switch (destinationType)
            {
                case DestinationType.Queue:
                case DestinationType.TemporaryQueue:
                    return await _context.GetQueueAsync(destinationName);
                case DestinationType.Topic:
                case DestinationType.TemporaryTopic:
                default:
                    return await _context.GetTopicAsync(destinationName);
            }
        }

        public IDestination GetDestination(string destinationName, DestinationType destinationType)
        {
            switch (destinationType)
            {
                case DestinationType.Queue:
                case DestinationType.TemporaryQueue:
                    return _context.GetQueue(destinationName);
                case DestinationType.Topic:
                case DestinationType.TemporaryTopic:
                default:
                    return _context.GetTopic(destinationName);
            }
        }

        public async ValueTask DisposeAsync()
        {
            TransportLogMessages.DisconnectHost(Description);

            try
            {
                await _context.CloseAsync().ConfigureAwait(false);

                TransportLogMessages.DisconnectedHost(Description + " \r\nTrace: " + Environment.StackTrace);

                _context.Dispose();

                await _executor.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                LogContext.Warning?.Log(exception, "Close Connection Faulted: {Host}", Description);
            }
        }
    }
}
