namespace CoffeeShop.Counter.Application;

/// <summary>
/// Thrown when no customer can be resolved from the supplied identifier
/// (email, C-XXXX customer ID, or ORD-XXXX order ID).
/// Mapped to HTTP 404 with an RFC 9457 problem-details body.
/// </summary>
public sealed class CustomerNotFoundException(string identifier)
    : Exception($"No account found for '{identifier}'. Please provide your email address, customer ID, or an existing order number.")
{
    public string Identifier { get; } = identifier;
}

/// <summary>
/// Thrown when no order can be found by the supplied order ID.
/// Mapped to HTTP 404 with an RFC 9457 problem-details body.
/// </summary>
public sealed class OrderNotFoundException(string orderId)
    : Exception($"No order found with ID '{orderId}'.")
{
    public string OrderId { get; } = orderId;
}

/// <summary>
/// Thrown when a modification or cancellation is attempted on an order that
/// has already passed the modifiable window (status ≥ Preparing).
/// Mapped to HTTP 409 with an RFC 9457 problem-details body.
/// </summary>
public sealed class OrderNotModifiableException(string orderId, string currentStatus)
    : Exception($"Order {orderId} is already '{currentStatus}' and can no longer be changed.")
{
    public string OrderId { get; } = orderId;
    public string CurrentStatus { get; } = currentStatus;
}

/// <summary>
/// Thrown when cancellation is attempted on an order that is already
/// in Preparing, Ready, Completed, or Cancelled state.
/// Mapped to HTTP 409 with an RFC 9457 problem-details body.
/// </summary>
public sealed class OrderNotCancellableException(string orderId, string currentStatus)
    : Exception($"Order {orderId} is already '{currentStatus}' and can no longer be cancelled.")
{
    public string OrderId { get; } = orderId;
    public string CurrentStatus { get; } = currentStatus;
}

/// <summary>
/// Thrown when the product-catalog MCP service is unreachable or times out.
/// Mapped to HTTP 503 with an RFC 9457 problem-details body.
/// </summary>
public sealed class ProductCatalogUnavailableException(string detail)
    : Exception(detail) { }

/// <summary>
/// Thrown when order-placement validation fails (empty order, invalid quantity, etc.).
/// Mapped to HTTP 400 with an RFC 9457 problem-details body.
/// </summary>
public sealed class OrderValidationException(string detail)
    : Exception(detail)
{
    public string Detail { get; } = detail;
}

/// <summary>
/// Thrown when a requested item is not available at order confirmation time.
/// Mapped to HTTP 409 with an RFC 9457 problem-details body.
/// </summary>
public sealed class ItemUnavailableException(string itemType)
    : Exception($"{itemType} is no longer available. Please revise your selection.")
{
    public string ItemType { get; } = itemType;
}
