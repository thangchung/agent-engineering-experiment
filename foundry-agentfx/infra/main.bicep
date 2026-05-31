targetScope = 'subscription'

@description('Environment name (dev, staging, prod)')
param environmentName string

@description('Primary location for resources — must support Azure AI Foundry')
param location string = 'westus'

@description('Model deployment name (alias used by the app)')
param modelDeploymentName string = 'gpt-5.4-mini'

@description('Actual Azure OpenAI model name to deploy (e.g. gpt-4o-mini, gpt-4.1-mini)')
param modelName string = 'gpt-4o-mini'

@description('Azure OpenAI model version')
param modelVersion string = '2024-07-18'

@description('Skip Container Apps provisioning. Set to "true" for infra-only mode (local dev with Aspire).')
param skipContainerApps string = 'false'

@description('Enable Azure AI Search + Knowledge Base provisioning')
param enableSearch bool = true

@description('Foundry IQ (AI Search) endpoint override — leave empty to use provisioned search endpoint')
param foundryIqEndpoint string = ''

@description('Knowledge base name for Foundry IQ queries')
param foundryIqKbName string = 'coffeeshop-kb'

@description('Foundry Toolbox MCP endpoint — enables code_interpreter tool when non-empty')
param toolboxEndpoint string = ''

@description('Brave Search API key — enables web_search tool when non-empty')
@secure()
param braveSearchApiKey string = ''

@secure()
param slackBotToken string = ''
@secure()
param slackAppToken string = ''
@secure()
param slackSigningSecret string = ''

@description('Id of the user or app running azd — used for RBAC role assignments')
param principalId string

@description('Principal type: User or ServicePrincipal')
param principalType string = 'User'

var tags = {
  'azd-env-name': environmentName
  project: 'foundry-agentfx'
}

var resourceToken = toLower(uniqueString(subscription().id, environmentName, location))

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

// ── Container Registry (for Container Apps images) ───────────────────────────

module containerRegistry 'modules/container-registry.bicep' = {
  name: 'container-registry'
  scope: rg
  params: {
    location: location
    tags: tags
    registryName: 'acr${resourceToken}'
  }
}

// ── Azure AI Foundry (CognitiveServices/accounts AIServices) + Monitoring + Search ──

module aiProject 'core/ai/ai-project.bicep' = {
  name: 'ai-project'
  scope: rg
  params: {
    location: location
    tags: tags
    aiFoundryProjectName: 'foundry-agentfx-${environmentName}'
    modelDeploymentName: modelDeploymentName
    modelName: modelName
    modelVersion: modelVersion
    principalId: principalId
    principalType: principalType
    enableSearch: enableSearch
  }
}

// ── Container Apps (skippable for local dev) ─────────────────────────────────

module containerApps 'modules/container-apps.bicep' = if (toLower(skipContainerApps) != 'true') {
  name: 'container-apps'
  scope: rg
  params: {
    location: location
    tags: tags
    environmentName: 'cae-${resourceToken}'
    logAnalyticsWorkspaceId: aiProject.outputs.LOG_ANALYTICS_WORKSPACE_ID
    appInsightsConnectionString: aiProject.outputs.APPLICATIONINSIGHTS_CONNECTION_STRING
    containerRegistryLoginServer: containerRegistry.outputs.loginServer
    containerRegistryResourceId: containerRegistry.outputs.registryId
    foundryProjectEndpoint: aiProject.outputs.AZURE_AI_PROJECT_ENDPOINT
    foundryProjectResourceId: aiProject.outputs.projectId
    foundryAccountName: aiProject.outputs.aiServicesAccountName
    foundryIqEndpoint: !empty(foundryIqEndpoint) ? foundryIqEndpoint : aiProject.outputs.searchEndpoint
    foundryIqKbName: foundryIqKbName
    toolboxEndpoint: !empty(toolboxEndpoint) ? toolboxEndpoint : '${aiProject.outputs.searchEndpoint}/knowledgebases/${foundryIqKbName}/mcp?api-version=2025-11-01-preview'
    braveSearchApiKey: braveSearchApiKey
    slackBotToken: slackBotToken
    slackAppToken: slackAppToken
    slackSigningSecret: slackSigningSecret
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

output AZURE_RESOURCE_GROUP string = rg.name
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = containerRegistry.outputs.loginServer
output AZURE_CONTAINER_REGISTRY_NAME string = containerRegistry.outputs.registryName

// Foundry
output FOUNDRY_PROJECT_ENDPOINT string = aiProject.outputs.AZURE_AI_PROJECT_ENDPOINT
output AZURE_AI_PROJECT_ENDPOINT string = aiProject.outputs.AZURE_AI_PROJECT_ENDPOINT
output AZURE_OPENAI_ENDPOINT string = aiProject.outputs.AZURE_OPENAI_ENDPOINT
output AZURE_AI_MODEL_DEPLOYMENT_NAME string = modelDeploymentName
output AZURE_AI_ACCOUNT_NAME string = aiProject.outputs.aiServicesAccountName
output AZURE_AI_PROJECT_NAME string = aiProject.outputs.projectName

// Monitoring
output APPLICATIONINSIGHTS_CONNECTION_STRING string = aiProject.outputs.APPLICATIONINSIGHTS_CONNECTION_STRING

// AI Search
output AZURE_AI_SEARCH_SERVICE_NAME string = aiProject.outputs.searchServiceName
output AZURE_AI_SEARCH_SERVICE_ENDPOINT string = aiProject.outputs.searchEndpoint
output AZURE_AI_SEARCH_CONNECTION_NAME string = aiProject.outputs.searchConnectionName
output AZURE_AI_SEARCH_KB_MCP_CONNECTION_NAME string = aiProject.outputs.kbMcpConnectionName
output AZURE_STORAGE_ACCOUNT_NAME string = aiProject.outputs.storageAccountName

// Container Apps (empty when skipped or hosted)
output CLAW_SLACK_URL string = toLower(skipContainerApps) != 'true' ? containerApps!.outputs.clawSlackUrl : ''
output COFFEESHOP_MCP_URL string = toLower(skipContainerApps) != 'true' ? containerApps!.outputs.coffeeshopMcpUrl : ''
output TOOLSEARCH_GATEWAY_URL string = toLower(skipContainerApps) != 'true' ? containerApps!.outputs.toolsearchGatewayUrl : ''
