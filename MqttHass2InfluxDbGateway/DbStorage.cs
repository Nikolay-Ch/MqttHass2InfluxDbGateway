using InfluxDB.Collector;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MqttHass2InfluxDbGateway.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MqttHass2InfluxDbGateway
{
    public interface IDbStorage
    {
        Task StoreToDatabase(string deviceId, Dictionary<string, object> values);
        string PrepareValueName(string valueName);
    }

    public class InfluxDbStorage : IDbStorage
    {
        protected ILogger<WorkerMqttListener> Logger { get; }

        protected MetricsCollector MetricsCollector { get; }
        protected Dictionary<string, string> ValueNamesMapping { get; } = new Dictionary<string, string>
        {
            { "temp", "Temperature" },
            { "tempc", "Temperature" },
            { "lux", "Light" },
            { "batt", "BatteryLevel" },
            { "moi", "SoilMoisture" },
            { "fer", "SoilFertility" },
            { "hum", "AirHumidity" },
            { "press", "Pressure" }
        };

        public InfluxDbStorage(
            ILogger<WorkerMqttListener> logger,
            IOptions<InfluxDbConfiguration> configuration)
        {
            Logger = logger;

            var dbConfiguration = configuration.Value;

            MetricsCollector = new CollectorConfiguration()
                        .WriteTo.InfluxDB($"{dbConfiguration.Uri}:{dbConfiguration.Port}", dbConfiguration.Database, dbConfiguration.User, dbConfiguration.UserPassword)
                        .CreateCollector();
        }

        public async Task StoreToDatabase(string deviceId, Dictionary<string, object> values)
        {
            try
            {
                MetricsCollector.Write(
                    "DeviceData",
                    values,
                    new Dictionary<string, string> { { "Id", deviceId } },
                    DateTime.Now.ToUniversalTime());

                Logger.LogInformation("Store to database values: {values} for device {device} at: {time}", String.Join(',', values.Keys), deviceId, DateTimeOffset.Now);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error in StoreToDatabase");
            }

            await Task.CompletedTask;
        }

        public string PrepareValueName(string valueName) =>
            ValueNamesMapping.ContainsKey(valueName) ? ValueNamesMapping[valueName] : null;
    }
}
