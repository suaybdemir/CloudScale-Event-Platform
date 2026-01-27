// Azure Data Explorer (ADX) Cluster and Database for CloudScale Analytics
// Cost Warning: Dev/Test SKU is ~$0.12/hour minimum

@description('Location for resources')
param location string = resourceGroup().location

@description('Environment name')
param environmentName string = 'dev'

@description('Cosmos DB account name for Change Feed connection')
param cosmosAccountName string

@description('Service Bus namespace for Event Hub bridge (optional)')
param serviceBusNamespace string = ''

var clusterName = 'adxcloudscale${environmentName}${uniqueString(resourceGroup().id)}'
var databaseName = 'EventsAnalytics'

// ============================================
// ADX CLUSTER
// ============================================

resource adxCluster 'Microsoft.Kusto/clusters@2023-08-15' = {
  name: clusterName
  location: location
  sku: {
    name: 'Dev(No SLA)_Standard_E2a_v4'  // Cheapest SKU for dev
    tier: 'Basic'
    capacity: 1
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    enableStreamingIngest: true
    enableAutoStop: true  // Auto-stop when idle (cost saving)
    enableDiskEncryption: true
  }
  tags: {
    environment: environmentName
    project: 'CloudScale'
    costCenter: 'analytics'
  }
}

// ============================================
// ADX DATABASE
// ============================================

resource adxDatabase 'Microsoft.Kusto/clusters/databases@2023-08-15' = {
  parent: adxCluster
  name: databaseName
  location: location
  kind: 'ReadWrite'
  properties: {
    softDeletePeriod: 'P365D'   // 1 year retention
    hotCachePeriod: 'P31D'      // 31 days hot cache
  }
}

// ============================================
// COSMOS DB CHANGE FEED CONNECTION
// ============================================

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2023-11-15' existing = {
  name: cosmosAccountName
}

// Role assignment for ADX to read Cosmos Change Feed
resource adxCosmosRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(adxCluster.id, cosmosAccount.id, 'CosmosDBDataReader')
  scope: cosmosAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'fbdf93bf-df7d-467e-a4d2-9458aa1360c8') // Cosmos DB Account Reader
    principalId: adxCluster.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ============================================
// DATA CONNECTION (Cosmos Change Feed)
// ============================================

resource cosmosDataConnection 'Microsoft.Kusto/clusters/databases/dataConnections@2023-08-15' = {
  parent: adxDatabase
  name: 'cosmos-events-feed'
  location: location
  kind: 'CosmosDb'
  properties: {
    cosmosDbAccountResourceId: cosmosAccount.id
    cosmosDbDatabase: 'EventsDb'
    cosmosDbContainer: 'Events'
    tableName: 'Events'
    mappingRuleName: 'JsonMapping'
    managedIdentityResourceId: adxCluster.id
  }
  dependsOn: [
    adxCosmosRole
  ]
}

// ============================================
// OUTPUTS
// ============================================

output clusterUri string = adxCluster.properties.uri
output databaseName string = databaseName
output clusterPrincipalId string = adxCluster.identity.principalId
