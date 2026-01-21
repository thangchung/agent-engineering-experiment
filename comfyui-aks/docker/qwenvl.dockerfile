# Custom ComfyUI API image with QwenVL support
# Base image: comfyui-api with ComfyUI 0.7.0, API 1.16.1, PyTorch 2.8.0, CUDA 12.8
FROM ghcr.io/saladtechnologies/comfyui-api:comfy0.7.0-api1.16.1-torch2.8.0-cuda12.8-runtime

# Set environment variables
ENV WORKFLOW_DIR=/workflows
ENV STARTUP_CHECK_MAX_TRIES=30

# Install ComfyUI-QwenVL custom node
WORKDIR /opt/ComfyUI/custom_nodes
RUN git clone https://github.com/1038lab/ComfyUI-QwenVL && \
    pip install --no-cache-dir -r ComfyUI-QwenVL/requirements.txt

# Optional: Install llama-cpp-python for GGUF support (uncomment if needed)
# RUN pip install --no-cache-dir llama-cpp-python

# Copy workflows into the image
COPY example-workflows/sd1.5 /workflows

# Copy the comfyui-api binary
COPY bin/comfyui-api /app/comfyui-api
RUN chmod +x /app/comfyui-api

# Copy the model download script (downloads models at runtime)
COPY docker/download-models.sh /app/download-models.sh
RUN chmod +x /app/download-models.sh

# Set working directory
WORKDIR /app

# Run the entrypoint script (downloads models then starts API)
CMD ["/app/download-models.sh"]
