param location string
param tags object
param environmentName string
param logAnalyticsWorkspaceId string
param appInsightsConnectionString string
param containerRegistryLoginServer string
param containerRegistryResourceId string
param foundryProjectEndpoint string

@description('Placeholder image used on first deploy before real images are pushed. azd deploy overwrites this.')
param seedImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Model deployment name (alias used by the app)')
param foundryModel string = 'gpt-5.4-mini'

@description('Foundry IQ (AI Search) endpoint — enables knowledge_lookup tool when non-empty')
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

@description('When true, claw-api is deployed as Foundry Hosted Agent instead of Container App.')
param enableHostedFoundry bool = false

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
        external: false
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
            { name: 'Services__CoffeeshopMcp__Url', value: 'https://${coffeeshopMcp.properties.latestRevisionFqdn}' }
            { name: 'FoundryIQ__SearchEndpoint', value: foundryIqEndpoint }
            { name: 'FoundryIQ__KnowledgeBaseName', value: foundryIqKbName }
            { name: 'Toolbox__McpEndpoint', value: toolboxEndpoint }
            { name: 'BraveSearch__ApiKey', value: braveSearchApiKey }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 5 }
    }
  }
  dependsOn: [coffeeshopMcp]
}

resource clawApi 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'claw-api'
  location: location
  tags: union(tags, { 'azd-service-name': 'claw-api' })
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
      secrets: concat(
        !empty(slackBotToken) ? [{ name: 'slack-bot-token', value: slackBotToken }] : [],
        !empty(slackAppToken) ? [{ name: 'slack-app-token', value: slackAppToken }] : [],
        !empty(slackSigningSecret) ? [{ name: 'slack-signing-secret', value: slackSigningSecret }] : []
      )
    }
    template: {
      containers: [
        {
          name: 'claw-api'
          image: seedImage
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat(
            [
              { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
              { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
              { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
              { name: 'OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT', value: 'true' }
              { name: 'Agent__Provider', value: 'foundry' }
              { name: 'Foundry__Endpoint', value: foundryProjectEndpoint }
              { name: 'Foundry__Model', value: foundryModel }
              { name: 'Services__ToolSearchGateway__Url', value: 'https://${toolsearchGateway.properties.latestRevisionFqdn}' }
            ],
            !empty(slackBotToken) ? [{ name: 'Slack__BotToken', secretRef: 'slack-bot-token' }] : [],
            !empty(slackAppToken) ? [{ name: 'Slack__AppToken', secretRef: 'slack-app-token' }] : [],
            !empty(slackSigningSecret) ? [{ name: 'Slack__SigningSecret', secretRef: 'slack-signing-secret' }] : []
          )
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 10
        rules: [{ name: 'http-scaling', http: { metadata: { concurrentRequests: '100' } } }]
      }
    }
  }
  dependsOn: [toolsearchGateway]
}

output clawApiUrl string = 'https://${clawApi.properties.latestRevisionFqdn}'
output coffeeshopMcpUrl string = 'https://${coffeeshopMcp.properties.latestRevisionFqdn}'
output toolsearchGatewayUrl string = 'https://${toolsearchGateway.properties.latestRevisionFqdn}'
