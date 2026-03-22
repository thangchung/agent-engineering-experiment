using System.Net.Http.Json;
using System.Web;

namespace McpServer.Brewery;

/// <summary>
/// Typed HTTP client for the Open Brewery DB v1 API.
/// Base URL: https://api.openbrewerydb.org/v1
///
/// Register via DI with AddHttpClient&lt;OpenBreweryDbClient&gt;() and configure
/// the BaseAddress to the API root. All methods accept a CancellationToken for
/// cooperative cancellation.
/// </summary>
public sealed class OpenBreweryDbClient(HttpClient http)
{
    /// <summary>
    /// GET /breweries/{obdb-id}
    /// Returns a single brewery by its Open Brewery DB identifier.
    /// </summary>
    public Task<Brewery?> GetBreweryAsync(string id, CancellationToken ct = default) =>
        http.GetFromJsonAsync<Brewery>($"breweries/{Uri.EscapeDataString(id)}", ct);

    /// <summary>
    /// GET /breweries
    /// Returns a list of breweries with optional filters and pagination.
    /// </summary>
    public Task<Brewery[]?> ListBreweriesAsync(BreweryListQuery query, CancellationToken ct = default)
    {
        string url = BuildListUrl("breweries", query);
        return http.GetFromJsonAsync<Brewery[]>(url, ct);
    }

    /// <summary>
    /// GET /breweries/random
    /// Returns one or more random breweries.
    /// When size is null or 1, the API returns a single brewery object wrapped in an array.
    /// </summary>
    /// <param name="size">Number of random breweries (1–50). Default is 1.</param>
    public Task<Brewery[]?> GetRandomBreweriesAsync(int? size = null, CancellationToken ct = default)
    {
        // The API returns a single object or an array depending on size.
        // We always request as an array (size >= 1) to keep the return type consistent.
        string url = size is null ? "breweries/random" : $"breweries/random?size={size}";
        return http.GetFromJsonAsync<Brewery[]>(url, ct);
    }

    /// <summary>
    /// GET /breweries/search?query={q}
    /// Full-text search against brewery names. Supports partial, case-insensitive matches.
    /// Returns an empty array when no results match.
    /// </summary>
    /// <param name="query">Search term (required).</param>
    /// <param name="page">Page number for pagination. Default 1.</param>
    /// <param name="perPage">Results per page. Default 50, maximum 200.</param>
    public Task<Brewery[]?> SearchBreweriesAsync(
        string query,
        int? page    = null,
        int? perPage = null,
        CancellationToken ct = default)
    {
        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["query"] = query;
        if (page is not null)    qs["page"]     = page.ToString();
        if (perPage is not null) qs["per_page"] = perPage.ToString();
        return http.GetFromJsonAsync<Brewery[]>($"breweries/search?{qs}", ct);
    }

    /// <summary>
    /// GET /breweries/meta
    /// Returns metadata (total, page, per_page) for the brewery dataset.
    /// Accepts the same filters as ListBreweriesAsync.
    /// </summary>
    public Task<BreweryMeta?> GetMetaAsync(BreweryListQuery query, CancellationToken ct = default)
    {
        string url = BuildListUrl("breweries/meta", query);
        return http.GetFromJsonAsync<BreweryMeta>(url, ct);
    }

    // Builds a query string URL from a BreweryListQuery, omitting null fields.
    private static string BuildListUrl(string path, BreweryListQuery q)
    {
        var qs = HttpUtility.ParseQueryString(string.Empty);
        if (q.ByCity    is not null) qs["by_city"]    = q.ByCity;
        if (q.ByCountry is not null) qs["by_country"] = q.ByCountry;
        if (q.ByDist    is not null) qs["by_dist"]    = q.ByDist;
        if (q.ByIds     is not null) qs["by_ids"]     = q.ByIds;
        if (q.ByName    is not null) qs["by_name"]    = q.ByName;
        if (q.ByState   is not null) qs["by_state"]   = q.ByState;
        if (q.ByPostal  is not null) qs["by_postal"]  = q.ByPostal;
        if (q.ByType    is not null) qs["by_type"]    = q.ByType;
        if (q.Page      is not null) qs["page"]       = q.Page.ToString();
        if (q.PerPage   is not null) qs["per_page"]   = q.PerPage.ToString();
        if (q.Sort      is not null) qs["sort"]       = q.Sort;

        string suffix = qs.Count > 0 ? $"?{qs}" : string.Empty;
        return $"{path}{suffix}";
    }
}
