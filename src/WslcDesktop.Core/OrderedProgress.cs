namespace WslcDesktop.Core;

/// <summary>
/// Like <see cref="Progress{T}"/>, but reports in order without a synchronization context: it runs the handler
/// inline instead of on the thread pool, where reports could interleave and arrive out of order.
/// With a context (the UI thread), it posts to it like <see cref="Progress{T}"/>.
/// </summary>
internal sealed class OrderedProgress<T>(Action<T> handler) : IProgress<T>
{
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;

    public void Report(T value)
    {
        if (_context is null)
        {
            handler(value);
        }
        else
        {
            _context.Post(static state => ((Action)state!)(), () => handler(value));
        }
    }
}
