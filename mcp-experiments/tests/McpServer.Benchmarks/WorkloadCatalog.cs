namespace McpServer.Benchmarks;

public static class WorkloadCatalog
{
    public static string Get(string workloadId, string style, bool runtimeSupportsHttpGet = false)
    {
        if (!runtimeSupportsHttpGet && style == "b_http_get")
            throw new InvalidOperationException(
                $"Style 'b_http_get' requires runtimeSupportsHttpGet=true. Workload: {workloadId}");

        return (workloadId, style) switch
        {
            ("simple", "a_requests") => SimpleRequestsStyle,
            ("simple", "b_http_get") => SimpleHttpGetStyle,
            ("complex", "a_requests") => ComplexRequestsStyle,
            ("complex", "b_http_get") => ComplexHttpGetStyle,
            _ => throw new ArgumentException($"Unknown workload: {workloadId} / {style}")
        };
    }

    private const string SimpleRequestsStyle = """
        r = requests.get(f"{BASE_URL}/breweries/random", timeout=10)
        if r.status_code >= 400:
            raise Exception(f"HTTP {r.status_code}")
        result = r.text
        """;

    private const string SimpleHttpGetStyle = """
        resp = http_get(f"{BASE_URL}/breweries/random")
        if resp["status"] >= 400:
            raise Exception(f"HTTP {resp['status']}")
        result = resp["body"]
        """;

    private const string ComplexRequestsStyle = """
        import json
        from collections import defaultdict
        
        errors = []
        
        sd_raw = []
        page = 1
        while True:
            r = requests.get(f"{BASE_URL}/breweries", params={"by_city":"san_diego","per_page":200,"page":page}, timeout=10)
            if r.status_code >= 400:
                errors.append(f"SD page {page} failed: HTTP {r.status_code}")
                break
            data = r.json()
            if not data:
                break
            sd_raw.extend(data)
            if len(data) < 200:
                break
            page += 1
        
        sd_seen = {b["id"]: b for b in sd_raw}
        sd = list(sd_seen.values())
        sd_total = len(sd)
        
        type_counts = defaultdict(int)
        for b in sd:
            type_counts[b.get("brewery_type") or "unknown"] += 1
        type_summary = sorted(
            [{"brewery_type": t, "count": c} for t, c in type_counts.items()],
            key=lambda x: (-x["count"], x["brewery_type"]),
        )
        
        city_counts = defaultdict(int)
        for b in sd:
            city_counts[b.get("city") or "Unknown"] += 1
        top_cities = sorted(
            [{"city": c, "count": n} for c, n in city_counts.items()],
            key=lambda x: (-x["count"], x["city"]),
        )[:3]
        
        moon_top_5 = []
        r2 = requests.get(f"{BASE_URL}/breweries/search", params={"query":"moon","per_page":200}, timeout=10)
        if r2.status_code >= 400:
            errors.append(f"moon search failed: HTTP {r2.status_code}")
        else:
            moon = r2.json()
            moon_seen = {b["id"]: b for b in moon}
            moon_sorted = sorted(moon_seen.values(), key=lambda x: x.get("name") or "")
            moon_top_5 = [{"name": b.get("name",""), "city": b.get("city","")} for b in moon_sorted[:5]]
        
        output = {
            "san_diego_total": sd_total,
            "san_diego_type_summary": type_summary,
            "san_diego_top_cities": top_cities,
            "moon_top_5": moon_top_5,
        }
        if errors:
            output["errors"] = errors
        
        result = json.dumps(output, indent=2)
        """;

    private const string ComplexHttpGetStyle = """
        import json
        from collections import defaultdict
        
        errors = []
        
        def fetch_json(url):
            resp = http_get(url)
            if resp["status"] >= 400:
                return None, f"HTTP {resp['status']}"
            body = resp["body"]
            return (json.loads(body) if isinstance(body, str) else body), None
        
        sd_raw = []
        page = 1
        while True:
            data, err = fetch_json(f"{BASE_URL}/breweries?by_city=san_diego&per_page=200&page={page}")
            if err:
                errors.append(f"SD page {page} failed: {err}")
                break
            if not data:
                break
            sd_raw.extend(data)
            if len(data) < 200:
                break
            page += 1
        
        sd_seen = {b["id"]: b for b in sd_raw}
        sd = list(sd_seen.values())
        sd_total = len(sd)
        
        type_counts = defaultdict(int)
        for b in sd:
            type_counts[b.get("brewery_type") or "unknown"] += 1
        type_summary = sorted(
            [{"brewery_type": t, "count": c} for t, c in type_counts.items()],
            key=lambda x: (-x["count"], x["brewery_type"]),
        )
        
        city_counts = defaultdict(int)
        for b in sd:
            city_counts[b.get("city") or "Unknown"] += 1
        top_cities = sorted(
            [{"city": c, "count": n} for c, n in city_counts.items()],
            key=lambda x: (-x["count"], x["city"]),
        )[:3]
        
        moon_top_5 = []
        moon, err = fetch_json(f"{BASE_URL}/breweries/search?query=moon&per_page=200")
        if err:
            errors.append(f"moon search failed: {err}")
        elif moon:
            moon_seen = {b["id"]: b for b in moon}
            moon_sorted = sorted(moon_seen.values(), key=lambda x: x.get("name") or "")
            moon_top_5 = [{"name": b.get("name",""), "city": b.get("city","")} for b in moon_sorted[:5]]
        
        output = {
            "san_diego_total": sd_total,
            "san_diego_type_summary": type_summary,
            "san_diego_top_cities": top_cities,
            "moon_top_5": moon_top_5,
        }
        if errors:
            output["errors"] = errors
        result = json.dumps(output, indent=2)
        """;
}
