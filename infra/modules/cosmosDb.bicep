param location string
param accountName string

resource account 'Microsoft.DocumentDB/databaseAccounts@2021-10-15' = {
  name: accountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    capabilities: [
      {
        name: 'EnableFreeTier'
      }
    ]
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2021-10-15' = {
  parent: account
  name: 'EventsDb'
  properties: {
    resource: {
      id: 'EventsDb'
    }
    options: {
      throughput: 1000 // Free Tier gives 1000 RU/s free
    }
  }
}

resource container 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2021-10-15' = {
  parent: database
  name: 'Events'
  properties: {
    resource: {
      id: 'Events'
      partitionKey: {
        paths: [
          '/PartitionKey'
        ]
        kind: 'Hash'
      }
      defaultTtl: 2592000 // 30 days
    }
  }
}

output endpoint string = account.properties.documentEndpoint
