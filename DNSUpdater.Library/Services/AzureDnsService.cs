using System.Net;
using System.Text;
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

        public async Task<UpdateStatus> UpdateARecord(string fqdn, string ip, int ttl)
        {
            IPAddress parsedIp;
            Domain domain;
            try {
                parsedIp = System.Net.IPAddress.Parse(ip);
                domain = DisectFqdn(fqdn);
            } catch (Exception e)
            {
                this.logger.LogWarning(e, "Invalid input");
                return UpdateStatus.invalidinput;
            }

            if (string.IsNullOrEmpty(this.rgName))
            {
                SetupRgName();
            }

            if (string.IsNullOrEmpty(this.zoneName))
            {
                SetupZone();
            }

            var zoneName = this.zoneName ?? domain.domain;
            var response =  this.client.GetResourceGroupAsync(this.rgName);

            try
            {
                var records = new List<DnsARecordResource>();
                var zones = (await response).Value.GetDnsZones();
                var zone = await zones.GetAsync(zoneName);
                foreach (var arecord in zone.Value.GetDnsARecords())
                {
                    if (arecord.Data.Name == fqdn)
                    {
                        LogIpv4Update(fqdn, ip, arecord.Data.DnsARecords);
                        if (arecord.Data.DnsARecords.Any(ar => ar.IPv4Address.Equals(parsedIp)))
                        {
                            this.logger.LogInformation($"IP update not required. Domain: {domain.fqdn}, ip: {ip}");
                            return UpdateStatus.nochg;
                        }

                        this.logger.LogInformation($"IP update. Domain: {domain.fqdn}, ip: {ip}");

                        var arecordData = new DnsARecordData() { TtlInSeconds = ttl };
                        arecordData.DnsARecords.Clear();
                        arecordData.DnsARecords.Add(new DnsARecordInfo() { IPv4Address = parsedIp });
                        arecord.Update(arecordData);
                        return UpdateStatus.good;
                    }
                }

                return UpdateStatus.nohost;
            }
            catch (ApplicationException e)
            {
                this.logger.LogError(e, $"Failed to parse fqdn.  Domain: {fqdn}, ip: {ip}");
                return UpdateStatus.notfqdn;
            }
            catch (Exception e)
            {
                this.logger.LogError(e, "Fail to update domain");
            }

            return UpdateStatus.othererr;
        }

        private void LogIpv4Update(string fqdn, string ip, IList<DnsARecordInfo> dnsARecords)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('[');
            var first = dnsARecords.FirstOrDefault();
            if (first != null)
            {
                sb.AppendFormat("\"{0}\"", first.IPv4Address.ToString());
            }

            foreach (var rest in dnsARecords.Skip(1))
            {
                sb.AppendFormat(", \"{0}\"", rest.IPv4Address.ToString());
            }

            sb.Append(']');
            this.logger.LogInformation("Updating qfdn: \"{0}\" to ip: \"{1}\" from ips: {2}", fqdn, ip, sb.ToString());
        }

        private void LogIpv6Update(string fqdn, string ip, IList<DnsAaaaRecordInfo> dnsAaaaRecords)
        {
                        StringBuilder sb = new StringBuilder();
            sb.Append('[');
            var first = dnsAaaaRecords.FirstOrDefault();
            if (first != null)
            {
                sb.AppendFormat("\"{0}\"", first.IPv6Address.ToString());
            }

            foreach (var rest in dnsAaaaRecords.Skip(1))
            {
                sb.AppendFormat(", \"{0}\"", rest.IPv6Address.ToString());
            }

            sb.Append(']');
            this.logger.LogInformation("Updating qfdn: \"{0}\" to ip: \"{1}\" from ips: {2}", fqdn, ip, sb.ToString());
        }


        public async Task<UpdateStatus> UpdateAAAARecord(string fqdn, string ip, int ttl)
        {
            IPAddress parsedIp;
            Domain domain;
            try {
                parsedIp = System.Net.IPAddress.Parse(ip);
                domain = DisectFqdn(fqdn);
            } catch (Exception e)
            {
                this.logger.LogWarning(e, "Invalid input");
                return UpdateStatus.invalidinput;
            } 

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
                var zoneName = this.zoneName ?? domain.domain;
                var records = new List<DnsARecordResource>();
                var zones = (await response).Value.GetDnsZones();
                var zone = await zones.GetAsync(zoneName);
                foreach (var aaaarecord in zone.Value.GetDnsAaaaRecords())
                {
                    if (aaaarecord.Data.Name == fqdn)
                    {
                        LogIpv6Update(fqdn, ip, aaaarecord.Data.DnsAaaaRecords);
                        if (aaaarecord.Data.DnsAaaaRecords.Any(ar => ar.IPv6Address.Equals(parsedIp)))
                        {
                            this.logger.LogInformation($"IP update not required. Domain: {domain.fqdn}, ipv6: {ip}");
                            return UpdateStatus.nochg;
                        }

                        this.logger.LogInformation($"IP update. Domain: {domain.fqdn}, ip: {ip}");

                        var aaaarecordData = new DnsAaaaRecordData() { TtlInSeconds = ttl };
                        aaaarecordData.DnsAaaaRecords.Clear();
                        aaaarecordData.DnsAaaaRecords.Add(new DnsAaaaRecordInfo() { IPv6Address = parsedIp });
                        aaaarecord.Update(aaaarecordData);
                        return UpdateStatus.good;
                    }
                }

                return UpdateStatus.nohost;
            }
            catch (ApplicationException e)
            {
                this.logger.LogError(e, $"Failed to parse fqdn. Domain: {fqdn}, ipv6: {ip}");
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