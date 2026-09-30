using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Codefinity.EventEmitter.Dispatch;

/// <summary>
/// The queue between publishers and the background workers. A publication is reserved from the moment it is
/// created until a worker has finished with it, so it can never be queued twice (for example by a resubmit
/// while it is still waiting for its transaction to commit).
/// </summary>
internal sealed class AsyncEventDispatcher
{
    private readonly Channel<EventPublication> _queue = Channel.CreateUnbounded<EventPublication>();
    private readonly ConcurrentDictionary<Guid, byte> _reserved = new();

    public ChannelReader<EventPublication> Reader => _queue.Reader;

    public bool TryReserve(Guid id) => _reserved.TryAdd(id, 0);

    public void Release(Guid id) => _reserved.TryRemove(id, out _);

    /// <summary>Queues a publication already reserved with <see cref="TryReserve"/>. Fails once shutdown has begun.</summary>
    public bool EnqueueReserved(EventPublication publication)
    {
        if (_queue.Writer.TryWrite(publication))
        {
            return true;
        }

        Release(publication.Id);
        return false;
    }

    public bool TryEnqueue(EventPublication publication) =>
        TryReserve(publication.Id) && EnqueueReserved(publication);

    /// <summary>Stops accepting publications; workers finish what is already queued.</summary>
    public void Complete() => _queue.Writer.TryComplete();
}
