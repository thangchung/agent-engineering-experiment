I am checking the current code-mode constraints first so the suggested prompts actually trigger `execute` instead of collapsing back to a single direct tool call.

Read [](file:///Users/chungt02/source_codes/oss/agent-engineering-experiment/mcp-experiments/src/McpServer/CodeMode/LocalConstrainedRunner.cs)

Searched for regex `get_execute_syntax|execute syntax|requests-compatible|call_tool\(|Do NOT use` (`**/src/McpServer/**/*.cs`), 14 results

Read [](file:///Users/chungt02/source_codes/oss/agent-engineering-experiment/mcp-experiments/src/TestWeb/Components/Pages/Chat.razor)

Read [](file:///Users/chungt02/source_codes/oss/agent-engineering-experiment/mcp-experiments/src/McpServer/CodeMode/ExecutionModels.cs)

Given the current implementation, prompts that use code mode need to push the model toward Python analysis, not just single-tool invocation.

Why:
- `execute` is isolated Python in this repo, not tool-bridging Python.
- Inside execute, it cannot call `search_tools`, `call_tool`, `search`, `get_schema`, or `execute`.
- The runner allows pure Python plus a lightweight `requests`-compatible shim.
- References:
LocalConstrainedRunner.cs
LocalConstrainedRunner.cs
McpToolHandlers.cs
CopilotChatService.cs

So the best prompts are ones that require:
- multi-page aggregation
- sorting/ranking
- bucketing/grouping
- derived statistics
- custom output formatting
- HTTP fetches from the public brewery API inside Python

Here are good code-mode prompts for your sample list.

**Recommended Code-Mode Prompts**
1. Use code mode to fetch brewery data for the first 5 pages of California breweries and return the top 10 cities by brewery count.

2. Use code mode and Python to fetch breweries matching `moon` across multiple pages, deduplicate by brewery id, and return the top 5 cities with the most matches.

3. Use code mode to fetch breweries from Texas and compute a breakdown by brewery type as percentages sorted descending.

4. Use code mode to fetch the first 10 pages of breweries and return the 15 most common website domains.

5. Use code mode to find breweries in San Diego, group them by type, and return counts plus one example brewery name per type.

6. Use code mode to fetch breweries for New York and return the average number of breweries per city across the first 5 pages.

7. Use code mode to fetch breweries from multiple states: California, Oregon, and Washington, then compare total counts and return the state with the highest share of micro breweries.

8. Use code mode to fetch breweries across the first 8 pages and return breweries whose names contain `ale`, grouped by state and sorted by group size.

9. Use code mode to sample 25 random breweries, count how many have websites, and return the website coverage rate plus the list of missing-site breweries.

10. Use code mode to fetch breweries in Colorado and return the top 20 postal code prefixes by brewery count.

**Prompts That Strongly Encourage `execute`**
These are more explicit, so they are more likely to trigger code mode in your current setup.

1. Use `get_execute_syntax` and `execute` to write Python that fetches the first 5 pages of California breweries and returns the top 10 cities by count.

2. Use code mode only. Write Python to call the public Open Brewery DB over HTTP, aggregate breweries across pages, and return counts by brewery type for Oregon.

3. Use `execute` with Python and the requests-compatible shim to fetch breweries with `moon` in the name, deduplicate results, and return top 5 as `name`, `city`, `state`.

4. Use code mode to query brewery data over HTTP and compute the percentage of breweries missing website URLs across the first 10 pages.

5. Use code mode and Python to fetch breweries for Texas, Arizona, and New Mexico, then compare totals and return a ranked table.

**Best Replacements For Your Existing Sample Prompts**
If you want a code-mode section next to the current list, these fit well:

- Code Mode (Ranking): use code mode to fetch the first 5 pages of California breweries and return the top 10 cities by brewery count
- Code Mode (Aggregation): use code mode to compute brewery type percentages for Texas across multiple pages
- Code Mode (Filtering): use code mode to search breweries with `moon` in the name, deduplicate by id, and return top 5 as name and city
- Code Mode (Comparison): use code mode to compare brewery totals across California, Oregon, and Washington
- Code Mode (Data Quality): use code mode to measure how many breweries are missing website URLs across the first 10 pages
- Code Mode (Domains): use code mode to extract and rank the most common website domains from brewery results

**What To Avoid**
These are unlikely to use code mode because a direct tool call is cheaper:
- `get random brewery`
- `show full json schema for brewery_random`
- `what tools are available?`
- `show concise parameter schema for getSingleBrewery and listBreweries`

These may use normal multi-step tool calls instead of execute unless you explicitly ask for Python analysis:
- `find breweries in San Diego and return only their names and types`
- `try calling getSingleBrewery with an invalid ID and explain the error`

If you want, I can turn this into a ready-to-paste `Code Mode` sample prompt section for Chat.razor and README.md.