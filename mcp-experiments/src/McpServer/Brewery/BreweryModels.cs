using System.Text.Json.Serialization;

namespace McpServer.Brewery;

/// <summary>
/// A single brewery record as returned by the Open Brewery DB API.
/// Field names match the API's snake_case JSON property names.
/// </summary>
public sealed record Brewery(
    [property: JsonPropertyName("id")]          string? Id,
    [property: JsonPropertyName("name")]         string? Name,
    [property: JsonPropertyName("brewery_type")] string? BreweryType,
    [property: JsonPropertyName("address_1")]    string? Address1,
    [property: JsonPropertyName("address_2")]    string? Address2,
    [property: JsonPropertyName("address_3")]    string? Address3,
    [property: JsonPropertyName("city")]         string? City,
    [property: JsonPropertyName("state_province")] string? StateProvince,
    [property: JsonPropertyName("postal_code")]  string? PostalCode,
    [property: JsonPropertyName("country")]      string? Country,
    [property: JsonPropertyName("longitude")]    double? Longitude,
    [property: JsonPropertyName("latitude")]     double? Latitude,
    [property: JsonPropertyName("phone")]        string? Phone,
    [property: JsonPropertyName("website_url")]  string? WebsiteUrl,
    [property: JsonPropertyName("state")]        string? State,
    [property: JsonPropertyName("street")]       string? Street);

/// <summary>
/// Metadata returned by the /breweries/meta endpoint.
/// The API returns all fields as strings even though they represent numbers.
/// </summary>
public sealed record BreweryMeta(
    [property: JsonPropertyName("total")]    string? Total,
    [property: JsonPropertyName("page")]     string? Page,
    [property: JsonPropertyName("per_page")] string? PerPage);

/// <summary>
/// Query parameters shared by the List Breweries and Metadata endpoints.
/// Null values are omitted from the request URL.
/// </summary>
public sealed record BreweryListQuery(
    string? ByCity     = null,
    string? ByCountry  = null,
    string? ByDist     = null,
    string? ByIds      = null,
    string? ByName     = null,
    string? ByState    = null,
    string? ByPostal   = null,
    string? ByType     = null,
    int?    Page       = null,
    int?    PerPage    = null,
    string? Sort       = null);
