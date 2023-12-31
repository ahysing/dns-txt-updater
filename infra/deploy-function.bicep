
@allowed([
  'test'
  'qa'
  'prod'
])
param environment string = 'test'
param lastReview string = utcNow('yyyy-MM-dd')
param location string = 'norwayeast'
param logAnalyticsSubscription string

targetScope = 'subscription'

var saName = 'sadnstxtupdater${environment}'
var tags = {
  environment: environment
  'last-review': lastReview
  'personal-data': 'no'
  confidentiality: 'Internal use'
}

resource rg 'Microsoft.Resources/resourceGroups@2020-06-01' = {
  name: 'rg-dns-txt-updater-${environment}'
  location: location
  tags: tags
}

// var appServicePlanLocation = 'Norway East'
param appServicePlan string = 'asp-dns-txt-updater-${environment}'

module webfarm './resources/webfarm.bicep' = {
  name: 'webfarm-dns-txt-updater-${environment}'
  params: {
    name: appServicePlan
    location: location
    tags: tags
  }
  scope: rg
}

module plan './resources/plan.bicep' = {
  name: 'app-service-plan-dns-txt-updater-${environment}'
  params: {
    environment: environment
    location: location
    tags: tags
    logAnalyticsSubscription: logAnalyticsSubscription
  }
  scope: rg
}

var siteName = 'func-dns-txt-updater-${environment}'
module site './resources/website.bicep' = {
  name: 'site-dns-txt-updater-${environment}'
  params: {
    asp: webfarm.outputs.asp
    name: siteName
    tags: tags
    location: location
  }
  scope: rg
}

module sa './resources/sa.bicep' = {
  name: 'sa-dns-txt-updater-${environment}'
  params: {
    name: saName
    location: location
    tags: tags
  }
  scope: rg
}
