using DNSUpdater.Library.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DNSUpdater.Console
{
    public class Program
    {

        public static async Task Main(string[] args)
        {
            System.Console.WriteLine("Starting up.");
            using var host = CreateHostBuilder(args).Build();

            await DoStuff(host.Services);

            await host.RunAsync();
        }

        private static IHostBuilder CreateHostBuilder(string[] args)
        {
            var basePath = AppContext.BaseDirectory;
            var configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", false)
                .Build();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();

            return Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(builder =>
                {
                    builder.Sources.Clear();
                    builder.AddConfiguration(configuration);
                })
                .ConfigureServices(services =>
                {
                    services.AddLogging(c => c.AddSerilog().AddConsole());
                    services.AddSingleton<IDnsServiceFactory, AzureDnsServiceFactory>();
                })
                .UseConsoleLifetime();
        }

        private static async Task DoStuff(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var provider = scope.ServiceProvider;

            var config = provider.GetRequiredService<IConfiguration>();
            var logger = provider.GetRequiredService<ILogger<Program>>();
            var factory = provider.GetRequiredService<IDnsServiceFactory>();
            var service = factory.GetDnsServiceAsync();

            var fqdn = config["fqdn"];
            if (!string.IsNullOrWhiteSpace(fqdn))
            {
                var isKnown = await service.IsKnown(fqdn);
                var ip = config["txt"];
                var ttlRaw = config["ttl"];
                if (!Int32.TryParse(ttlRaw, out int ttl))
                {
                    ttl = 3600;
                }

                if (!string.IsNullOrWhiteSpace(ip))
                {
                    var result = await service.UpdateTXTRecord(fqdn, ip, ttl);
                    logger.LogDebug($"Result: {result}");
                } else {
                    logger.LogWarning("$\"ip\" is not configured in the configuration file.");
                }
            } else {
                logger.LogWarning("$\"qfdn\" is not configured in the configuration file.");
            }
        }
    }
}
