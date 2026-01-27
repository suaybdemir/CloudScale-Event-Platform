param location string
param appServicePlanName string
param webAppName string
param serviceBusNamespace string

// B1 for Dev, P1v3 for Prod
resource plan 'Microsoft.Web/serverfarms@2021-03-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
}

resource app 'Microsoft.Web/sites@2021-03-01' = {
  name: webAppName
  location: location
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      appSettings: [
        {
          name: 'ServiceBus__FullyQualifiedNamespace'
          value: '${serviceBusNamespace}.servicebus.windows.net'
        }
        {
           name: 'ServiceBus__QueueName'
           value: 'events-ingestion'
        }
      ]
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
}

output url string = 'https://${app.properties.defaultHostName}'
output defaultHostName string = app.properties.defaultHostName
