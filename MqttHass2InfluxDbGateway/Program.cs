using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MqttHass2InfluxDbGateway.Configuration;
using MQTTnet;
using MQTTnet.Extensions.ManagedClient;

namespace MqttHass2InfluxDbGateway
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSystemd()
                .ConfigureServices((hostContext, services) =>
                {
                    services.Configure<MqttConfiguration>(hostContext.Configuration.GetSection("MqttConfiguration"));
                    services.Configure<InfluxDbConfiguration>(hostContext.Configuration.GetSection("InfluxDbConfiguration"));
                    services.Configure<ProgramConfiguration>(hostContext.Configuration.GetSection("ProgramConfiguration"));
                    services.AddSingleton(typeof(IDbStorage), typeof(InfluxDbStorage));
                    services.AddSingleton(e => new MqttFactory().CreateManagedMqttClient());
                    services.AddSingleton(typeof(IMqttReceiver), typeof(MqttReceiver));
                    services.AddHostedService<WorkerMqttListener>();
                });
    }
}
