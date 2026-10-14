namespace MassTransit
{
    using System.Collections.Generic;


    public interface IActiveMqHostConfigurator
    {
        /// <summary>
        /// Sets the username for the connection to ActiveMQ
        /// </summary>
        /// <param name="username"></param>
        void Username(string username);

        /// <summary>
        /// Sets the password for the connection to ActiveMQ
        /// </summary>
        /// <param name="password"></param>
        void Password(string password);

        void UseSsl(bool enabled = true);

        /// <summary>
        /// Sets a list of hosts to enable the failover transport
        /// </summary>
        /// <param name="hosts"></param>
        void FailoverHosts(string[] hosts);

        /// <summary>
        /// Sets options on the underlying NMS transport
        /// </summary>
        /// <param name="options"></param>
        void TransportOptions(IEnumerable<KeyValuePair<string, string>> options);

        /// <summary>
        /// </summary>
        void EnableOptimizeAcknowledge();

        /// <summary>
        /// Opt into independent physical publisher connections (1..8). Default 1
        /// retains the existing connection. Consumer ACKs and temporary destinations
        /// remain on their original connection. Experimental; validate recovery first.
        /// </summary>
        void PublisherConnectionCount(int count);

        void SetPrefetchPolicy(int limit);

        void SetQueuePrefetchPolicy(int limit);

        /// <summary>
        /// Previous versions has nms.AsyncSend enabled by default. This can result in message loss,
        /// so now it's disabled by default. It can be enabled using this method, or by adding <see cref="TransportOptions"/>.
        /// </summary>
        void EnableAsyncSend();
    }
}
