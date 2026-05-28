targetScope = 'resourceGroup'

param tags object = {}
param resourceName string
param azureSearchSkuName string = 'standard'
param storageAccountResourceId string
param containerName string = 'knowledge'
param aiServicesAccountName string = ''
param aiProjectName string = ''
param principalId string
param principalType string
param connectionName string = 'azure-ai-search-connection'
param knowledgeBaseName string = 'coffeeshop-kb'
param kbMcpConnectionName string = 'kb-mcp-connection'
param location string = resourceGroup().location

resource aiAccount 'Microsoft.CognitiveServices/accounts@2025-04-01-preview' existing = if (!empty(aiServicesAccountName) && !empty(aiProjectName)) {
  name: aiServicesAccountName
  resource aiProject 'projects' existing = {
    name: aiProjectName
  }
}

resource searchService 'Microsoft.Search/searchServices@2024-06-01-preview' = {
  name: resourceName
  location: location
  tags: tags
  sku: { name: azureSearchSkuName }
  identity: { type: 'SystemAssigned' }
  properties: {
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'default'
    authOptions: {
      aadOrApiKey: {
        aadAuthFailureMode: 'http401WithBearerChallenge'
      }
    }
    disableLocalAuth: false
    publicNetworkAccess: 'enabled'
  }
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: last(split(storageAccountResourceId, '/'))
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' existing = {
  parent: storageAccount
  name: 'default'
}

resource storageContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: containerName
  properties: { publicAccess: 'None' }
}

// Search reads from Storage
resource searchToStorageRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, searchService.id, 'Storage Blob Data Reader', uniqueString(deployment().name))
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1') // Storage Blob Data Reader
    principalId: searchService.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Search uses OpenAI vectorization on the AI account
resource searchToAIServicesRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(aiServicesAccountName)) {
  name: guid(aiServicesAccountName, searchService.id, 'Cognitive Services OpenAI User', uniqueString(deployment().name))
  scope: aiAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd') // Cognitive Services OpenAI User
    principalId: searchService.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// AI Project MI → Search Service Contributor
resource aiProjectToSearchServiceRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(aiServicesAccountName) && !empty(aiProjectName)) {
  name: guid(searchService.id, aiServicesAccountName, aiProjectName, 'Search Service Contributor', uniqueString(deployment().name))
  scope: searchService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7ca78c08-252a-4471-8644-bb5ff32d4ba0') // Search Service Contributor
    principalId: aiAccount::aiProject.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// AI Project MI → Search Index Data Contributor
resource aiProjectToSearchDataRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(aiServicesAccountName) && !empty(aiProjectName)) {
  name: guid(searchService.id, aiServicesAccountName, aiProjectName, 'Search Index Data Contributor', uniqueString(deployment().name))
  scope: searchService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8ebe5a00-799e-43f5-93ac-243d3dce84a7') // Search Index Data Contributor
    principalId: aiAccount::aiProject.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// AI Services account MI → Search Index Data Contributor (for hosted agents)
resource aiAccountToSearchDataRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(aiServicesAccountName)) {
  name: guid(searchService.id, aiServicesAccountName, 'Search Index Data Contributor', uniqueString(deployment().name))
  scope: searchService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8ebe5a00-799e-43f5-93ac-243d3dce84a7')
    principalId: aiAccount.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// User → Search Index Data Contributor
resource userToSearchDataRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(searchService.id, principalId, 'Search Index Data Contributor', uniqueString(deployment().name))
  scope: searchService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8ebe5a00-799e-43f5-93ac-243d3dce84a7')
    principalId: principalId
    principalType: principalType
  }
}

// User → Search Service Contributor (keyless index management)
resource userToSearchServiceRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(searchService.id, principalId, 'Search Service Contributor', uniqueString(deployment().name))
  scope: searchService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7ca78c08-252a-4471-8644-bb5ff32d4ba0')
    principalId: principalId
    principalType: principalType
  }
}

// Standard AI Search connection (AAD auth) for agent tool use
module aiSearchConnection '../ai/connection.bicep' = if (!empty(aiServicesAccountName) && !empty(aiProjectName)) {
  name: 'ai-search-connection-creation'
  params: {
    aiServicesAccountName: aiServicesAccountName
    aiProjectName: aiProjectName
    connectionConfig: {
      name: connectionName
      category: 'CognitiveSearch'
      target: 'https://${searchService.name}.search.windows.net'
      authType: 'AAD'
      isSharedToAll: true
      metadata: {
        ApiVersion: '2024-07-01'
        ResourceId: searchService.id
        ApiType: 'Azure'
        type: 'azure_ai_search'
      }
    }
  }
  dependsOn: [aiProjectToSearchDataRoleAssignment]
}

// KB MCP connection — Foundry Toolbox calls KB MCP endpoint via project MI
resource kbMcpConnection 'Microsoft.CognitiveServices/accounts/projects/connections@2026-03-01' = if (!empty(aiServicesAccountName) && !empty(aiProjectName)) {
  parent: aiAccount::aiProject
  name: kbMcpConnectionName
  properties: {
    #disable-next-line BCP036
    authType: 'ProjectManagedIdentity'
    category: 'RemoteTool'
    target: 'https://${searchService.name}.search.windows.net/knowledgebases/${knowledgeBaseName}/mcp?api-version=2025-11-01-preview'
    isSharedToAll: true
    audience: 'https://search.azure.com/'
    metadata: { ApiType: 'Azure' }
  }
  dependsOn: [
    aiProjectToSearchDataRoleAssignment
    aiProjectToSearchServiceRoleAssignment
  ]
}

output searchServiceName string = searchService.name
output searchServiceId string = searchService.id
output searchEndpoint string = 'https://${searchService.name}.search.windows.net'
output searchConnectionName string = (!empty(aiServicesAccountName) && !empty(aiProjectName)) ? aiSearchConnection!.outputs.connectionName : ''
output kbMcpConnectionName string = (!empty(aiServicesAccountName) && !empty(aiProjectName)) ? kbMcpConnection.name : ''
