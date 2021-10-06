namespace MqttHass2InfluxDbGateway.Configuration
{
    public class InfluxDbConfiguration
    {
        public string Uri { get; set; }
        public int Port { get; set; } = 8086;
        public string Database { get; set; }
        public string User { get; set; }
        public string UserPassword { get; set; }
    }
}
