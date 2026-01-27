param location string
param serviceBusName string
param skuName string = 'Standard'

resource serviceBus 'Microsoft.ServiceBus/namespaces@2021-11-01' = {
  name: serviceBusName
  location: location
  sku: {
    name: skuName
    tier: skuName
  }
}

resource queue 'Microsoft.ServiceBus/namespaces/queues@2021-11-01' = {
  parent: serviceBus
  name: 'events-ingestion'
  properties: {
    maxDeliveryCount: 10
    deadLetteringOnMessageExpiration: true
  }
}
