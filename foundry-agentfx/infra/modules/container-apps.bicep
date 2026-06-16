param location string
param tags object
param environmentName string
param logAnalyticsWorkspaceId string
param appInsightsConnectionString string
param containerRegistryLoginServer string
param containerRegistryResourceId string
param foundryProjectEndpoint string

@description('Resource ID of the Foundry AI project — used to scope RBAC for claw-channels identity')
param foundryProjectResourceId string = ''

@description('Foundry AI Services account name — needed to reference the CognitiveServices/accounts/projects resource for RBAC')
param foundryAccountName string = ''

@description('Azure AI Search service name — used to grant toolsearch-gateway MI data-plane RBAC for knowledge base access')
param searchServiceName string = ''

@description('Placeholder image used on first deploy before real images are pushed. azd deploy overwrites this.')
param seedImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Foundry IQ (AI Search) endpoint — enables knowledge_lookup tool when non-empty')
param foundryIqEndpoint string = ''

@description('Knowledge base name for Foundry IQ queries')
param foundryIqKbName string = 'coffeeshop-kb'

@description('Azure AI Search API key fallback used by knowledge/toolbox calls in gateway when MI auth fails')
@secure()
param foundryIqApiKey string = ''

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

// User-assigned identity for ACR pull — pre-created so AcrPull can be assigned before containers start
resource acrPullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-acr-pull-${environmentName}'
  location: location
  tags: tags
}

resource acrPullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistryResourceId, acrPullIdentity.id, 'AcrPull')
  scope: existingRegistry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d') // AcrPull
    principalId: acrPullIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource existingRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: last(split(containerRegistryResourceId, '/'))
}

resource containerAppsEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: reference(logAnalyticsWorkspaceId, '2023-09-01').customerId
        sharedKey: listKeys(logAnalyticsWorkspaceId, '2023-09-01').primarySharedKey
      }
    }
  }
}

resource coffeeshopMcp 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'coffeeshop-mcp'
  location: location
  tags: union(tags, { 'azd-service-name': 'coffeeshop-mcp' })
  identity: {
    type: 'SystemAssigned, UserAssigned'
    userAssignedIdentities: { '${acrPullIdentity.id}': {} }
  }
  properties: {
    managedEnvironmentId: containerAppsEnv.id
    configuration: {
      ingress: {
        external: false
        targetPort: 8080
        transport: 'http'
      }
      registries: [{ server: containerRegistryLoginServer, identity: acrPullIdentity.id }]
    }
    template: {
      containers: [
        {
          name: 'coffeeshop-mcp'
          image: seedImage
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 5 }
    }
  }
}

resource toolsearchGateway 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'toolsearch-gateway'
  location: location
  tags: union(tags, { 'azd-service-name': 'toolsearch-gateway' })
  identity: {
    type: 'SystemAssigned, UserAssigned'
    userAssignedIdentities: { '${acrPullIdentity.id}': {} }
  }
  properties: {
    managedEnvironmentId: containerAppsEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
      }
      registries: [{ server: containerRegistryLoginServer, identity: acrPullIdentity.id }]
    }
    template: {
      containers: [
        {
          name: 'toolsearch-gateway'
          image: seedImage
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
            { name: 'Services__CoffeeshopMcp__Url', value: 'https://${coffeeshopMcp.properties.configuration.ingress.fqdn}' }
            { name: 'FoundryIQ__SearchEndpoint', value: foundryIqEndpoint }
            { name: 'FoundryIQ__KnowledgeBaseName', value: foundryIqKbName }
            { name: 'FoundryIQ__ApiKey', value: foundryIqApiKey }
            { name: 'Foundry__ApiKey', value: foundryIqApiKey }
            { name: 'Toolbox__McpEndpoint', value: toolboxEndpoint }
            { name: 'BraveSearch__ApiKey', value: braveSearchApiKey }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 5 }
    }
  }
}

resource existingSearchService 'Microsoft.Search/searchServices@2024-06-01-preview' existing = if (!empty(searchServiceName)) {
  name: searchServiceName
}

// RBAC: toolsearch-gateway MI -> Search Index Data Contributor (required for KB retrieve/mcp data-plane calls)
resource toolsearchGatewaySearchDataRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(searchServiceName)) {
  name: guid(existingSearchService.id, toolsearchGateway.id, 'Search Index Data Contributor')
  scope: existingSearchService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8ebe5a00-799e-43f5-93ac-243d3dce84a7') // Search Index Data Contributor
    principalId: toolsearchGateway.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// RBAC: toolsearch-gateway MI -> Search Service Contributor (some KB APIs require service-level permissions)
resource toolsearchGatewaySearchServiceRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(searchServiceName)) {
  name: guid(existingSearchService.id, toolsearchGateway.id, 'Search Service Contributor')
  scope: existingSearchService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7ca78c08-252a-4471-8644-bb5ff32d4ba0') // Search Service Contributor
    principalId: toolsearchGateway.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource clawChannels 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'claw-channels'
  location: location
  tags: union(tags, { 'azd-service-name': 'claw-channels' })
  identity: {
    type: 'SystemAssigned, UserAssigned'
    userAssignedIdentities: { '${acrPullIdentity.id}': {} }
  }
  properties: {
    managedEnvironmentId: containerAppsEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
      }
      registries: [{ server: containerRegistryLoginServer, identity: acrPullIdentity.id }]
    }
    template: {
      containers: [
        {
          name: 'claw-channels'
          image: seedImage
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
            { name: 'Agent__Provider', value: 'foundry' }
            { name: 'Agent__BaseUrl', value: foundryProjectEndpoint }
            { name: 'Agent__InvocationsPath', value: 'agents/claw-agent/endpoint/protocols/invocations?api-version=v1' }
            { name: 'Agent__TokenResource', value: 'https://ai.azure.com' }
            { name: 'Slack__BotToken', value: slackBotToken }
            { name: 'Slack__AppToken', value: slackAppToken }
            { name: 'Slack__SigningSecret', value: slackSigningSecret }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
  dependsOn: [toolsearchGateway]
}

// RBAC: grant claw-channels system identity permission to invoke Foundry Hosted Agent endpoint
// Role: Azure AI Developer (64702f94-c441-49e6-a78b-ef80e0188fee) on the Foundry project
resource clawChannelsFoundryRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(foundryProjectResourceId) && !empty(foundryAccountName)) {
  name: guid(foundryProjectResourceId, 'claw-channels', 'AzureAIDeveloper')
  scope: existingFoundryProject
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '64702f94-c441-49e6-a78b-ef80e0188fee') // Azure AI Developer
    principalId: clawChannels.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource existingFoundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' existing = if (!empty(foundryProjectResourceId) && !empty(foundryAccountName)) {
  name: '${foundryAccountName}/${last(split(foundryProjectResourceId, '/'))}'
}

output clawChannelsUrl string = 'https://${clawChannels.properties.configuration.ingress.fqdn}'
output coffeeshopMcpUrl string = 'https://${coffeeshopMcp.properties.configuration.ingress.fqdn}'
output toolsearchGatewayUrl string = 'https://${toolsearchGateway.properties.configuration.ingress.fqdn}'
