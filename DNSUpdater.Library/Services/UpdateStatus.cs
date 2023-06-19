namespace DNSUpdater.Library.Services
{
    public enum UpdateStatus
    {
        nochg,
        good,
        nohost,
        notfqdn,
        badauth,
        othererr,
        invalidinput
    }
}