#!/usr/bin/env sh
# Registers claw-api as a Foundry Hosted Agent after azd deploy.
# Runs locally using the caller's own Azure credentials — no managed identity needed.
# Skips automatically when ENABLE_HOSTED_FOUNDRY != "true".

set -e

# Load azd env vars into shell
eval "$(azd env get-values 2>/dev/null | sed 's/^/export /')" 2>/dev/null || true

if [ "${ENABLE_HOSTED_FOUNDRY}" != "true" ]; then
  echo "ENABLE_HOSTED_FOUNDRY is not 'true' — skipping Foundry agent registration."
  exit 0
fi

AGENT_NAME="claw-api"
ACR_NAME="${AZURE_CONTAINER_REGISTRY_NAME}"
ACR_ENDPOINT="${AZURE_CONTAINER_REGISTRY_ENDPOINT}"
AZD_ENV="${AZURE_ENV_NAME}"
FOUNDRY_ENDPOINT="${AZURE_AI_PROJECT_ENDPOINT}"
GATEWAY_URL="${TOOLSEARCH_GATEWAY_URL:-}"
SLACK_BOT="${SLACK_BOT_TOKEN:-}"
SLACK_APP="${SLACK_APP_TOKEN:-}"
SLACK_SIG="${SLACK_SIGNING_SECRET:-}"
MODEL_DEPLOYMENT="${AZURE_AI_MODEL_DEPLOYMENT_NAME:-gpt-4o-mini}"

# Get latest image tag pushed by azd deploy
REPO="foundry-agentfx/${AGENT_NAME}-${AZD_ENV}"
IMAGE_TAG=$(az acr repository show-tags \
  --name "$ACR_NAME" \
  --repository "$REPO" \
  --orderby time_desc --top 1 --output tsv 2>/dev/null || echo "")

if [ -z "$IMAGE_TAG" ]; then
  echo "ERROR: No image found in ACR repo $REPO."
  echo "Run 'azd deploy --service claw-api' first to push the image."
  exit 1
fi

FULL_IMAGE="${ACR_ENDPOINT}/${REPO}:${IMAGE_TAG}"
echo "Registering agent '$AGENT_NAME' with image: $FULL_IMAGE"

export FULL_IMAGE GATEWAY_URL FOUNDRY_ENDPOINT SLACK_BOT SLACK_APP SLACK_SIG MODEL_DEPLOYMENT

# Build JSON definition using stdlib python3 (no pip needed)
python3 -c "
import json, os
d = {
    'definition': {
        'kind': 'hosted',
        'image': os.environ['FULL_IMAGE'],
        'cpu': '1',
        'memory': '2Gi',
        'container_protocol_versions': [
            {'protocol': 'invocations', 'version': '1.0.0'}
        ],
        'environment_variables': {
            'ASPNETCORE_HTTP_PORTS':                               '8080',
            'ASPNETCORE_ENVIRONMENT':                              'Production',
            'AZURE_AI_MODEL_DEPLOYMENT_NAME':                      os.environ.get('MODEL_DEPLOYMENT', 'gpt-4o-mini'),
            'OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT':  'true',
            'Services__ToolSearchGateway__Url':                    os.environ.get('GATEWAY_URL', ''),
            'Toolbox__McpEndpoint':                                os.environ['FOUNDRY_ENDPOINT'] + '/toolboxes/foundry-iq/mcp?api-version=v1',
            'Slack__BotToken':                                     os.environ.get('SLACK_BOT', ''),
            'Slack__AppToken':                                     os.environ.get('SLACK_APP', ''),
            'Slack__SigningSecret':                                os.environ.get('SLACK_SIG', ''),
        }
    }
}
# Remove empty Slack entries
ev = d['definition']['environment_variables']
for k in ['Slack__BotToken', 'Slack__AppToken', 'Slack__SigningSecret']:
    if not ev[k]:
        del ev[k]
json.dump(d, open('/tmp/agent-definition.json', 'w'), indent=2)
print('Agent definition written.')
"

echo "=== Agent definition ==="
cat /tmp/agent-definition.json
echo "========================"

echo "Calling Foundry API..."
API_RESPONSE=$(az rest --method POST \
  --url "${FOUNDRY_ENDPOINT}/agents/${AGENT_NAME}/versions?api-version=2025-11-15-preview" \
  --headers "Content-Type=application/json" "Foundry-Features=HostedAgents=V1Preview,CodeAgents=V1Preview" \
  --body @/tmp/agent-definition.json \
  --resource "https://ai.azure.com" \
  --output json 2>&1) || {
  # Check for unsupported region error
  if echo "$API_RESPONSE" | grep -q "Unsupported region"; then
    echo ""
    echo "WARNING: Foundry Hosted Agents are not available in this region."
    echo "Supported regions: eastus2, westus, westus3, canadacentral, swedencentral, francecentral, norwayeast, australiaeast, etc."
    echo "To use Foundry Hosted Agents, re-provision with a supported region:"
    echo "  azd env set AZURE_LOCATION eastus2"
    echo "  azd down && azd up"
    echo ""
    echo "claw-api Container App is running and accessible at:"
    echo "  $(azd env get-values 2>/dev/null | grep CLAW_API_URL | cut -d= -f2 | tr -d '\"')"
    exit 0
  fi
  echo "ERROR: $API_RESPONSE"
  exit 1
}
echo "$API_RESPONSE"

echo ""
echo "Agent registration submitted!"
echo "Agent endpoint: ${FOUNDRY_ENDPOINT}/agents/${AGENT_NAME}/endpoint/protocols/invocations?api-version=v1"
echo ""
echo "Check status: az rest --method GET --url \"${FOUNDRY_ENDPOINT}/agents/${AGENT_NAME}?api-version=2025-11-15-preview\" --resource https://ai.azure.com --query status -o tsv"
