using System.Collections.Concurrent;

namespace NebulaReader.Services;

/// <summary>Conserva aperturas recibidas antes de crear la ventana y las procesa de una en una.</summary>
internal sealed class DocumentOpenQueue(Func<Exception, Task> onError)
{
    private readonly ConcurrentQueue<Func<Task>> _requests = new();
    private readonly SemaphoreSlim _drainLock = new(1, 1);
    private volatile bool _ready;

    public bool HasPendingRequests => !_requests.IsEmpty;
    public void Enqueue(Func<Task> request) => _requests.Enqueue(request);
    public void MarkReady() => _ready = true;

    public async Task DrainAsync()
    {
        if (!_ready || !await _drainLock.WaitAsync(0)) return;
        try
        {
            while (_requests.TryDequeue(out var request))
            {
                try { await request(); }
                catch (Exception ex) { await onError(ex); }
            }
        }
        finally { _drainLock.Release(); }

        // Una petición puede haber llegado mientras terminaba el procesamiento anterior.
        if (HasPendingRequests) await DrainAsync();
    }
}
