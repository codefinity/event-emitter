using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Codefinity.EventEmitter.Listeners;

/// <summary>
/// All registered listener methods, looked up by the runtime type of a published event.
/// </summary>
internal sealed class ListenerRegistry
{
    private readonly object _gate = new();
    private readonly List<ListenerDescriptor> _descriptors = [];
    private readonly HashSet<Type> _listenerTypes = [];
    private readonly Dictionary<string, ListenerDescriptor> _byId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Type, ListenerDescriptor[]> _byEventType = new();

    public IReadOnlyCollection<Type> ListenerTypes
    {
        get
        {
            lock (_gate)
            {
                return _listenerTypes.ToArray();
            }
        }
    }

    /// <summary>Scans and validates <paramref name="listenerType"/>. Returns false if it was already registered.</summary>
    public bool Add(Type listenerType)
    {
        lock (_gate)
        {
            if (_listenerTypes.Contains(listenerType))
            {
                return false;
            }

            var descriptors = ListenerMethodScanner.Scan(listenerType);
            EnsureUniqueIds(listenerType, descriptors);

            _listenerTypes.Add(listenerType);
            foreach (var descriptor in descriptors)
            {
                _descriptors.Add(descriptor);
                _byId.Add(descriptor.Id, descriptor);
            }

            _byEventType.Clear();
            return true;
        }
    }

    public ListenerDescriptor[] GetListeners(Type eventType) => _byEventType.GetOrAdd(eventType, Match);

    public bool TryGetById(string id, [NotNullWhen(true)] out ListenerDescriptor? descriptor)
    {
        lock (_gate)
        {
            return _byId.TryGetValue(id, out descriptor);
        }
    }

    private void EnsureUniqueIds(Type listenerType, IReadOnlyList<ListenerDescriptor> descriptors)
    {
        var seen = new Dictionary<string, ListenerDescriptor>(_byId, StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            if (!seen.TryAdd(descriptor.Id, descriptor))
            {
                var existing = seen[descriptor.Id];
                throw new InvalidOperationException(
                    $"Listener id '{descriptor.Id}' on '{listenerType.FullName}.{descriptor.Method.Name}' is already used by " +
                    $"'{existing.ListenerType.FullName}.{existing.Method.Name}'. Listener ids must be unique.");
            }
        }
    }

    private ListenerDescriptor[] Match(Type eventType)
    {
        lock (_gate)
        {
            return _descriptors.Where(d => d.EventType.IsAssignableFrom(eventType)).ToArray();
        }
    }
}
