targetScope = 'resourceGroup'

param tags object = {}
param location string

param aiFoundryProjectName string
param modelDeploymentName string = 'gpt-5.4-mini'
@description('Actual Azure OpenAI model name (e.g. gpt-4o-mini, gpt-4.1-mini)')
param modelName string = 'gpt-4o-mini'
param modelVersion string = '2024-07-18'

@description('Id of the user or app to assign application roles')
param principalId string

@description('Principal type: User or ServicePrincipal')
param principalType string = 'User'

@description('Provision Azure AI Search + Storage and wire Foundry connections')
param enableSearch bool = true

var resourceToken = uniqueString(subscription().id, resourceGroup().id, location)

// ── Monitoring ────────────────────────────────────────────────────────────────

module logAnalytics '../monitor/loganalytics.bicep' = {
  name: 'logAnalytics'
  params: {
    location: location
    tags: tags
    name: 'log-${resourceToken}'
  }
}

module applicationInsights '../monitor/applicationinsights.bicep' = {
  name: 'applicationInsights'
  params: {
    location: location
    tags: tags
    name: 'appi-${resourceToken}'
    logAnalyticsWorkspaceId: logAnalytics.outputs.id
  }
}

// ── Azure AI Foundry (new CognitiveServices pattern) ─────────────────────────

resource aiAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: 'ai-${resourceToken}'
  location: location
  tags: tags
  sku: { name: 'S0' }
  kind: 'AIServices'
  identity: { type: 'SystemAssigned' }
  properties: {
    allowProjectManagement: true
    customSubDomainName: 'ai-${resourceToken}'
    networkAcls: {
      defaultAction: 'Allow'
      virtualNetworkRules: []
      ipRules: []
    }
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: true
  }

  @batchSize(1)
  resource modelDeployments 'deployments' = [
    for dep in deploymentsList: {
      name: dep.name
      properties: { model: dep.model }
      sku: dep.sku
    }
  ]

  resource project 'projects' = {
    name: aiFoundryProjectName
    location: location
    identity: { type: 'SystemAssigned' }
    properties: {
      description: '${aiFoundryProjectName} project'
      displayName: aiFoundryProjectName
    }
    dependsOn: [modelDeployments]
  }
}

var deploymentsList = [
  {
    name: modelDeploymentName
    model: {
      format: 'OpenAI'
      name: modelName
      version: modelVersion
    }
    sku: {
      name: 'GlobalStandard'
      capacity: 10
    }
  }
]

// ── App Insights connection into the Foundry project ─────────────────────────

resource appInsightConnection 'Microsoft.CognitiveServices/accounts/projects/connections@2025-04-01-preview' = {
  parent: aiAccount::project
  name: 'appi-connection'
  properties: {
    category: 'AppInsights'
    target: applicationInsights.outputs.id
    authType: 'ApiKey'
    isSharedToAll: true
    credentials: {
      key: applicationInsights.outputs.connectionString
    }
    metadata: {
      ApiType: 'Azure'
      ResourceId: applicationInsights.outputs.id
    }
  }
}

// ── RBAC: project MI → Log Analytics Reader (continuous eval) ────────────────

resource projectLogAnalyticsReaderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, resourceGroup().id, aiAccount::project.name, '73c42c96-874c-492b-b04d-ab87d138a893')
  properties: {
    principalId: aiAccount::project.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', '73c42c96-874c-492b-b04d-ab87d138a893') // Log Analytics Reader
  }
}

// ── RBAC: user/deployer → Azure AI User on project ───────────────────────────

resource userAzureAIUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: aiAccount::project
  name: guid(subscription().id, resourceGroup().id, principalId, '53ca6127-db72-4b80-b1b0-d745d6d5456d')
  properties: {
    principalId: principalId
    principalType: principalType
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', '53ca6127-db72-4b80-b1b0-d745d6d5456d') // Azure AI User
  }
}

// ── Storage (required for AI Search indexer) ─────────────────────────────────

module storage '../storage/storage.bicep' = if (enableSearch) {
  name: 'storage'
  params: {
    location: location
    tags: tags
    resourceName: 'st${resourceToken}'
    principalId: principalId
    principalType: principalType
    aiServicesAccountName: aiAccount.name
    aiProjectName: aiAccount::project.name
  }
}

// ── Azure AI Search ───────────────────────────────────────────────────────────

module search '../search/azure-ai-search.bicep' = if (enableSearch) {
  name: 'azure-ai-search'
  params: {
    tags: tags
    location: location
    resourceName: 'search-${resourceToken}'
    storageAccountResourceId: enableSearch ? storage!.outputs.storageAccountId : ''
    principalId: principalId
    principalType: principalType
    aiServicesAccountName: aiAccount.name
    aiProjectName: aiAccount::project.name
  }
  dependsOn: [storage]
}

// ── Outputs ───────────────────────────────────────────────────────────────────

output AZURE_AI_PROJECT_ENDPOINT string = aiAccount::project.properties.endpoints['AI Foundry API']
output AZURE_OPENAI_ENDPOINT string = aiAccount.properties.endpoints['OpenAI Language Model Instance API']
output APPLICATIONINSIGHTS_CONNECTION_STRING string = applicationInsights.outputs.connectionString
output APPLICATIONINSIGHTS_RESOURCE_ID string = applicationInsights.outputs.id
output LOG_ANALYTICS_WORKSPACE_ID string = logAnalytics.outputs.id
output aiServicesAccountName string = aiAccount.name
output projectName string = aiAccount::project.name
output accountId string = aiAccount.id
output projectId string = aiAccount::project.id

output searchServiceName string = enableSearch ? search!.outputs.searchServiceName : ''
output searchEndpoint string = enableSearch ? search!.outputs.searchEndpoint : ''
output searchConnectionName string = enableSearch ? search!.outputs.searchConnectionName : ''
output kbMcpConnectionName string = enableSearch ? search!.outputs.kbMcpConnectionName : ''
output storageAccountName string = enableSearch ? storage!.outputs.storageAccountName : ''
