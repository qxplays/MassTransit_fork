namespace MassTransit.ActiveMqTransport
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Apache.NMS;


    public interface ConnectionContext :
        PipeContext
    {
        /// <summary>
        /// The ActiveMQ Connection
        /// </summary>
        INMSContext Context { get; }

        /// <summary>
        /// The connection description, useful to debug output
        /// </summary>
        string Description { get; }

        /// <summary>
        /// The Host Address for this connection
        /// </summary>
        Uri HostAddress { get; }

        IActiveMqBusTopology Topology { get; }


        bool IsVirtualTopicConsumer(string name);

        IQueue GetTemporaryQueue(string topicName);

        ITopic GetTemporaryTopic(string topicName);

        bool TryGetTemporaryEntity(string name, out IDestination destination);

        bool TryRemoveTemporaryEntity(string name);
        Task<IDestination> GetDestinationAsync(string destinationName, DestinationType destinationType);
        IDestination GetDestination(string destinationName, DestinationType destinationType);
    }
}
