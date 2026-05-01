# MCP in the enterprise AI integration

## MCP Traditional Flow

```mermaid
sequenceDiagram
    participant LLM
    participant Client
    participant Server

    Note over Client,Server: Discovery
    Client->>Server: tools/list
    Server-->>Client: List of tools

    Note over Client,LLM: Tool Selection
    LLM->>Client: Select tool to use

    Note over Client,Server: Invocation
    Client->>Server: tools/call
    Server-->>Client: Tool result
    Client->>LLM: Process result

    Note over Client,Server: Updates
    Server--)Client: tools/list_changed
    Client->>Server: tools/list
    Server-->>Client: Updated tools
```

## MCP problems
- Performance and Practical Limitations
  - **Context Window Bloat**: Active MCP servers consume large amounts of tokens to describe their capabilities to the LLM, reducing performance and increasing costs.
  - Operational Complexity: Managing multiple, concurrent local servers is difficult, often leading to debugging challenges and high "context rot".
- Security Vulnerabilities
  - Data Leakage/Exfiltration: Malicious or poorly configured servers can steal or leak sensitive data to third parties.
  - Remote Code Execution (RCE): Unauthenticated users can run arbitrary commands on machines hosting improperly secured MCP servers.
  - Tool Poisoning/Injection: Malicious instructions can be hidden in tool descriptions, manipulating AI agents into taking unauthorized actions (e.g., reading/exfiltrating private files).
  - Excessive Permissions/Credential Theft: MCP servers often access more data than necessary and can steal API keys or passwords.
  - Broken Authentication/Authorization: The protocol lacks strong, built-in security standards, relying on often-weak, inconsistent implementations.
- You name it

## Context reduction strategy

- **Dynamic tool search**: 2 tools - search, call (*)
- Command-line interfaces: OpenClaw (MCPorter), Moltworker
- Client-side Code Mode
  - https://block.github.io/goose/blog/2025/12/15/code-mode-mcp/
  - https://platform.claude.com/docs/en/agents-and-tools/tool-use/programmatic-tool-calling
- **Server-side Code Mode**: 3 tools - search, get_schema and execute(code) (*)

## 1) OpenAPI Specs Load at Startup

```mermaid
sequenceDiagram
    autonumber
    participant Host as ASP.NET Host Startup
    participant Program as Program.cs
    participant Builder as OpenApiToolCatalogBuilder
    participant Loader as OpenApiDocumentLoader
    participant Reader as OpenApiStreamReader
    participant Registry as ToolRegistry/DI

    Host->>Program: Build web app
    Program->>Builder: ResolveSources(configuration, baseDirectory)
    Program->>Builder: BuildAsync(openApiSources)

    Builder->>Builder: LoadSourceDocumentsAsync(sources)
    loop each source
        Builder->>Loader: LoadAsync(source)
        alt remote URL
            Loader->>Loader: HttpClient.GetStreamAsync(url)
        else local path
            Loader->>Loader: File.OpenRead(path)
        end
        Loader->>Reader: Read(stream, out diagnostic)
        Reader-->>Loader: OpenApiDocument
        Loader-->>Builder: OpenApiSourceDocument
    end

    Builder->>Builder: BuildToolsFromLoadedSources(...)
    Builder->>Builder: ExtractBaseUrlsFromLoadedSources(...)
    Builder-->>Program: (openApiTools, codeModeBaseUrls)

    Program->>Registry: register IToolRegistry(new ToolRegistry(tools))
    Program-->>Host: startup continues and host run begins
```

## 2) Tool-Search-Tool Calling OpenAPI

```mermaid
sequenceDiagram
    autonumber
    participant LLM as MCP Client/LLM
    participant SearchHandler as ToolSearchHandlers.search_tools
    participant Meta as MetaTools
    participant Searcher as WeightedToolSearcher
    participant Reg as ToolRegistry
    participant CallHandler as ToolSearchHandlers.call_tool
    participant OpenApiInvoker as OpenApiRequestInvoker
    participant Api as OpenAPI Backend

    LLM->>SearchHandler: search_tools(query, limit)
    SearchHandler->>Meta: SearchTools(query, limit, context)
    Meta->>Searcher: Search(query, limit, context)
    Searcher->>Reg: GetVisibleTools(context)
    Reg-->>Searcher: visible tools
    Searcher-->>Meta: ranked tools
    Meta-->>SearchHandler: ToolDefinition[]
    SearchHandler-->>LLM: candidate tools + schemas

    LLM->>CallHandler: call_tool(name, arguments)
    CallHandler->>Meta: CallToolAsync(name, arguments, context)
    Meta->>Reg: InvokeAsync(name, arguments, context)
    Reg->>OpenApiInvoker: tool.Handler(arguments, ct)
    OpenApiInvoker->>Api: HTTP request
    Api-->>OpenApiInvoker: HTTP response
    OpenApiInvoker-->>Reg: parsed result
    Reg-->>Meta: result
    Meta-->>CallHandler: result
    CallHandler-->>LLM: final output
```

## 3) Code Mode Calling OpenAPI (If Code Performs HTTP)

```mermaid
sequenceDiagram
    autonumber
    participant LLM as MCP Client/LLM
    participant CM as CodeModeHandlers
    participant Disc as DiscoveryTools
    participant Exec as ExecuteTool
    participant Runner as ISandboxRunner
    participant Local as LocalConstrainedRunner
    participant OS as OpenSandboxRunner
    participant Api as OpenAPI Backend

    LLM->>CM: search(query)
    CM->>Disc: Search(...)
    Disc-->>CM: results
    CM-->>LLM: discovery output

    LLM->>CM: get_schema(toolNames)
    CM->>Disc: GetSchema(...)
    Disc-->>CM: schemas
    CM-->>LLM: schema output

    LLM->>CM: execute(code)
    CM->>Exec: ExecuteAsync(code)
    Exec->>Runner: RunAsync(code)

    alt local runner
        Runner->>Local: RunAsync(code)
        Local->>Api: Python HTTP call (if code performs it)
        Api-->>Local: response
        Local-->>Exec: RunnerResult
    else opensandbox runner
        Runner->>OS: RunAsync(code)
        OS->>Api: Python HTTP call (if code performs it)
        Api-->>OS: response
        OS-->>Exec: RunnerResult
    end

    Exec-->>CM: ExecuteResponse(finalValue)
    CM-->>LLM: finalValue
```

## MCP - roadmap 2026

**MCP Creator Reveals the 2026 Roadmap for AI Agents** by **David Soria Parra**: https://youtu.be/kAVRFYgCPg0?si=467OYBqrZPErmKbj

### Progressive discovery

![](assets/mcp_progressive_discovery.PNG)
Ref: https://github.com/modelcontextprotocol/modelcontextprotocol/issues/1821

### Composability via code

![](assets/mcp_composable_code.PNG)

## References
- https://www.anthropic.com/engineering/code-execution-with-mcp
- https://www.anthropic.com/engineering/advanced-tool-use
- https://blog.cloudflare.com/code-mode-mcp/
- https://blog.cloudflare.com/code-mode/
- https://nx.dev/blog/why-we-deleted-most-of-our-mcp-tools
- https://docs.spring.io/spring-ai/reference/2.0/guides/dynamic-tool-search.html
