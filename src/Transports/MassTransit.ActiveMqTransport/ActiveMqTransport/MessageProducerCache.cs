namespace MassTransit.ActiveMqTransport
{
    using System.Threading.Tasks;
    using Apache.NMS;
    using Internals.Caching;
    using MassTransit.Middleware;
    using Transports;


    public class MessageProducerCache :
        Agent
    {
        public delegate Task<INMSProducer> MessageProducerFactory(IDestination destination);


        readonly ICache<IDestination, INMSProducer, ITimeToLiveCacheValue<INMSProducer>> _cache;

        public MessageProducerCache()
        {
            var options = new CacheOptions { Capacity = SendEndpointCacheDefaults.Capacity };
            var policy = new TimeToLiveCachePolicy<INMSProducer>(SendEndpointCacheDefaults.MaxAge);

            _cache = new MassTransitCache<IDestination, INMSProducer, ITimeToLiveCacheValue<INMSProducer>>(policy, options);
        }

        public async Task<INMSProducer> GetMessageProducer(IDestination key, MessageProducerFactory factory)
        {
            var messageProducer = await _cache.GetOrAdd(key, x => GetMessageProducerFromFactory(x, factory)).ConfigureAwait(false);

            return messageProducer;
        }

        static async Task<INMSProducer> GetMessageProducerFromFactory(IDestination destination, MessageProducerFactory factory)
        {
            return await factory(destination).ConfigureAwait(false);
        }

        protected override Task StopAgent(StopContext context)
        {
            return _cache.Clear();
        }
    }
}
