param location string
param keyVaultName string

resource vault 'Microsoft.KeyVault/vaults@2021-10-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    accessPolicies: [] // Manage via RBAC preferable, or add policies later
    enableRbacAuthorization: true
  }
}
