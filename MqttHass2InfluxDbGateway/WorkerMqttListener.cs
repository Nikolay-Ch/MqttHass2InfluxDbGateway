using HassDeviceBaseWorkers;
using HassMqttIntegration;
using HassSensorConfiguration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
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
    public class WorkerMqttListener : DeviceBaseWorker<WorkerMqttListener>
    {
        protected IDbStorage Storage { get; init; }
        protected List<IHassComponent> SubscriberComponentList { get; } = new List<IHassComponent>();

        protected BinarySensor InnerPresenseSensor => (BinarySensor)ComponentList[0];

        public WorkerMqttListener(
            ILogger<WorkerMqttListener> logger,
            IOptions<WorkersConfiguration> workersConfiguration,
            IOptions<MqttConfiguration> mqttConfiguration,
            IMqttClientForMultipleSubscribers mqttClient,
            IDbStorage storage) :
            base("MqttHass2InfluxDbGateway", logger, workersConfiguration, mqttConfiguration, mqttClient)
        {
            Storage = storage;


            var binarySensorFactory = new BinarySensorFactory();
            Device device = new()
            {
                Name = DeviceId,
                Model = "V1",
                Identifiers = new() { DeviceId },
                Connections = HassComponentExtensions.AllComponentAddresses
            };

            ComponentList.Add(binarySensorFactory.CreateBinarySensor(
                DeviceClassDescription.None,
                device: device,
                sensorName: $"{DeviceId}-Presence",
                stateTopic: $"home/{DeviceId}/Service",
                uniqueId: $"{DeviceId}",
                payloadOn: "Started",
                payloadOff: "Stopped"
                ));
        }

        protected override async Task PostSendConfigurationAsync(CancellationToken stoppingToken)
        {
            await base.PostSendConfigurationAsync(stoppingToken);

            // send presense On message
            await MqttClient.PublishAsync(InnerPresenseSensor.StateTopic, InnerPresenseSensor.PayloadOn, MqttConfiguration.MqttQosLevel, true);

            // subscribe to configuration messages from HASS-component
            await MqttClient.SubscribeAsync(this,
                string.Format(MqttConfiguration.ConfigurationTopic, "+", "+"),
                MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);
        }

        protected override async Task BeforeExitAsync()
        {
            // send presense Off message
            await MqttClient.PublishAsync(InnerPresenseSensor.StateTopic, InnerPresenseSensor.PayloadOff, MqttConfiguration.MqttQosLevel, false);
        }

        public override async Task MqttReceiveHandler(MqttApplicationMessageReceivedEventArgs e)
        {
            if (e.ApplicationMessage.Topic.LikeMqttHassTopic(string.Format(MqttConfiguration.ConfigurationTopic, "+", "+")))
                await ProcessConfigurationMessage(e);
            else
                await ProcessDataMessage(e);
        }

        private readonly object ComponentLockObject = new();

        private async Task ProcessConfigurationMessage(MqttApplicationMessageReceivedEventArgs msg)
        {
            var component = (IHassComponent)JsonConvert.DeserializeObject(
                msg.ApplicationMessage.ConvertPayloadToString().ChangeJObjectPropertyNames(),
                msg.ApplicationMessage.Topic.GetComponentTypeFromConfigurationTopic().ComponentType());

            if (component.Device != null && component.StateTopic != null)
            {
                lock (ComponentLockObject)
                {
                    // if we have this component - first remove it from list, and then renew it
                    var oldComponent = SubscriberComponentList.FirstOrDefault(e => e.UniqueId == component.UniqueId);
                    if (oldComponent != null)
                        SubscriberComponentList.Remove(oldComponent);

                    SubscriberComponentList.Add(component);

                    // subscribe to component-data
                    if (oldComponent == null || oldComponent.StateTopic != component.StateTopic)
                        MqttClient.SubscribeAsync(this, component.StateTopic, MqttConfiguration.MqttQosLevel);
                }
                Logger.LogInformation("Receive Config message for sensor {sensor} at: {time}", component.UniqueId, DateTimeOffset.Now);
            }

            await Task.CompletedTask;
        }

        private async Task ProcessDataMessage(MqttApplicationMessageReceivedEventArgs msg)
        {
            try
            {
                IHassComponent[] components = null;
                lock (ComponentLockObject)
                    components = SubscriberComponentList
                        .Where(e => msg.ApplicationMessage.Topic.LikeMqttHassTopic(e.StateTopic))
                        .ToArray();

                if (!components.Any())
                {
                    Logger.LogWarning("Receive component data message from unknown component: {topic} at: {time}", msg.ApplicationMessage.Topic, DateTimeOffset.Now);
                    return;
                }

                Logger.LogInformation("Receive component data message '{component}' at: {time}", msg.ApplicationMessage.Topic, DateTimeOffset.Now);
                Logger.LogTrace(msg.ApplicationMessage.ConvertPayloadToString());

                var fields = new Dictionary<string, object>();

                var componentType = components[0].GetConfigTopic().Split('/')[1];
                var componentId = components[0].UniqueId;
                switch (componentType)
                {
                    case "sensor":
                    {
                        var componentData = (JObject)JsonConvert.DeserializeObject(msg.ApplicationMessage.ConvertPayloadToString());
                        componentId = componentData.Value<string>("id");

                        Logger.LogTrace("This is sensors. ComponentId: {componentId}", componentId);
                        Logger.LogTrace($"Sensors: {string.Join(", ", components.Select(e=>e.Name))}");

                        foreach (Sensor component in components.Where(e => e is Sensor))
                        {
                            var valName = Regex.Match(component.ValueTemplate, @"^\{\{ value_json.(\w*)|").Groups[1].ToString();
                            var valStorageName = Storage.PrepareValueName(valName);

                            Logger.LogTrace($"{nameof(valName)}:{valName} - {nameof(valStorageName)}:{valStorageName}");

                            if (!string.IsNullOrEmpty(valStorageName) && componentData.ContainsKey(valName))
                            {
                                var value = componentData.Value<double>(valName);

                                Logger.LogTrace($"{nameof(value)}:{value}");

                                if (!fields.ContainsKey(valStorageName))
                                    fields.Add(valStorageName, value);

                                Logger.LogTrace($"{string.Join(", ", fields.Select(e => e.Key))}");
                            }
                        }
                    }; break;
                    case "binary_sensor":
                    {
                        var componentData = msg.ApplicationMessage.ConvertPayloadToString();

                        Logger.LogTrace("This id binary_sensor. Message: {message}", componentData);

                        fields.Add("status", componentData.ToString());
                        fields.Add("binary_status", componentData.ToString() == (components[0] as BinarySensor)?.PayloadOn);

                    }; break;
                }

                if (fields.Count != 0)
#if DEBUG
                    await Task.CompletedTask;
#else
                    await Storage.StoreToDatabase(componentId, fields);
#endif
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error in ProcessSensorDataMessage");
            }
        }
    }
}
