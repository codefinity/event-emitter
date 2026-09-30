using System.Collections;

namespace Codefinity.EventEmitter.Testing;

/// <summary>
/// Records every event published in the application, like Spring Modulith's <c>PublishedEvents</c>.
/// Registered by <c>AddEventEmitterTesting()</c>.
/// </summary>
public sealed class PublishedEvents
{
    private readonly object _gate = new();
    private readonly List<object> _events = [];
    private readonly List<Waiter> _waiters = [];

    public IReadOnlyList<object> All
    {
        get
        {
            lock (_gate)
            {
                return _events.ToArray();
            }
        }
    }

    public TypedPublishedEvents<TEvent> OfType<TEvent>() => new(All.OfType<TEvent>().ToArray());

    public void Clear()
    {
        lock (_gate)
        {
            _events.Clear();
        }
    }

    internal int Count
    {
        get
        {
            lock (_gate)
            {
                return _events.Count;
            }
        }
    }

    /// <summary>Called by <see cref="RecordingEventPublisher"/> for every event, before it is published.</summary>
    internal void Record(object evt)
    {
        List<Waiter> matched;
        lock (_gate)
        {
            _events.Add(evt);
            matched = _waiters.Where(w => w.Accepts(evt)).ToList();
            _waiters.RemoveAll(matched.Contains);
        }

        foreach (var waiter in matched)
        {
            waiter.Completion.TrySetResult(evt);
        }
    }

    /// <summary>Waits for an event published at or after position <paramref name="fromIndex"/> that matches.</summary>
    internal async Task<object> WaitForAsync(
        int fromIndex,
        Func<object, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var waiter = new Waiter(predicate);
        lock (_gate)
        {
            for (var i = fromIndex; i < _events.Count; i++)
            {
                if (waiter.Accepts(_events[i]))
                {
                    return _events[i];
                }
            }

            _waiters.Add(waiter);
        }

        try
        {
            return await waiter.Completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _waiters.Remove(waiter);
            }
        }
    }

    private sealed class Waiter(Func<object, bool> predicate)
    {
        public TaskCompletionSource<object> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Accepts(object evt)
        {
            try
            {
                return predicate(evt);
            }
            catch (Exception ex)
            {
                Completion.TrySetException(ex);
                return false;
            }
        }
    }
}

/// <summary>Published events of one type, narrowed with <see cref="Matching"/>.</summary>
public sealed class TypedPublishedEvents<TEvent> : IReadOnlyList<TEvent>
{
    private readonly IReadOnlyList<TEvent> _events;

    internal TypedPublishedEvents(IReadOnlyList<TEvent> events) => _events = events;

    public TypedPublishedEvents<TEvent> Matching(Func<TEvent, bool> predicate) => new(_events.Where(predicate).ToArray());

    public TEvent this[int index] => _events[index];

    public int Count => _events.Count;

    public IEnumerator<TEvent> GetEnumerator() => _events.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
