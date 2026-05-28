targetScope = 'resourceGroup'

param location string = resourceGroup().location
param tags object = {}
param resourceName string
param principalId string
param principalType string
param aiServicesAccountName string = ''
param aiProjectName string = ''
param connectionName string = 'storage-connection'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: resourceName
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  identity: { type: 'SystemAssigned' }
  properties: {
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    accessTier: 'Hot'
  }
}

resource aiAccount 'Microsoft.CognitiveServices/accounts@2025-04-01-preview' existing = if (!empty(aiServicesAccountName) && !empty(aiProjectName)) {
  name: aiServicesAccountName
  resource aiProject 'projects' existing = {
    name: aiProjectName
  }
}

resource storageRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(aiServicesAccountName) && !empty(aiProjectName)) {
  name: guid(storageAccount.id, aiAccount.id, 'ai-storage-contributor')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe') // Storage Blob Data Contributor
    principalId: aiAccount::aiProject.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource userStorageRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, principalId, 'Storage Blob Data Contributor')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalId: principalId
    principalType: principalType
  }
}

module storageConnection '../ai/connection.bicep' = if (!empty(aiServicesAccountName) && !empty(aiProjectName)) {
  name: 'storage-connection-creation'
  params: {
    aiServicesAccountName: aiServicesAccountName
    aiProjectName: aiProjectName
    connectionConfig: {
      name: connectionName
      category: 'AzureStorageAccount'
      target: storageAccount.properties.primaryEndpoints.blob
      authType: 'AAD'
      isSharedToAll: true
      metadata: {
        ApiType: 'Azure'
        ResourceId: storageAccount.id
        location: storageAccount.location
      }
    }
  }
}

output storageAccountName string = storageAccount.name
output storageAccountId string = storageAccount.id
output storageConnectionName string = (!empty(aiServicesAccountName) && !empty(aiProjectName)) ? storageConnection!.outputs.connectionName : ''
