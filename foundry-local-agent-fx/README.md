# Foundry Local Agent with Aspire

A .NET 10 + Aspire 13 project demonstrating tool calling with Foundry Local using the `qwen2.5-14b` model.

## Key Features

- **Manual Tool Call Parsing**: Foundry Local/Qwen returns function calls as JSON in `content` field, not standard `tool_calls`. This project handles that.
- **Aspire Orchestration**: Full Aspire 13 setup with dashboard and health checks

## Prerequisites

1. **.NET 10 SDK**
2. **Foundry Local** installed and running:
   ```bash
   foundry service start
   foundry model run qwen2.5-14b
   ```

## Available Tools

| Tool | Description |
|------|-------------|
| `get_weather` | Get current weather for a city |
| `get_datetime` | Get current time in a timezone |
| `get_typical_weather` | Get historical weather for city/date |

## Running

```bash
dotnet run --project AppHost\AppHost.csproj
```

## API Usage

```bash
# Chat endpoint
curl -X POST http://localhost:5000/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "What time is it in Sydney?"}'

# Info endpoint
curl http://localhost:5000/interactive
```

## Why Manual Parsing?

Foundry Local with Qwen models uses non-standard format:

```
# Standard OpenAI format
response.tool_calls = [{"name": "...", "arguments": {...}}]

# Foundry Local format (JSON in content)
response.content = '{"name": "...", "arguments": {...}}'
```

The `ToolCallParser` handles this by extracting JSON from the content string.

