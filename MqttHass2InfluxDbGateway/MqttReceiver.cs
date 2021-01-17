using HassSensorConfiguration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MqttHass2InfluxDbGateway.Configuration;
using MQTTnet;
using MQTTnet.Client.Connecting;
using MQTTnet.Client.Disconnecting;
using MQTTnet.Client.Options;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MqttHass2InfluxDbGateway
{
    public interface IMqttReceiver
    {
        Task Start();
        Task Stop();
    }

    public class MqttReceiver : IMqttReceiver
    {
        protected ILogger<WorkerMqttListener> Logger { get; }
        protected IManagedMqttClient MqttClient { get; }
        protected IDbStorage Storage { get; }

        protected MqttConfiguration MqttConfiguration { get; }

        private readonly object SensorsLockObject = new object();
        protected List<Sensor> Sensors { get; } = new List<Sensor>();

        public MqttReceiver(
            ILogger<WorkerMqttListener> logger,
            IManagedMqttClient mqttClient,
            IDbStorage storage,
            IOptions<MqttConfiguration> mqttConfiguration)
        {
            Logger = logger;
            MqttClient = mqttClient;
            Storage = storage;

            MqttConfiguration = mqttConfiguration.Value;
        }

        public async Task Start()
        {
            MqttClient.UseApplicationMessageReceivedHandler(async e => await MessageReceive(e));
            MqttClient.UseConnectedHandler(async e => await MqttConnected(e));
            MqttClient.UseDisconnectedHandler(async e => await MqttDisconnected(e));

            await MqttClientConnect();
        }

        public async Task Stop()
        {
            //MqttClient.UseDisconnectedHandler(async e => await Task.CompletedTask);

            Logger.LogInformation($"{typeof(MqttReceiver).Name} - Stopping connection to Mqtt at: {DateTimeOffset.Now}");

            await MqttClient.StopAsync();

            Logger.LogInformation($"{typeof(MqttReceiver).Name} - Stopping connection to Mqtt done at: {DateTimeOffset.Now}");
        }

        private async Task MqttConnected(MqttClientConnectedEventArgs e)
        {
            Logger.LogInformation($"{typeof(MqttReceiver).Name} - connection to Mqtt established at: {DateTimeOffset.Now}. Authenticate result: {e.AuthenticateResult.ResultCode}");

            await MqttClient.SubscribeAsync(MqttConfiguration.ConfigurationTopic, MqttConfiguration.MqttQosLevel);

            Logger.LogInformation($"{typeof(WorkerMqttListener).Name} Subscribe to Configuration topic: {MqttConfiguration.ConfigurationTopic} at: {DateTimeOffset.Now}");

            await MqttClient.SubscribeAsync(MqttConfiguration.SensorDataTopic, MqttQualityOfServiceLevel.ExactlyOnce);

            Logger.LogInformation($"{typeof(WorkerMqttListener).Name} Subscribe to Sensor data topic: {MqttConfiguration.SensorDataTopic} at: {DateTimeOffset.Now}");
        }

        private async Task MqttDisconnected(MqttClientDisconnectedEventArgs e)
        {
            Logger.LogWarning($"{typeof(MqttReceiver).Name} - connection to Mqtt closed at: {DateTimeOffset.Now}. Reason: {e.Reason}");

            if (e.Exception != null)
                Logger.LogError(e.Exception, "Mqtt disconnecter error at {time}", DateTimeOffset.Now);

            await Task.CompletedTask;
        }

        private async Task MqttClientConnect()
        {
            var messageBuilder = new MqttClientOptionsBuilder()
                .WithClientId(MqttConfiguration.ClientId.Replace("-", ""))
                .WithCredentials(MqttConfiguration.MqttUser, MqttConfiguration.MqttUserPassword)
                .WithTcpServer(MqttConfiguration.MqttUri, MqttConfiguration.MqttPort)
                .WithCleanSession();

            var options = MqttConfiguration.MqttSecure ?
                messageBuilder.WithTls().Build() :
                messageBuilder.Build();

            var managedOptions = new ManagedMqttClientOptionsBuilder()
                .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
                .WithClientOptions(options)
                .Build();

            await MqttClient.StartAsync(managedOptions);

            // wait for connection
            while (!MqttClient.IsConnected)
                Thread.Sleep(1000);
        }

        private async Task MessageReceive(MqttApplicationMessageReceivedEventArgs e)
        {
            if (e.ApplicationMessage.Topic.LikeMqttHassTopic(MqttConfiguration.ConfigurationTopic))
                await ProcessConfigurationMessage(e);

            if (e.ApplicationMessage.Topic.LikeMqttHassTopic(MqttConfiguration.SensorDataTopic))
                await ProcessSensorDataMessage(e);
        }

        private async Task ProcessConfigurationMessage(MqttApplicationMessageReceivedEventArgs msg)
        {
            var sensor = JsonConvert.DeserializeObject<Sensor>(msg.ApplicationMessage.ConvertPayloadToString());

            if (sensor.Device != null)
            {
                lock (SensorsLockObject)
                {
                    // if we have this sensor - first remove it from list, and then renew it
                    var oldSensor = Sensors.FirstOrDefault(e => e.UniqueId == sensor.UniqueId);
                    if (oldSensor != null)
                        Sensors.Remove(oldSensor);

                    Sensors.Add(sensor);
                }
                Logger.LogInformation("Receive Config message for sensor {sensor} at: {time}", sensor.UniqueId, DateTimeOffset.Now);
            }

            await Task.CompletedTask;
        }

        private async Task ProcessSensorDataMessage(MqttApplicationMessageReceivedEventArgs msg)
        {
            try
            {
                Sensor[] sensors = null;
                lock (SensorsLockObject)
                    sensors = Sensors.Where(e => msg.ApplicationMessage.Topic.LikeMqttHassTopic(e.StatTopic)).ToArray();

                if (!sensors.Any())
                {
                    Logger.LogWarning("Receive Sensor Data message from unknown sensor: {topic} at: {time}", msg.ApplicationMessage.Topic, DateTimeOffset.Now);
                    return;
                }

                var sensorData = (JObject)JsonConvert.DeserializeObject(msg.ApplicationMessage.ConvertPayloadToString());
                var sensorId = sensorData.Value<string>("id");

                Logger.LogInformation("Receive Sensor Data message for sensor {sensor} at: {time}", sensorId, DateTimeOffset.Now);

                var fields = new Dictionary<string, object>();
                foreach (var sensor in sensors)
                {
                    var valName = Regex.Match(sensor.ValueJScript, @"^\{\{ value_json.(\w*)|").Groups[1].ToString();
                    var valStorageName = Storage.PrepareValueName(valName);

                    if (!string.IsNullOrEmpty(valStorageName) && sensorData.ContainsKey(valName))
                    {
                        var value = sensorData.Value<double>(valName);
                        fields.Add(valStorageName, value);
                    }
                }

                if (fields.Count != 0)
                    await Storage.StoreToDatabase(sensorId, fields);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error in ProcessSensorDataMessage");
            }
        }

    }
}
