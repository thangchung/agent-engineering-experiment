using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using AGUI.Abstractions;
using AGUI.Client;
using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;
using Microsoft.JSInterop;
using WebApp.Models;

namespace WebApp;

public sealed record EventEntry(string Type, string Detail);

/// <summary>Per-circuit app state: the guest id, the one UIAgent every page shares, the cart and the AG-UI event log.</summary>
public sealed partial class OrderSession : IDisposable
{
    private const string GuestKey = "cs.guest";
    private const int MaxEvents = 300;

    private readonly IHttpClientFactory _http;
    private readonly IJSRuntime _js;
    private Dictionary<string, CatalogItem>? _menu;

    public OrderSession(IHttpClientFactory http, IJSRuntime js)
    {
        _http = http;
        _js = js;
        Agent = CreateAgent();
    }

    public event Action? Changed;

    public string CustomerId { get; private set; } = "";

    public UIAgent<OrderUiState> Agent { get; private set; }

    public Dictionary<string, int> Cart { get; } = [];

    public void AddToCart(string id, int delta)
    {
        Cart[id] = Math.Clamp(Cart.GetValueOrDefault(id) + delta, 0, 20);
        Changed?.Invoke();
    }

    public void ClearCart()
    {
        Cart.Clear();
        Changed?.Invoke();
    }

    public List<EventEntry> Events { get; } = [];

    public bool ShowEvents { get; set; }

    public async Task InitAsync()
    {
        if (CustomerId.Length > 0)
        {
            return;
        }

        var stored = await JsAsync<string?>("cs.get", GuestKey);
        CustomerId = stored is { Length: > 0 } ? stored : await StoreNewGuestAsync();
        Changed?.Invoke();
    }

    public async Task NewGuestAsync()
    {
        await StoreNewGuestAsync();
        ClearCart();
        Events.Clear();
        Agent = CreateAgent();
        Changed?.Invoke();
    }

    public async Task<IReadOnlyDictionary<string, CatalogItem>> GetMenuAsync()
    {
        if (_menu is null)
        {
            var items = await _http.CreateClient("counter-api").GetFromJsonAsync<List<CatalogItem>>("/menu", Json.Web) ?? [];
            _menu = items.ToDictionary(i => i.Id);
        }

        return _menu;
    }

    public string Display(string id) => _menu is not null && _menu.TryGetValue(id, out var item) ? item.DisplayName : id;

    /// <summary>JS interop that tolerates the circuit going away mid-call (BL0016).</summary>
    public async Task<T?> JsAsync<T>(string function, params object?[] args)
    {
        try
        {
            return await _js.InvokeAsync<T>(function, args);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException or InvalidOperationException)
        {
            return default;
        }
    }

    /// <summary>One order as raw JSON (shown as-is on the details page) and typed; null when it does not exist.</summary>
    public async Task<(OrderRecordInfo Order, string Raw)?> GetOrderAsync(int number)
    {
        using var response = await _http.CreateClient("counter-api").GetAsync($"/orders/{number}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync();
        return (JsonSerializer.Deserialize<OrderRecordInfo>(raw, Json.Web)!, raw);
    }

    public Task<List<OrderRecordInfo>> GetOrdersAsync(bool mine) =>
        _http.CreateClient("counter-api").GetFromJsonAsync<List<OrderRecordInfo>>(
            mine ? $"/orders?customerId={Uri.EscapeDataString(CustomerId)}" : "/orders", Json.Web)!;

    public void Dispose() => Agent.Dispose();

    private async Task<string> StoreNewGuestAsync()
    {
        CustomerId = "guest-" + Guid.NewGuid().ToString("N")[..8];
        await JsAsync<object>("cs.set", GuestKey, CustomerId);
        return CustomerId;
    }

    private UIAgent<OrderUiState> CreateAgent()
    {
        var threadId = Guid.NewGuid().ToString("N");
        var chat = new TapChatClient(
            new AGUIChatClient(new AGUIChatClientOptions(_http.CreateClient("agui"), "/agui")), Log);

        UIAgent<OrderUiState>? agent = null;
        agent = new UIAgent<OrderUiState>(chat, options =>
        {
            options.AddGeneratedToolBlocks();
            options.ChatOptions = new ChatOptions
            {
                // Only the 3 fields the server reads go back; everything else in the state is server output.
                RawRepresentationFactory = _ => new RunAgentInput
                {
                    ThreadId = threadId,
                    State = JsonSerializer.SerializeToElement(
                        new { customerId = CustomerId, asks = agent!.State.Value.Asks, history = agent.State.Value.History }, Json.Web),
                },
            };
            options.StateMapper = ctx =>
            {
                if (ctx.Update.RawRepresentation is StateSnapshotEvent snapshot &&
                    snapshot.Snapshot.Deserialize<OrderUiState>(Json.Web) is { } state)
                {
                    ctx.SetState(state);
                }
            };
        }, new OrderUiState());
        return agent;
    }

    private void Log(string type, string detail)
    {
        if (Events.Count >= MaxEvents)
        {
            Events.RemoveAt(0);
        }

        Events.Add(new EventEntry(type, detail));
        Changed?.Invoke();
    }

    [GeneratedRegex("(?<=.)([A-Z])")]
    private static partial Regex Words();

    private sealed class TapChatClient(IChatClient inner, Action<string, string> log) : DelegatingChatClient(inner)
    {
        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                if (update.RawRepresentation is BaseEvent evt)
                {
                    var name = evt.GetType().Name.Replace("Event", "", StringComparison.Ordinal);
                    var detail = evt is StateSnapshotEvent snapshot ? snapshot.Snapshot.GetRawText() : update.Text;
                    log(Words().Replace(name, "_$1").ToUpperInvariant(), detail.Length > 220 ? detail[..220] + "…" : detail);
                }

                yield return update;
            }
        }
    }
}
