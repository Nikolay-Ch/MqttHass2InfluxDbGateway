using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MqttHass2InfluxDbGateway
{
    public class WorkerMqttListener : BackgroundService
    {
        protected ILogger<WorkerMqttListener> Logger { get; }
        protected IMqttReceiver MqttReceiver { get; }

        public WorkerMqttListener(ILogger<WorkerMqttListener> logger, IMqttReceiver mqttReceiver)
        {
            Logger = logger;
            MqttReceiver = mqttReceiver;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                Logger.LogInformation($"{typeof(WorkerMqttListener).Name} running at: {DateTimeOffset.Now}");

                await MqttReceiver.Start();

                while (!stoppingToken.IsCancellationRequested)
                    await Task.Delay(1000, stoppingToken);

                await MqttReceiver.Stop();

                Logger.LogInformation($"{typeof(WorkerMqttListener).Name} stopping at: {DateTimeOffset.Now}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"{typeof(WorkerMqttListener).Name} error at {DateTimeOffset.Now}");
            }
        }
    }
}
