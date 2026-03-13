# coffeeshop-cli

```sh
dotnet run --project src/CoffeeshopCli/CoffeeshopCli.csproj -- models list --json

dotnet run --project src/CoffeeshopCli/CoffeeshopCli.csproj -- skills list

# Inspector (stdio MCP server mode)
npx @modelcontextprotocol/inspector dotnet run --project src/CoffeeshopCli/CoffeeshopCli.csproj -- mcp serve

# Inspector against HTTP MCP bridge
# 1) Start bridge in one terminal:
Hosting__EnableHttpMcpBridge=true dotnet run --project src/CoffeeshopCli/CoffeeshopCli.csproj
# 2) In another terminal, connect inspector via HTTP transport:
npx @modelcontextprotocol/inspector --transport http --server-url http://127.0.0.1:8080/mcp
```