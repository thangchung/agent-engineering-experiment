"""Create Azure AI Search index and knowledge base for coffeeshop.

Uses REST API directly — no azure-search-documents SDK version dependency.
Run after `azd provision` to seed the knowledge base used by the
ToolSearch.Gateway's `knowledge_lookup` tool.

Usage:
    pip install azure-identity httpx python-dotenv
    python infra/create-search-indexes.py

Requires environment variables (auto-set by `azd env get-values`):
    AZURE_AI_SEARCH_SERVICE_ENDPOINT  — https://<name>.search.windows.net
    AZURE_OPENAI_ENDPOINT             — OpenAI endpoint for vectorization
    AZURE_AI_MODEL_DEPLOYMENT_NAME    — Model deployment name (e.g. gpt-5.4-mini)
    AZURE_TENANT_ID                   — Azure tenant ID
"""

import asyncio
import json
import os
import time
from pathlib import Path

import httpx
from azure.identity import AzureDeveloperCliCredential
from dotenv import load_dotenv

load_dotenv(dotenv_path=".env", override=True)

INDEX_NAME = "coffeeshop-info"
KB_NAME = "coffeeshop-kb"
SEARCH_API_STABLE = "2024-07-01"
SEARCH_API_PREVIEW = "2025-11-01-preview"
SEARCH_SCOPE = "https://search.azure.com/.default"


def get_token(credential) -> str:
    token = credential.get_token(SEARCH_SCOPE)
    return token.token


def _build_index_schema(openai_endpoint: str, embedding_deployment: str) -> dict:
    schema: dict = {
        "name": INDEX_NAME,
        "fields": [
            {"name": "id", "type": "Edm.String", "key": True, "filterable": True},
            {"name": "snippet", "type": "Edm.String", "searchable": True, "retrievable": True},
            {"name": "category", "type": "Edm.String", "filterable": True, "facetable": True},
            {"name": "uid", "type": "Edm.String", "retrievable": True},
            {"name": "blob_path", "type": "Edm.String", "retrievable": True},
            {"name": "snippet_parent_id", "type": "Edm.String", "retrievable": True},
        ],
        "semantic": {
            "configurations": [
                {
                    "name": "semantic-configuration",
                    "prioritizedFields": {
                        "prioritizedContentFields": [{"fieldName": "snippet"}],
                    },
                }
            ]
        },
    }
    # Only add vectorSearch when an embedding model deployment is explicitly configured
    embedding_models = {"text-embedding-ada-002", "text-embedding-3-large", "text-embedding-3-small"}
    if openai_endpoint and embedding_deployment and embedding_deployment in embedding_models:
        schema["vectorSearch"] = {
            "algorithms": [{"name": "hnsw", "kind": "hnsw"}],
            "vectorizers": [
                {
                    "name": "openai-vectorizer",
                    "kind": "azureOpenAI",
                    "azureOpenAIParameters": {
                        "resourceUri": openai_endpoint,
                        "deploymentId": embedding_deployment,
                        "modelName": embedding_deployment,
                    },
                }
            ],
            "profiles": [{"name": "vector-profile", "algorithm": "hnsw", "vectorizer": "openai-vectorizer"}],
        }
    return schema


def create_index(endpoint: str, token: str, openai_endpoint: str, model_deployment: str) -> None:
    schema = _build_index_schema(openai_endpoint, model_deployment)
    url = f"{endpoint}/indexes/{INDEX_NAME}?api-version={SEARCH_API_STABLE}&allowIndexDowntime=true"
    headers = {"Authorization": f"Bearer {token}", "Content-Type": "application/json"}
    resp = httpx.put(url, headers=headers, json=schema, timeout=60)
    if resp.status_code in (200, 201, 204):
        print(f"Index '{INDEX_NAME}' created/updated.")
    else:
        raise RuntimeError(f"Index creation failed {resp.status_code}: {resp.text}")


def upload_sample_data(endpoint: str, token: str) -> None:
    data_file = Path("data/index-data/coffeeshop-exported.jsonl")
    if not data_file.exists():
        print(f"No data file at {data_file} — skipping document upload.")
        return

    docs = []
    for line in data_file.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line:
            docs.append(json.loads(line))

    if not docs:
        return

    url = f"{endpoint}/indexes/{INDEX_NAME}/docs/index?api-version={SEARCH_API_STABLE}"
    headers = {"Authorization": f"Bearer {token}", "Content-Type": "application/json"}
    body = {"value": [{"@search.action": "mergeOrUpload", **d} for d in docs]}
    resp = httpx.post(url, headers=headers, json=body, timeout=60)
    if resp.status_code in (200, 207):
        print(f"Uploaded {len(docs)} documents to '{INDEX_NAME}'.")
    else:
        raise RuntimeError(f"Document upload failed {resp.status_code}: {resp.text}")


