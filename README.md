# Azure DNS Updater

**Problem:** Domains are handled by Azure DNS. One (or multiple) subdomains are pointing to a dynamic (public) IP and need to be updated on IP change.

**Solution:** An Azure Function that can be triggered by any authorized ddclient, basically DynDNS on Azure. ddclient is not required, as long as the [dynDNS API-specs](https://help.dyn.com/remote-access-api/perform-update/) are followed.

**Use-Case:** Ubiquiti USG / Ubiquiti UDM dynamic DNS feature.

![image](https://user-images.githubusercontent.com/842121/170864950-cf8e85b2-8dbb-4cb9-a284-f36d4f9bee2a.png)

### Setup

1. Set up you DNS Zones in Azure
2. Set up your Azure Function App (v4/.NET6, consumption plan, Application Insights enabled)

![image](https://user-images.githubusercontent.com/842121/170865030-fdb026b2-fb98-4d1f-af53-73e8c2f1657d.png)

3. Deploy this Azure Function to your Function App resource and configure Application Settings accordingly

### Configuration

#### Configure a Managed Identity

Managed identity is turned on for the azure function provided in this code. This means that the azure function will function as it's own identity with it's own accesses to azure resources.

To check that you have Managed Identity turned on visit the azure function and look for Identity in the blade to the left on the azure portal.

Now we just need to assign the managed access to the azure DNS zone we want to configure.

_detailed walk-through:_ (<https://docs.microsoft.com/en-us/azure/role-based-access-control/role-assignments-portal?tabs=current>)

##### quick guide

1. Select "Access control (IAM)" in your DNS resource (or resource group if you have multiple DNS Zones that you want to modify)
2. Click on "Add role assignment"
3. Search for "DNS Zone Contributor", select it and click "Next"
4. Click "Select Members" and search for your Managed Identtity. **The name of the managed identity is identical to the name of the azure function**
5. Click "Next" and then "Review + assign"

You can double check the success of your operation by providing your Azure Function name to the "Check access" form

![image](https://user-images.githubusercontent.com/842121/170866976-4086bbe0-ec17-4c70-a326-413fe17baf3a.png)

#### getting the remaining configuration items

- **tenantId** - you can get this from your AAD Overview page
- **subscriptionId** - the GUID of your subscription, can be found in the overview page of any resource
- **rgName** - the name of the resource group that holds your DNS Zone resources

#### local testing

Set up local.settings.yaml with the following keys

```yaml
{
    "Values": {
        "clientPassword": "password"
        "clientUsername": "username",
        "fqdn": "<fqdn>",
        "resourceGroupName": "<rg>",
    }
}
```

#### production

Add the following keys to your AppSettings.

| Name | Value |
|:-----|:------|
| clientPassword | password for txt-record client |
| clientUsername | username for txt-record client |
| fqdn | Your domain name. must contain a subdomain |
| resourceGroupName | Name of the resource group with the dynamic DNS |
