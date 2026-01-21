# Azure Container Apps Deployment Script for ComfyUI-API (PowerShell)
# Uses Serverless T4 GPU with scale-to-zero

$ErrorActionPreference = "Stop"

# ============================================
# Configuration
# ============================================
$RESOURCE_GROUP = "comfyui-rg"
$LOCATION = "eastus"  # Check T4 availability: https://learn.microsoft.com/en-us/azure/container-apps/gpu-serverless-overview#supported-regions
$ENVIRONMENT_NAME = "comfyui-env"
$APP_NAME = "comfyui-api"
$IMAGE = "ghcr.io/thangchung/agent-engineering-experiment/comfyui-api:qwenvl-1"

Write-Host "🚀 Deploying ComfyUI-API to Azure Container Apps with T4 GPU" -ForegroundColor Cyan
Write-Host ""

# ============================================
# Step 1: Create Resource Group
# ============================================
Write-Host "📦 Creating resource group..." -ForegroundColor Yellow
az group create `
  --name $RESOURCE_GROUP `
  --location $LOCATION `
  --output none

# ============================================
# Step 2: Create Container Apps Environment
# ============================================
Write-Host "🌐 Creating Container Apps environment with workload profiles..." -ForegroundColor Yellow
az containerapp env create `
  --name $ENVIRONMENT_NAME `
  --resource-group $RESOURCE_GROUP `
  --location $LOCATION `
  --enable-workload-profiles `
  --output none

# ============================================
# Step 3: Add GPU Workload Profile (T4)
# ============================================
Write-Host "🎮 Adding T4 GPU workload profile..." -ForegroundColor Yellow
az containerapp env workload-profile add `
  --name $ENVIRONMENT_NAME `
  --resource-group $RESOURCE_GROUP `
  --workload-profile-name gpu-t4 `
  --workload-profile-type Consumption-GPU-NC8as-T4 `
  --output none

# ============================================
# Step 4: Deploy Container App
# ============================================
Write-Host "🐳 Deploying ComfyUI-API container..." -ForegroundColor Yellow
az containerapp create `
  --name $APP_NAME `
  --resource-group $RESOURCE_GROUP `
  --environment $ENVIRONMENT_NAME `
  --workload-profile-name gpu-t4 `
  --image $IMAGE `
  --target-port 3000 `
  --ingress external `
  --cpu 8 `
  --memory 56Gi `
  --min-replicas 0 `
  --max-replicas 3 `
  --scale-rule-name http-requests `
  --scale-rule-type http `
  --scale-rule-http-concurrency 1 `
  --env-vars `
    LOG_LEVEL=debug `
    WORKFLOW_DIR=/workflows `
    STARTUP_CHECK_MAX_TRIES=30

# ============================================
# Step 5: Get App URL
# ============================================
Write-Host ""
Write-Host "✅ Deployment complete!" -ForegroundColor Green
Write-Host ""

$APP_URL = az containerapp show `
  --name $APP_NAME `
  --resource-group $RESOURCE_GROUP `
  --query "properties.configuration.ingress.fqdn" `
  --output tsv

Write-Host "🌐 Application URL: https://$APP_URL" -ForegroundColor Cyan
Write-Host ""
Write-Host "📊 Test endpoints:" -ForegroundColor White
Write-Host "   Health: https://$APP_URL/health"
Write-Host "   API:    https://$APP_URL/"
Write-Host ""
Write-Host "💡 Tips:" -ForegroundColor White
Write-Host "   - First request may take 30s-2min (cold start)"
Write-Host "   - App scales to 0 after idle period (saves cost!)"
Write-Host "   - View logs: az containerapp logs show -n $APP_NAME -g $RESOURCE_GROUP"
Write-Host "   - View metrics: az containerapp show -n $APP_NAME -g $RESOURCE_GROUP"
