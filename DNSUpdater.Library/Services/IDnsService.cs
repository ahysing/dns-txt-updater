namespace DNSUpdater.Library.Services;

public interface IDnsService
{
    Task<bool> IsKnown(string fqdn);
    Task<UpdateStatus> UpdateTXTRecord(string fqdn, string ip, int ttl);
}