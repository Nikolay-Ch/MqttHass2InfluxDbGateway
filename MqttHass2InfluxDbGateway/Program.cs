using HassMqttIntegration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MqttHass2InfluxDbGateway.Configuration;
using Syslog.Framework.Logging;

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
                .ConfigureAppConfiguration((ctx, cfg) =>
                {
                    var env = ctx.HostingEnvironment;

                    cfg.AddJsonFile("/config/appsettings.json", true, false)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", true, false)
                        .AddEnvironmentVariables();
                })
                .ConfigureLogging((ctx, logging) =>
                {
                    var slConfig = ctx.Configuration.GetSection("SyslogSettings");
                    if (slConfig != null)
                    {
                        var settings = new SyslogLoggerSettings();
                        slConfig.Bind(settings);

                        // Configure structured data here if desired.
                        logging.AddSyslog(settings);
                    }
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.Configure<MqttConfiguration>(hostContext.Configuration.GetSection("MqttConfiguration"));
                    services.Configure<InfluxDbConfiguration>(hostContext.Configuration.GetSection("InfluxDbConfiguration"));
                    services.Configure<ProgramConfiguration>(hostContext.Configuration.GetSection("ProgramConfiguration"));
                    services.AddSingleton(typeof(IDbStorage), typeof(InfluxDbStorage));
                    services.AddSingleton(typeof(IMqttClientForMultipleSubscribers), typeof(MqttClientForMultipleSubscribers));
                    services.AddHostedService<WorkerMqttListener>();
                });
    }
}
