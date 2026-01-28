targetScope = 'resourceGroup'

@description('Location for all resources.')
param location string = resourceGroup().location

@description('Environment name (dev, prod).')
param environmentName string = 'dev'

var uniqueSuffix = uniqueString(resourceGroup().id)
var names = {
  serviceBus: 'sb-cloudscale-${environmentName}-${uniqueSuffix}'
  cosmosDb: 'cosmos-cloudscale-${environmentName}-${uniqueSuffix}'
  adx: 'adxcloudscale${environmentName}${uniqueSuffix}' // ADX name must be alphanumeric only? Start with letter.
  appServicePlan: 'asp-cloudscale-${environmentName}'
  ingestionApi: 'app-ingestion-${environmentName}-${uniqueSuffix}'
  containerEnv: 'cae-cloudscale-${environmentName}-${uniqueSuffix}'
  eventProcessor: 'ca-processor-${environmentName}-${uniqueSuffix}'
  keyVault: 'kv-cloudscale-${environmentName}-${uniqueSuffix}'
  storageAccount: 'stcloudscale${environmentName}${uniqueSuffix}' // SA names must be alphanumeric
  frontDoorProfile: 'fd-cloudscale-${environmentName}-${uniqueSuffix}'
  frontDoorEndpoint: 'fde-cloudscale-${environmentName}-${uniqueSuffix}'
  synapseWorkspace: 'syn-cloudscale-${environmentName}-${uniqueSuffix}'
  redis: 'redis-cloudscale-${environmentName}-${uniqueSuffix}'
}

// 0. Redis Cache
module redis 'modules/redis.bicep' = {
  name: 'redisDeploy'
  params: {
    location: location
    redisName: names.redis
  }
}

// 1. Key Vault
module keyVault 'modules/keyVault.bicep' = {
  name: 'keyVaultDeploy'
  params: {
    location: location
    keyVaultName: names.keyVault
  }
}

// 2. Service Bus (Premium for Production, Standard for Dev cost saving? Requirement says Premium for DLQ/Size)
// But user warned about cost. We'll stick to Standard for Bicep default, comments for Premium.
module serviceBus 'modules/serviceBus.bicep' = {
  name: 'serviceBusDeploy'
  params: {
    location: location
    serviceBusName: names.serviceBus
    skuName: 'Standard' // Upgraded to Standard for Topics/DLQ support as per diagram
  }
}

// 3. Cosmos DB
module cosmosDb 'modules/cosmosDb.bicep' = {
  name: 'cosmosDbDeploy'
  params: {
    location: location
    accountName: names.cosmosDb
  }
}

// 4. Ingestion API (App Service)
module ingestionApi 'modules/appService.bicep' = {
  name: 'ingestionApiDeploy'
  params: {
    location: location
    appServicePlanName: names.appServicePlan
    webAppName: names.ingestionApi
    serviceBusNamespace: names.serviceBus // passing name to construct connection string or identity role assign
  }
}

// 5. Event Processor (Container Apps)
module eventProcessor 'modules/containerApp.bicep' = {
  name: 'eventProcessorDeploy'
  params: {
    location: location
    environmentName: names.containerEnv
    containerAppName: names.eventProcessor
    serviceBusNamespace: names.serviceBus
    cosmosDbAccount: names.cosmosDb
  }
}

// 6. Storage Account (Archive/Cold Store)
module storageAccount 'modules/storageAccount.bicep' = {
  name: 'storageAccountDeploy'
  params: {
    location: location
    storageAccountName: names.storageAccount
  }
}

// 7. Azure Front Door & WAF (Edge Layer)
module frontDoor 'modules/frontDoor.bicep' = {
  name: 'frontDoorDeploy'
  params: {
    frontDoorProfileName: names.frontDoorProfile
    endpointName: names.frontDoorEndpoint
    originHostName: ingestionApi.outputs.defaultHostName // Use the raw hostname from App Service
  }
}

// 8. Azure Synapse (Analytics Layer)
module synapse 'modules/synapse.bicep' = {
  name: 'synapseDeploy'
  params: {
    location: location
    synapseWorkspaceName: names.synapseWorkspace
    storageAccountName: names.storageAccount
  }
}

// Outputs
output ingestionApiUrl string = ingestionApi.outputs.url
output cosmosDbEndpoint string = cosmosDb.outputs.endpoint
output storageAccountName string = storageAccount.outputs.storageAccountName
output frontDoorUrl string = 'https://${frontDoor.outputs.frontDoorEndpointHostName}'
output synapseWorkspaceUrl string = synapse.outputs.workspaceUrl
