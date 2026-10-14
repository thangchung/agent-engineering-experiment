namespace CounterService.Features.Orders.Workflow;

/// <summary>How the Gate asks: one 5-way <c>choice</c> (decisive providers) or four yes/no <c>noul</c> questions (providers that separate those better).</summary>
public enum GateMode
{
    Choice,
    Noul,
}

/// <summary>
/// Per-provider calibration for the decision model (README "Decision model providers"). Every
/// threshold in the policies was tuned on one provider; another one answers with different
/// confidence levels, so cut-offs are data, not constants. A provider that cannot do a job at all
/// switches it off (<see cref="SplitWithModel"/>) instead of getting a threshold that hides it.
/// </summary>
public sealed record DecisionProfile(
    string Name,
    string Model,
    double IntentConfidence,
    double OnMenu,
    double StationReview,
    double StationConfirm,
    bool SplitWithModel = true,
    string Path = "v1/systemone",
    string? ApiKeyHeader = null,
    bool EntraWhenNoKey = false,
    GateMode GateMode = GateMode.Choice)
{
    /// <summary>TypeSafe's hosted decision model: the values the policies were tuned on.</summary>
    public static DecisionProfile Typesafe { get; } = new("typesafe", "jev-latest", 0.5, 0.5, 0.6, 0.85);

    /// <summary>Self-hosted OpenJev: the server the policies were first tuned on (T00 probe), same cut-offs as TypeSafe's. It also
    /// accepts "openjev-latest"; "jev-latest" is the name every earlier run used.</summary>
    public static DecisionProfile OpenJev { get; } = Typesafe with { Name = "openjev" };

    /// <summary>Microsoft-Decision-1 on Microsoft Foundry. <c>DecisionModel:Model</c> is the <b>deployment name</b>, not the model
    /// name. Key goes in the <c>api-key</c> header; with no key configured the counter signs in with Microsoft Entra ID
    /// (<c>DefaultAzureCredential</c>). Intent cut-off 0.7: probed 2026-10-14, clear messages come back at 0.99-1.00 and vague ones ("something sweet" 0.52, "hmm" 0.67) are ambiguous, so they are asked about instead of refused. Other cut-offs are the TypeSafe defaults; calibrate on labeled examples (Microsoft's own advice).</summary>
    public static DecisionProfile Microsoft { get; } = Typesafe with
    {
        Name = "microsoft",
        Model = "Microsoft-Decision-1",
        IntentConfidence = 0.7,
        Path = "providers/microsoft/v1/systemone",
        ApiKeyHeader = "api-key",
        EntraWhenNoKey = true,
    };

    /// <summary>Local laya-serve, "english" checkpoint, measured 2026-10: argmax confidences sit at 0.1-0.3, and it routes
    /// every item to the barista, so the station split uses the catalog instead. Its 5-way choice collapses to "other", so the
    /// Gate asks four yes/no questions (<see cref="IntentPolicy.FromNouls"/>); on-menu is ignored (0.0, it does not separate: pizza 0.77),
    /// the extract step already drops items that are not on the menu.</summary>
    public static DecisionProfile Laya { get; } = new("laya", "english", 0.1, 0.0, 0.6, 0.85, SplitWithModel: false, GateMode: GateMode.Noul);

    /// <summary>Accepts the endpoint however it was copied: the bare host, the host plus the request path this profile already
    /// adds (<c>/v1/systemone</c>), or for Foundry also the bare <c>/providers/microsoft</c> prefix.</summary>
    public string NormalizeBaseUrl(string url)
    {
        url = url.TrimEnd('/');
        foreach (var suffix in new[] { "/" + Path.Trim('/'), "/providers/microsoft" })
        {
            if (url.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && (suffix == "/" + Path.Trim('/') || Path.StartsWith("providers/microsoft/", StringComparison.Ordinal)))
            {
                return url[..^suffix.Length];
            }
        }

        return url;
    }

    /// <summary>Reads <c>DecisionModel:Provider</c> (typesafe | openjev | microsoft | laya | custom), then lets
    /// <c>DecisionModel:Model</c>, <c>DecisionModel:SplitWithModel</c>, <c>DecisionModel:GateMode</c> and <c>DecisionModel:Thresholds:*</c> override it.</summary>
    public static DecisionProfile FromConfiguration(Microsoft.Extensions.Configuration.IConfiguration config)
    {
        var provider = config["DecisionModel:Provider"];
        var baseline = (provider?.Trim().ToLowerInvariant()) switch
        {
            null or "" or "typesafe" or "jev" => Typesafe,
            "laya" => Laya,
            "openjev" => OpenJev,
            "microsoft" => Microsoft,
            "custom" => Typesafe with { Name = "custom" },
            _ => throw new InvalidOperationException($"Unknown DecisionModel:Provider '{provider}'. Use typesafe, openjev, microsoft, laya or custom."),
        };

        double T(string key, double fallback) => double.TryParse(config[$"DecisionModel:Thresholds:{key}"], System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

        return baseline with
        {
            Model = config["DecisionModel:Model"] is { Length: > 0 } m ? m : baseline.Model,
            IntentConfidence = T("IntentConfidence", baseline.IntentConfidence),
            OnMenu = T("OnMenu", baseline.OnMenu),
            StationReview = T("StationReview", baseline.StationReview),
            StationConfirm = T("StationConfirm", baseline.StationConfirm),
            SplitWithModel = bool.TryParse(config["DecisionModel:SplitWithModel"], out var s) ? s : baseline.SplitWithModel,
            GateMode = Enum.TryParse<GateMode>(config["DecisionModel:GateMode"], ignoreCase: true, out var g) ? g : baseline.GateMode,
        };
    }
}
