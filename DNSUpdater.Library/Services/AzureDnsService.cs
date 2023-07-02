using Azure;
using Azure.ResourceManager.Dns;
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
                this.logger.LogError(e, $"Failed to find domain. {e.Message}");
            }

            return false;
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
            if (!Uri.IsWellFormedUriString(fqdn, UriKind.RelativeOrAbsolute))
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
            return new Domain
            {
                domain = domain,
                fqdn = fqdn,
                subdomain = subdomain
            };
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

            this.logger.LogInformation("Update TXT-record");
            try
            {
                var response =  this.client.GetResourceGroupAsync(this.rgName);
                var zoneName = this.zoneName;
                var records = new List<DnsTxtRecordResource>();
                var zones = (await response).Value.GetDnsZones();
                var domain = DisectFqdn(fqdn);
                foreach (var zone in zones)
                {
                    this.logger.LogDebug($"zone {zone.Data.Name}");
                    if (zone.Data.Name == domain.domain)
                    {
                        var txtCollection = zone.GetDnsTxtRecords();
                        foreach (var dnsTxtRecordResource in txtCollection)
                        {
                            this.logger.LogDebug($"zone name {dnsTxtRecordResource.Data.Name}");
                            if (dnsTxtRecordResource.Data.Name == domain.subdomain)
                            {
                                if (dnsTxtRecordResource.Data.DnsTxtRecords.Any(txt => txt.Values.Equals(txtRecord)))
                                {
                                    this.logger.LogDebug($"TXT-record {domain.subdomain}: {txtRecord} already exists. Skipping");
                                    return UpdateStatus.nochg;
                                }

                                this.logger.LogDebug($"Updating TXT-record {domain.subdomain}: {txtRecord}");
                                var txtRecordData = new DnsTxtRecordData()
                                {
                                    TtlInSeconds = ttl,
                                    Metadata = {
                                        [domain.subdomain] = txtRecord
                                    }
                                };
                                // https://github.com/thomhurst/azure-sdk-for-net/blob/3bfd7ba3dc94af7e4d2aba8a6a4661c0b6729c66/sdk/dns/Azure.ResourceManager.Dns/samples/Generated/Samples/Sample_DnsTxtRecordResource.cs#L18
                                DnsTxtRecordResource result = await dnsTxtRecordResource.UpdateAsync(txtRecordData);
                                return UpdateStatus.good;
                            } else {
                                this.logger.LogDebug($"zone name {dnsTxtRecordResource.Data.Name} != {domain.subdomain}");
                            }
                        }

                        this.logger.LogInformation($"Creating TXT-record {domain.subdomain}: {txtRecord}");
                        DnsTxtRecordData newTx = new DnsTxtRecordData()
                        {
                            TtlInSeconds = ttl
                        };
                        var info = new Azure.ResourceManager.Dns.Models.DnsTxtRecordInfo();
                        info.Values.Add(txtRecord);
                        newTx.DnsTxtRecords.Add(info);
                        await txtCollection.CreateOrUpdateAsync(WaitUntil.Completed, domain.subdomain, newTx);
                        return UpdateStatus.good;
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
                this.logger.LogError(e, $"Fail to update domain. {e.GetType().Name} {e.Message}");
            }

            return UpdateStatus.othererr;
        }

        public async Task<UpdateStatus> DeleteTXTRecord(string fqdn, string txtRecord)
        {
            if (string.IsNullOrEmpty(this.rgName))
            {
                SetupRgName();
            }

            if (string.IsNullOrEmpty(this.zoneName))
            {
                SetupZone();
            }

            logger.LogInformation("Update TXT-record");
            try
            {
                var response =  this.client.GetResourceGroupAsync(this.rgName);
                var zoneName = this.zoneName;
                var records = new List<DnsTxtRecordResource>();
                var zones = (await response).Value.GetDnsZones();
                var domain = DisectFqdn(fqdn);
                foreach (var zone in zones)
                {
                    this.logger.LogDebug($"zone {zone.Data.Name}");
                    if (zone.Data.Name == domain.domain)
                    {
                        foreach (var dnsTxtRecordResource in zone.GetDnsTxtRecords())
                        {
                            this.logger.LogDebug($"zone name {dnsTxtRecordResource.Data.Name}");
                            if (dnsTxtRecordResource.Data.Name == domain.subdomain)
                            {
                                if (string.IsNullOrEmpty(txtRecord) || dnsTxtRecordResource.Data.DnsTxtRecords.Any(txt => txt.Values.Equals(txtRecord)))
                                {
                                    this.logger.LogInformation($"Deleting TXT-record {domain.subdomain}: {txtRecord}");
                                    await dnsTxtRecordResource.DeleteAsync(WaitUntil.Started);
                                    return UpdateStatus.good;
                                }

                                this.logger.LogDebug($"nochg");
                                return UpdateStatus.nochg;
                            } else {
                                this.logger.LogDebug($"zone name {dnsTxtRecordResource.Data.Name} != {domain.subdomain}");
                            }
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
                this.logger.LogError(e, $"Fail to update domain. {e.GetType().Name} {e.Message}");
            }

            return UpdateStatus.othererr;
        }
    }
}