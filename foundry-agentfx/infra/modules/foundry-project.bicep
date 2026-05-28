param location string
param tags object
param projectName string
param modelDeploymentName string = 'gpt-5.4-mini'

resource aiHub 'Microsoft.MachineLearningServices/workspaces@2024-10-01' = {
  name: '${projectName}-hub'
  location: location
  tags: tags
  kind: 'Hub'
  identity: { type: 'SystemAssigned' }
  properties: {
    friendlyName: 'foundry-agentfx Hub'
  }
}

resource aiProject 'Microsoft.MachineLearningServices/workspaces@2024-10-01' = {
  name: projectName
  location: location
  tags: tags
  kind: 'Project'
  identity: { type: 'SystemAssigned' }
  properties: {
    friendlyName: 'foundry-agentfx'
    hubResourceId: aiHub.id
  }
}

output projectEndpoint string = 'https://${aiProject.properties.discoveryUrl}'
output projectName string = aiProject.name
