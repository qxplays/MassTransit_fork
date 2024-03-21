namespace MassTransit
{
    using Metadata;


    public class ActiveMqTransportOptions
    {
        public ActiveMqTransportOptions()
        {
            Host = HostMetadataCache.IsRunningInContainer ? "activemq" : "tower";
            Port = 61616;
            User = "admin";
            Pass = "admin";
        }

        public string Host { get; set; }
        public ushort Port { get; set; }
        public bool UseSsl { get; set; }
        public string User { get; set; }
        public string Pass { get; set; }
    }
}
