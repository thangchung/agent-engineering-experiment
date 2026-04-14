# MCP Experiments Sequence Diagrams

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
