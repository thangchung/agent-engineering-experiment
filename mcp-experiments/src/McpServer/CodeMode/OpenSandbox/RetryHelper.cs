namespace McpServer.CodeMode.OpenSandbox;

/// <summary>
/// Helper for retrying operations with exponential backoff.
/// </summary>
public static class RetryHelper
{
    /// <summary>
    /// Retries an async operation with exponential backoff.
    /// </summary>
    /// <typeparam name="T">Return type of the operation.</typeparam>
    /// <param name="operation">The operation to retry.</param>
    /// <param name="maxAttempts">Maximum number of attempts.</param>
    /// <param name="initialDelay">Initial delay before first retry.</param>
    /// <param name="maxDelay">Maximum delay between retries (caps exponential growth).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the operation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if maxAttempts is not positive.</exception>
    public static async Task<T> RetryAsync<T>(
        Func<Task<T>> operation,
        int maxAttempts = 3,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        CancellationToken cancellationToken = default)
    {
        if (maxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "maxAttempts must be greater than 0.");
        }

        var delay = initialDelay ?? TimeSpan.FromSeconds(1);
        var cap = maxDelay ?? TimeSpan.FromSeconds(8);
        var attempt = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            try
            {
                return await operation();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch when (attempt < maxAttempts)
            {
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, cap.TotalMilliseconds));
            }
        }
    }
}
