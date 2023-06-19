using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Azure.Identity;
using Azure.Core;
using Azure.ResourceManager.Dns;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;

namespace DNSUpdater.Library.Services;

public class AzureDnsServiceFactory : IDnsServiceFactory
{
    private readonly IConfiguration config;
    private readonly ILogger<AzureDnsServiceFactory> logger;
    private readonly ILoggerFactory loggerFactory;

    public AzureDnsServiceFactory(IConfiguration config, ILoggerFactory loggerFactory)
    {
        this.config = config;
        this.logger = loggerFactory.CreateLogger<AzureDnsServiceFactory>();
        this.loggerFactory = loggerFactory;
    }

    public async Task<IDnsService> GetDnsServiceAsync()
    {
        var tenantId = config["tenantId"];
        var clientId = config["clientId"];
        var secret = config["secret"];

        var subscriptionId = config["subscriptionId"];

        var logger = this.loggerFactory.CreateLogger<AzureDnsService>();
        ArmClient armClient = new ArmClient(new DefaultAzureCredential());
        SubscriptionResource subscription = await armClient.GetDefaultSubscriptionAsync();
        return new AzureDnsService(subscription, config, logger);
    }

    IDnsService IDnsServiceFactory.GetDnsServiceAsync()
    {
        var tenantId = config["tenantId"];
        var clientId = config["clientId"];
        var secret = config["secret"];

        var subscriptionId = config["subscriptionId"];

        var logger = this.loggerFactory.CreateLogger<AzureDnsService>();
        ArmClient armClient = new ArmClient(new DefaultAzureCredential());
        var subscription = armClient.GetDefaultSubscriptionAsync().GetAwaiter().GetResult();
        return new AzureDnsService(subscription, config, logger);
    }
}