using System;
using DNSUpdater.Library.Services;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: FunctionsStartup(typeof(DNSUpdater.Function.Startup))]
namespace DNSUpdater.Function
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Environment.CurrentDirectory)
                .AddJsonFile("settings.json", true)
                .AddJsonFile($"local.settings.json", true, true)
                .AddEnvironmentVariables()
                .Build();

            builder.Services.AddSingleton(config);
            builder.Services.AddSingleton<IDnsServiceFactory, AzureDnsServiceFactory>();
        }
    }
}

