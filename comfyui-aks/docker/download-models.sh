#!/bin/bash
set -e

MODELS_DIR="/opt/ComfyUI/models/checkpoints"
mkdir -p "$MODELS_DIR"

# Download dreamshaper_8 if not exists
if [ ! -f "$MODELS_DIR/dreamshaper_8.safetensors" ]; then
  echo "Downloading dreamshaper_8.safetensors..."
  wget -q --show-progress -O "$MODELS_DIR/dreamshaper_8.safetensors" \
    "https://civitai.com/api/download/models/128713?type=Model&format=SafeTensor&size=pruned&fp=fp16"
  echo "✓ dreamshaper_8.safetensors downloaded"
else
  echo "✓ dreamshaper_8.safetensors already exists, skipping download"
fi

# Download dreamshaper5 if not exists
if [ ! -f "$MODELS_DIR/dreamshaper5.safetensors" ]; then
  echo "Downloading dreamshaper5.safetensors..."
  wget -q --show-progress -O "$MODELS_DIR/dreamshaper5.safetensors" \
    "https://huggingface.co/Lykon/DreamShaper/resolve/main/DreamShaper_5_beta2_noVae_half_pruned.safetensors?download=true"
  echo "✓ dreamshaper5.safetensors downloaded"
else
  echo "✓ dreamshaper5.safetensors already exists, skipping download"
fi

echo "All models ready. Starting comfyui-api..."

# Start the API server
exec /app/comfyui-api
