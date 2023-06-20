using Azure;
using Azure.ResourceManager.Dns;
using Azure.ResourceManager.Dns.Models;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DNSUpdater.Library.Services
{
    public class AzureDnsService : IDnsService
    {
        private struct Domain
        {
            internal string fqdn;
            internal string subdomain;
            internal string domain;
        }

        private string? zoneName;
        private string? rgName;
        private readonly IConfiguration config;
        private readonly ILogger<AzureDnsService> logger;
        private readonly SubscriptionResource client;

        public AzureDnsService(SubscriptionResource azureClient, IConfiguration config, ILogger<AzureDnsService> logger)
        {
            this.rgName = null;
            this.zoneName = null;
            this.config = config;
            this.logger = logger;
            this.client = azureClient;
        }

        public async Task<bool> IsKnown(string fqdn)
        {
            if (string.IsNullOrWhiteSpace(this.rgName))
            {
                SetupRgName();
            }

            try
            {
                var domain = DisectFqdn(fqdn);
                return await HasRecordSet(domain);
            }
            catch (Exception e)
            {
                this.logger.LogError(e, "Failed to find domain");
            }

            return false;
        }

        public async Task<UpdateStatus> UpdateTXTRecord(string fqdn, string txtRecord, int ttl)
        {
            if (string.IsNullOrEmpty(this.rgName))
            {
                SetupRgName();
            }

            if (string.IsNullOrEmpty(this.zoneName))
            {
                SetupZone();
            }

            try
            {
                var response =  this.client.GetResourceGroupAsync(this.rgName);
                var zoneName = this.zoneName;
                var records = new List<DnsTxtRecordResource>();
                var zones = (await response).Value.GetDnsZones();
                var domain = DisectFqdn(fqdn);
                foreach (var z in zones)
                {
                    var zone = z.GetDnsTxtRecord(zoneName);
                    var Txtrecord = zone.Value;
                    {
                        if (Txtrecord.Data.Name == fqdn)
                        {
                            if (Txtrecord.Data.DnsTxtRecords.Any(txt => txt.Values.Equals(txtRecord)))
                            {
                                this.logger.LogInformation($"txt update not required. Domain: {domain.fqdn}, TXT-record: {txtRecord}");
                                return UpdateStatus.nochg;
                            }

                            this.logger.LogInformation($"TXT update. Domain: {domain.fqdn}, txt-record: {txtRecord}");

                            var info = new DnsTxtRecordInfo();
                            info.Values.Add(txtRecord);

                            var txtRecordData = new DnsTxtRecordData() { TtlInSeconds = ttl };
                            txtRecordData.DnsTxtRecords.Clear();
                            txtRecordData.DnsTxtRecords.Add(info);
                            Txtrecord.Update(txtRecordData);
                            return UpdateStatus.good;
                        }
                    }
                }

                return UpdateStatus.nohost;
            }
            catch (ApplicationException e)
            {
                this.logger.LogError(e, $"Failed to parse fqdn. Domain: {fqdn}, TXT-record: {txtRecord}");
                return UpdateStatus.notfqdn;
            }
            catch (Exception e)
            {
                this.logger.LogError(e, "Fail to update domain");
            }

            return UpdateStatus.othererr;
        }

        private void SetupRgName()
        {
            try {
                this.rgName = config["resourceGroupName"];
            } catch {
                this.logger.LogError("Failed loading \"resourceGroupName\"");
            }
        }

        private void SetupZone()
        {
            try {
                if (config != null)
                {
                    this.zoneName = config["zoneName"];
                }
            } catch {
                this.logger.LogError("Failed loading \"zoneName\"");
            }
        }

        private Domain DisectFqdn(string fqdn)
        {
            if (Uri.CheckHostName(fqdn) != UriHostNameType.Dns)
            {
                throw new ApplicationException($"{fqdn} is not a valid domain");
            }

            var parts = fqdn.Split(".");
            if (parts.Length <= 2)
            {
                throw new ApplicationException($"{fqdn} does not contain a subdomain");
            }

            var domain = $"{parts[^2]}.{parts[^1]}";
            var subdomain = fqdn.Replace("." + domain, "");
            return new Domain { domain = domain, fqdn = fqdn, subdomain = subdomain };
        }

        private async Task<bool> HasRecordSet(Domain domain)
        {
            var zoneName = this.zoneName ?? domain.domain;
            var resourceGroupResponseTask =  this.client.GetResourceGroupAsync(this.rgName);
            var resourceGroupResponse = (await resourceGroupResponseTask);
            if (resourceGroupResponse.HasValue)
            {
                Response<DnsZoneResource> response = resourceGroupResponse.Value.GetDnsZones().Get(zoneName);
                if (response.HasValue)
                {
                    DnsZoneResource zone = response.Value;
                    return true;
                }
            }

            return false;
        }
    }
}