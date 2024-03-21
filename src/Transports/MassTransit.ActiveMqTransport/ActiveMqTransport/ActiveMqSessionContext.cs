namespace MassTransit.ActiveMqTransport
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Apache.NMS;
    using Apache.NMS.Util;
    using Internals;
    using MassTransit.Middleware;
    using Topology;
    using Transports;
    using Util;


    public class ActiveMqSessionContext :
        ScopePipeContext,
        SessionContext,
        IAsyncDisposable
    {
        readonly ChannelExecutor _executor;
        readonly MessageProducerCache _messageProducerCache;

        public ActiveMqSessionContext(ConnectionContext connectionContext, CancellationToken cancellationToken)
            : base(connectionContext)
        {
            ConnectionContext = connectionContext;
            CancellationToken = cancellationToken;

            _executor = new ChannelExecutor(1);

            _messageProducerCache = new MessageProducerCache();
        }

        public async ValueTask DisposeAsync()
        {

            try
            {
                await _messageProducerCache.Stop(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogContext.Warning?.Log(ex, "Close session faulted: {Host}", ConnectionContext.Description);
            }



            await _executor.DisposeAsync().ConfigureAwait(false);
        }

        public override CancellationToken CancellationToken { get; }


        public ConnectionContext ConnectionContext { get; }

        public Task<ITopic> GetTopic(Topic topic)
        {
            return _executor.Run(async () =>
            {
                if (!topic.Durable && topic.AutoDelete
                    && topic.EntityName.StartsWith(ConnectionContext.Topology.PublishTopology.VirtualTopicPrefix, StringComparison.InvariantCulture))
                    return ConnectionContext.GetTemporaryTopic(topic.EntityName);

                return await ConnectionContext.Context.GetTopicAsync(topic.EntityName);
            }, CancellationToken);
        }

        public Task<IQueue> GetQueue(Queue queue)
        {
            return _executor.Run(() =>
            {
                if (!queue.Durable && queue.AutoDelete && !ConnectionContext.IsVirtualTopicConsumer(queue.EntityName))
                    return ConnectionContext.GetTemporaryQueue(queue.EntityName);

                return ConnectionContext.Context.GetQueue(queue.EntityName);
            }, CancellationToken);
        }

        public Task<IDestination> GetDestination(string destinationName, DestinationType destinationType)
        {
            if ((destinationType == DestinationType.Queue || destinationType == DestinationType.TemporaryQueue)
                && ConnectionContext.TryGetTemporaryEntity(destinationName, out var destination))
                return Task.FromResult(destination);

            return _executor.Run(() => ConnectionContext.GetDestination(destinationName, destinationType), CancellationToken);
        }

        public Task<INMSConsumer> CreateMessageConsumer(IDestination destination, string selector, bool noLocal)
        {
            return _executor.Run(() => ConnectionContext.Context.CreateConsumerAsync(destination, selector, noLocal), CancellationToken);
        }

        public async Task SendAsync(IDestination destination, IMessage message, CancellationToken cancellationToken)
        {
            var producer = await _messageProducerCache.GetMessageProducer(destination,
                x => _executor.Run(() => ConnectionContext.Context.CreateProducerAsync(), cancellationToken)).ConfigureAwait(false);
            await _executor.Run(() => producer.SendAsync(destination, message)
                .OrCanceled(cancellationToken), cancellationToken).ConfigureAwait(false);
        }

        public IBytesMessage CreateBytesMessage(byte[] content)
        {
            return ConnectionContext.Context.CreateBytesMessage(content);
        }

        public ITextMessage CreateTextMessage(string content)
        {
            return ConnectionContext.Context.CreateTextMessage(content);
        }

        public IMessage CreateMessage()
        {
            return ConnectionContext.Context.CreateMessage();
        }

        public Task DeleteTopic(string topicName)
        {
            TransportLogMessages.DeleteTopic(topicName);

            return _executor.Run(() =>
            {
                //TODO: Fix this somehow...
                //if (!ConnectionContext.TryRemoveTemporaryEntity(topicName))
                //    SessionUtil.DeleteTopic(topicName);
            }, CancellationToken.None);
        }

        public Task DeleteQueue(string queueName)
        {
            TransportLogMessages.DeleteQueue(queueName);

            return _executor.Run(() =>
                {
                    //TODO: Fix this somehow...
                    //if (!ConnectionContext.TryRemoveTemporaryEntity(queueName))
                    //    SessionUtil.DeleteQueue(queueName);
                }
                , CancellationToken.None);
        }

        public IDestination GetTemporaryDestination(string name)
        {
            return ConnectionContext.TryGetTemporaryEntity(name, out var destination) ? destination : null;
        }
    }
}
