targetScope = 'resourceGroup'

@description('AI Services account name')
param aiServicesAccountName string

@description('AI project name')
param aiProjectName string

type ConnectionConfig = {
  @description('Name of the connection')
  name: string

  @description('Category of the connection (e.g., ContainerRegistry, AzureStorageAccount, CognitiveSearch)')
  category: string

  @description('Target endpoint or URL for the connection')
  target: string

  @description('Authentication type')
  authType: 'AAD' | 'AccessKey' | 'AccountKey' | 'ApiKey' | 'CustomKeys' | 'ManagedIdentity' | 'None' | 'OAuth2' | 'PAT' | 'SAS' | 'ServicePrincipal' | 'UsernamePassword'

  @description('Whether the connection is shared to all users')
  isSharedToAll: bool?

  @description('Credentials for non-ApiKey authentication types')
  credentials: object?

  @description('Additional metadata for the connection')
  metadata: object?
}

@description('Connection configuration')
param connectionConfig ConnectionConfig

@secure()
@description('API key for ApiKey based connections (optional)')
param apiKey string = ''

resource aiAccount 'Microsoft.CognitiveServices/accounts@2025-04-01-preview' existing = {
  name: aiServicesAccountName

  resource project 'projects' existing = {
    name: aiProjectName
  }
}

resource connection 'Microsoft.CognitiveServices/accounts/projects/connections@2025-04-01-preview' = {
  parent: aiAccount::project
  name: connectionConfig.name
  properties: {
    category: connectionConfig.category
    target: connectionConfig.target
    authType: connectionConfig.authType
    isSharedToAll: connectionConfig.?isSharedToAll ?? true
    credentials: connectionConfig.authType == 'ApiKey' ? {
      key: apiKey
    } : connectionConfig.?credentials
    metadata: connectionConfig.?metadata
  }
}

output connectionName string = connection.name
output connectionId string = connection.id
