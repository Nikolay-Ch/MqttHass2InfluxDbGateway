using DeviceId;
using DeviceId.Encoders;
using DeviceId.Formatters;
using MQTTnet.Protocol;
using System.Reflection;

namespace MqttHass2InfluxDbGateway.Configuration
{
    public class MqttConfiguration
    {
        // create ClientId: GUID in windows systems, and /etc/machine-id in linux
        public string ClientId { get; set; } = new DeviceIdBuilder()
            .AddOSInstallationID()
            .UseFormatter(new StringDeviceIdFormatter(new PlainTextDeviceIdComponentEncoder()))
            .ToString() + "-" + Assembly.GetExecutingAssembly().GetName();

        public string MqttUri { get; set; }
        public string MqttUser { get; set; }
        public string MqttUserPassword { get; set; }
        public int MqttPort { get; set; } = 1883;
        public bool MqttSecure { get; set; } = false;
        public MqttQualityOfServiceLevel MqttQosLevel { get; set; } = MqttQualityOfServiceLevel.AtMostOnce;
        public string SensorDataTopic { get; set; }
        public string ConfigurationTopic { get; set; }
    }
}