def create_knowledge_source(endpoint: str, token: str) -> None:
    url = f"{endpoint}/knowledgesources/{INDEX_NAME}?api-version={SEARCH_API_PREVIEW}"
    headers = {"Authorization": f"Bearer {token}", "Content-Type": "application/json"}
    body = {
        "name": INDEX_NAME,
        "kind": "searchIndex",
        "description": "Coffeeshop company info: FAQ, store hours, loyalty program, policies.",
        "searchIndexParameters": {
            "searchIndexName": INDEX_NAME,
            "sourceDataFields": [
                {"name": "uid"}, {"name": "snippet"},
                {"name": "blob_path"}, {"name": "snippet_parent_id"},
            ],
            "searchFields": [{"name": "snippet"}],
            "semanticConfigurationName": "semantic-configuration",
        },
    }
    resp = httpx.put(url, headers=headers, json=body, timeout=60)
    if resp.status_code in (200, 201, 204):
        print(f"Knowledge source '{INDEX_NAME}' created/updated.")
    else:
        print(f"Warning: knowledge source creation returned {resp.status_code}: {resp.text[:300]}")


def create_knowledge_base(endpoint: str, token: str) -> None:
    # Check if already exists
    check_url = f"{endpoint}/knowledgebases/{KB_NAME}?api-version={SEARCH_API_PREVIEW}"
    headers = {"Authorization": f"Bearer {token}", "Content-Type": "application/json"}
    check = httpx.get(check_url, headers=headers, timeout=30)
    if check.status_code == 200:
        existing = check.json()
        # Update if retrievalReasoningEffort not set (required for intents-based retrieval)
        if not existing.get("retrievalReasoningEffort"):
            print(f"Knowledge base '{KB_NAME}' exists but missing retrievalReasoningEffort — updating...")
        else:
            print(f"Knowledge base '{KB_NAME}' already exists — skipping.")
            return

    url = f"{endpoint}/knowledgebases/{KB_NAME}?api-version={SEARCH_API_PREVIEW}"
    body = {
        "name": KB_NAME,
        "description": "Coffeeshop company knowledge base for natural language Q&A.",
        "knowledgeSources": [{"name": INDEX_NAME}],
        "outputMode": "extractiveData",
        # required for intents-based retrieval without a model deployment
        "retrievalReasoningEffort": {"kind": "minimal"},
    }
    resp = httpx.put(url, headers=headers, json=body, timeout=60)
    if resp.status_code in (200, 201, 204):
        print(f"Knowledge base '{KB_NAME}' created/updated.")
    else:
        print(f"Warning: knowledge base creation returned {resp.status_code}: {resp.text[:300]}")
        print("  The knowledge base API may not be available in this region/tier yet.")


def wait_for_rbac(endpoint: str, credential, retries: int = 6, delay: int = 20) -> str:
    """Retry token acquisition + index list until RBAC propagates."""
    for attempt in range(1, retries + 1):
        try:
            token = get_token(credential)
            resp = httpx.get(
                f"{endpoint}/indexes?api-version={SEARCH_API_STABLE}&$select=name",
                headers={"Authorization": f"Bearer {token}"},
                timeout=15,
            )
            if resp.status_code == 200:
                return token
            if resp.status_code == 403 and attempt < retries:
                print(f"  RBAC not propagated yet (attempt {attempt}/{retries}), waiting {delay}s...")
                time.sleep(delay)
                continue
            # non-403 error — return token and let subsequent calls surface the real error
            return token
        except Exception as e:
            if attempt < retries:
                print(f"  Auth error (attempt {attempt}/{retries}): {e}. Retrying in {delay}s...")
                time.sleep(delay)
    return get_token(credential)


def main() -> None:
    endpoint = os.environ["AZURE_AI_SEARCH_SERVICE_ENDPOINT"].rstrip("/")
    openai_endpoint = os.environ.get("AZURE_OPENAI_ENDPOINT", "")
    model_deployment = os.environ.get("AZURE_AI_MODEL_DEPLOYMENT_NAME", "gpt-5.4-mini")
    tenant_id = os.environ["AZURE_TENANT_ID"]

    credential = AzureDeveloperCliCredential(tenant_id=tenant_id)

    print(f"Target search endpoint: {endpoint}")
    print("Waiting for RBAC to propagate (this can take up to 2 minutes after provision)...")
    token = wait_for_rbac(endpoint, credential)

    print("Creating search index...")
    create_index(endpoint, token, openai_endpoint, model_deployment)

    print("Uploading sample data...")
    upload_sample_data(endpoint, token)

    print("Creating knowledge source...")
    create_knowledge_source(endpoint, token)

    print("Creating knowledge base...")
    create_knowledge_base(endpoint, token)

    print("Done.")


if __name__ == "__main__":
    main()
