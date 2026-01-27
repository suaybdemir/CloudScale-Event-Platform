param location string
param environmentName string
param containerAppName string
param serviceBusNamespace string
param cosmosDbAccount string

resource env 'Microsoft.App/managedEnvironments@2022-03-01' = {
  name: environmentName
  location: location
  properties: {}
}

resource containerApp 'Microsoft.App/containerApps@2022-03-01' = {
  name: containerAppName
  location: location
  properties: {
    managedEnvironmentId: env.id
    configuration: {
       activeRevisionsMode: 'Single'
       // secrets can be from Key Vault ref
    }
    template: {
      containers: [
        {
          name: 'processor'
          image: 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest' // Placeholder
          env: [
            {
               name: 'ServiceBus__FullyQualifiedNamespace'
               value: '${serviceBusNamespace}.servicebus.windows.net'
            }
            {
               name: 'CosmosDb__DatabaseName'
               value: 'EventsDb'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 10
        rules: [
          {
            name: 'queue-scale'
            custom: {
              type: 'azure-servicebus'
              metadata: {
                queueName: 'events-ingestion'
                namespace: serviceBusNamespace // KEDA needs auth, usually via identity or scaler connection string
                messageCount: '100'
              }
              auth: []
            }
          }
        ]
      }
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
}
