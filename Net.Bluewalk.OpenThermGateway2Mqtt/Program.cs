using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace Net.Bluewalk.OpenThermGateway2Mqtt
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            var version = FileVersionInfo.GetVersionInfo(typeof(Program).Assembly.Location).ProductVersion;
            Console.WriteLine($"OpenThermGateway2MQTT version {version}");
            Console.WriteLine("https://github.com/bluewalk/OpenThermGateway2MQTT\n");


            #region Configuration
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json",
                    true, true)
                .AddEnvironmentVariables("OTGW_")
                .Build();
            #endregion


            #region Serilog
            var outputTemplate =
                "{Timestamp:yyyy-MM-dd HH:mm:ss zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.ControlledBy(new EnvironmentVariableLoggingLevelSwitch("LOG_LEVEL"))
                .WriteTo.Debug(outputTemplate: outputTemplate)
                .WriteTo.Console(outputTemplate: outputTemplate, theme: AnsiConsoleTheme.Code)
                .CreateLogger();
            #endregion

            #region HostBuilder
            var host = new HostBuilder()
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddConfiguration(configuration);
                })
                .ConfigureServices((context, services) =>
                {
                    services.Configure<Config>(options => context.Configuration.GetSection("Config").Bind(options));

                    // Singletons
                    services.AddSingleton<OtgwClient>();

                    // Hosted services
                    services.AddHostedService<Logic>();
                })
                .UseSerilog()
                .Build();
            #endregion

            await host.RunAsync();
        }
    }
}