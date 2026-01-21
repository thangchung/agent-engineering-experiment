# Scale ComfyUI on AKS - experiment

> We use Windows 11 - WSL (Ubuntu 24) for building and deploying this stack

## Build the `comfyui-api` execution file

```shell
# Ref: https://github.com/SaladTechnologies/comfyui-api/blob/main/DEVELOPING.md#setting-up-the-development-environment
git clone https://github.com/SaladTechnologies/comfyui-api.git
cd comfyui-api
npm install
npm run build-binary
```

## Dockerize the apps

Ref: [qwenvl.dockerfile](comfyui-aks\docker\qwenvl.dockerfile)

```sh
docker login ghcr.io -u thangchung # this will require you to input your GH token, use classic token with package permission
docker build -t ghcr.io/thangchung/agent-engineering-experiment/comfyui-api:qwenvl-1 -f docker/qwenvl.dockerfile .
docker push ghcr.io/thangchung/agent-engineering-experiment/comfyui-api:qwenvl-1
```

Here it is: [ghcr.io/thangchung/agent-engineering-experiment/comfyui-api:qwenvl-1](https://github.com/thangchung/agent-engineering-experiment/pkgs/container/agent-engineering-experiment%2Fcomfyui-api)

## Run it on AKS

### Setup AKS cluster with GPU in-placed

- Follow the guidance at https://learn.microsoft.com/en-us/azure/aks/use-nvidia-gpu?tabs=add-ubuntu-gpu-node-pool#manually-install-the-nvidia-device-plugin to setup AKS and 

- Update GPU node-pool (Standard_NC4as_T4_v3) - if not do it correct in previous step

```sh
az aks nodepool update \
  --resource-group <your-rg> \
  --cluster-name <your-cluster> \
  --name gpupool \
  --min-count 0 \
  --max-count 3 \
  --enable-cluster-autoscaler #remember to enable this, if not then KEDA will not work
```

### Run normal comfyui-api workload (without austoscale)

```sh
kubectl apply -f comfyui-aks\k8s\comfyui-aks-download.yaml
```

### Run comfyui-api workload with autoscale (saving cost)

- Install KEDA

```sh
helm repo add kedacore https://kedacore.github.io/charts
helm repo update
helm install keda kedacore/keda --namespace keda --create-namespace
```

- Install KEDA HTTP Add-on

```sh
helm install http-add-on kedacore/keda-add-ons-http --namespace keda
```

- Run the workload with autoscale enabled

```sh
kubectl apply -f comfyui-aks\k8s\comfyui-aks-autoscale.yaml
```

- Test the app

```sh
kubectl port-forward svc/keda-add-ons-http-interceptor-proxy -n keda 3000:8080
curl -H "Host: comfyui.local" http://localhost:3000/health # if not use curl then can install ModHeader in the brower to run it
```

or

```sh
curl -X POST http://localhost:3000/workflow/txt2img \
  -H "Content-Type: application/json" \
  -H "Host: comfyui.local" \
  -d @payload.json
```

```ps1
# PowerShell
curl.exe -X POST http://localhost:3000/workflow/txt2img `
  -H "Content-Type: application/json" `
  -H "Host: comfyui.local" `
  -d "@payload.json"
```

```ps1
# PowerShell
$body = Get-Content -Raw payload.json
Invoke-RestMethod -Uri "http://localhost:3000/workflow/txt2img" -Method POST -Headers @{"Host"="comfyui.local"; "Content-Type"="application/json"} -Body $body
```

> Check quote of your Azure subscription: `az vm list-usage --location eastus --query "[?contains(name.localizedValue, 'NC')]" -o table`
