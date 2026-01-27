param location string
param synapseWorkspaceName string
param storageAccountName string

resource storageAccount 'Microsoft.Storage/storageAccounts@2022-09-01' existing = {
  name: storageAccountName
}

resource synapseWorkspace 'Microsoft.Synapse/workspaces@2021-06-01' = {
  name: synapseWorkspaceName
  location: location
  properties: {
    defaultDataLakeStorage: {
      accountUrl: 'https://${storageAccountName}.dfs.${environment().suffixes.storage}'
      filesystem: 'events-archive'
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
}

output synapseWorkspaceId string = synapseWorkspace.id
output workspaceUrl string = synapseWorkspace.properties.connectivityEndpoints.web
