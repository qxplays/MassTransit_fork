namespace MassTransit.ActiveMqTransport.Middleware
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Apache.NMS;
    using Apache.NMS.ActiveMQ;
    using MassTransit.Middleware;
    using Topology;
    using Transports;
    using Util;


    /// <summary>
    /// A filter that uses the model context to create a basic consumer and connect it to the model
    /// </summary>
    public class ActiveMqConsumerFilter :
        IFilter<SessionContext>
    {
        readonly ActiveMqReceiveEndpointContext _context;

        public ActiveMqConsumerFilter(ActiveMqReceiveEndpointContext context)
        {
            _context = context;
        }

        void IProbeSite.Probe(ProbeContext context)
        {
        }

        string GetReceiveEntityName(ReceiveSettings settings, string entityName = null)
        {
            return settings.AutoDelete
                ? entityName ?? settings.EntityName
                : $"{entityName ?? settings.EntityName}?consumer.prefetchSize={settings.PrefetchCount}";
        }

        async Task IFilter<SessionContext>.Send(SessionContext context, IPipe<SessionContext> next)
        {
            var receiveSettings = context.GetPayload<ReceiveSettings>();

            var executor = new ChannelExecutor(receiveSettings.PrefetchCount, receiveSettings.ConcurrentMessageLimit);

            var consumers = new List<Task<ActiveMqConsumer>>
            {
                CreateConsumer(context, new QueueEntity(0, GetReceiveEntityName(receiveSettings), receiveSettings.Durable,
                    receiveSettings.AutoDelete), receiveSettings.Selector, executor)
            };

            consumers.AddRange(_context.BrokerTopology.Consumers.Select(x =>
                CreateConsumer(context, new QueueEntity(0, GetReceiveEntityName(receiveSettings, x.Destination.EntityName), x.Destination.Durable,
                    x.Destination.AutoDelete), x.Selector, executor)));

            ActiveMqConsumer[] actualConsumers = await Task.WhenAll(consumers).ConfigureAwait(false);

            var supervisor = CreateConsumerSupervisor(context, actualConsumers);

            await supervisor.Ready.ConfigureAwait(false);

            LogContext.Debug?.Log("Consumers Ready: {InputAddress}", _context.InputAddress);

            _context.AddConsumeAgent(supervisor);

            await _context.TransportObservers.NotifyReady(_context.InputAddress).ConfigureAwait(false);

            try
            {
                await supervisor.Completed.ConfigureAwait(false);
            }
            finally
            {
                DeliveryMetrics[] consumerMetrics = actualConsumers.Cast<DeliveryMetrics>().ToArray();

                DeliveryMetrics metrics = new CombinedDeliveryMetrics(consumerMetrics.Sum(x => x.DeliveryCount),
                    consumerMetrics.Max(x => x.ConcurrentDeliveryCount));

                await _context.TransportObservers.NotifyCompleted(_context.InputAddress, metrics).ConfigureAwait(false);

                _context.LogConsumerCompleted(metrics.DeliveryCount, metrics.ConcurrentDeliveryCount);

                await executor.DisposeAsync().ConfigureAwait(false);
            }
        }

        Supervisor CreateConsumerSupervisor(SessionContext context, ActiveMqConsumer[] actualConsumers)
        {
            var supervisor = new ConsumerSupervisor(actualConsumers);

            void HandleException(Exception exception)
            {
                TransportLogMessages.ExceptionListenerHandled(exception.ToString());
                if (exception.ToString().Contains("not correlate acknowledgment"))
                    return;
                supervisor.Stop(exception.Message);
            }

            context.ConnectionContext.Context.ExceptionListener += HandleException;

            supervisor.SetReady();

            supervisor.Completed.ContinueWith(task => context.ConnectionContext.Context.ExceptionListener -= HandleException,
                TaskContinuationOptions.ExecuteSynchronously);

            return supervisor;
        }

        async Task<ActiveMqConsumer> CreateConsumer(SessionContext context, Queue entity, string selector,
            ChannelExecutor executor)
        {
            var queue = await context.GetQueue(entity).ConfigureAwait(false);

            var messageConsumer = await context.CreateMessageConsumer(queue, selector, false).ConfigureAwait(false);

            LogContext.Debug?.Log("Created consumer for {InputAddress}: {Queue}", _context.InputAddress, entity.EntityName);

            var consumer = new ActiveMqConsumer(context, messageConsumer, _context, executor);

            return consumer;
        }


        class ConsumerSupervisor :
            Supervisor
        {
            public ConsumerSupervisor(ActiveMqConsumer[] consumers)
            {
                foreach (var consumer in consumers)
                {
                    consumer.Completed.ContinueWith(async _ =>
                    {
                        try
                        {
                            if (!IsStopping)
                                await this.Stop("Consumer stopped, stopping supervisor").ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            LogContext.Warning?.Log(exception, "Stop Faulted");
                        }
                    }, TaskContinuationOptions.RunContinuationsAsynchronously);

                    Add(consumer);
                }
            }
        }


        class CombinedDeliveryMetrics :
            DeliveryMetrics
        {
            public CombinedDeliveryMetrics(long deliveryCount, int concurrentDeliveryCount)
            {
                DeliveryCount = deliveryCount;
                ConcurrentDeliveryCount = concurrentDeliveryCount;
            }

            public long DeliveryCount { get; }
            public int ConcurrentDeliveryCount { get; }
        }
    }
}
