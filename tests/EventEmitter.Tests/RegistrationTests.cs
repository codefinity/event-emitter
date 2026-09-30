using Codefinity.EventEmitter.Dispatch;
using Codefinity.EventEmitter.Listeners;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Codefinity.EventEmitter.Tests;

public class RegistrationTests
{
    private sealed class PingListener(CallLog calls)
    {
        [ApplicationModuleListener]
        public void On(Ping evt) => calls.Record("ping", evt);
    }

    private sealed class CustomRepository : IEventPublicationRepository
    {
        private readonly InMemoryEventPublicationRepository _inner = new();

        public int Created { get; private set; }

        public Task CreateAsync(IReadOnlyCollection<EventPublication> publications, CancellationToken cancellationToken = default)
        {
            Created += publications.Count;
            return _inner.CreateAsync(publications, cancellationToken);
        }

        public Task MarkCompletedAsync(Guid id, DateTimeOffset completionDate, CancellationToken cancellationToken = default) => _inner.MarkCompletedAsync(id, completionDate, cancellationToken);
        public Task MarkFailedAsync(Guid id, string failure, CancellationToken cancellationToken = default) => _inner.MarkFailedAsync(id, failure, cancellationToken);
        public Task<IReadOnlyList<EventPublication>> FindIncompleteAsync(CancellationToken cancellationToken = default) => _inner.FindIncompleteAsync(cancellationToken);
        public Task<IReadOnlyList<EventPublication>> FindCompletedAsync(CancellationToken cancellationToken = default) => _inner.FindCompletedAsync(cancellationToken);
        public Task DeleteAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) => _inner.DeleteAsync(ids, cancellationToken);
        public Task DeleteCompletedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default) => _inner.DeleteCompletedBeforeAsync(cutoff, cancellationToken);
    }

    /// <summary>A transaction the test opens and completes by hand.</summary>
    private sealed class ManualTransactions : ITransactionSynchronization
    {
        private readonly List<Action<bool>> _callbacks = [];

        public bool IsTransactionActive { get; private set; }

        public void Begin() => IsTransactionActive = true;

        public void End(bool committed)
        {
            IsTransactionActive = false;
            var callbacks = _callbacks.ToArray();
            _callbacks.Clear();
            foreach (var callback in callbacks)
            {
                callback(committed);
            }
        }

        public void RegisterAfterCompletion(Action<bool> onCompleted) => _callbacks.Add(onCompleted);
    }

    private sealed class SingletonListener
    {
        public int Calls;

        [EventListener]
        public void On(Ping evt) => Interlocked.Increment(ref Calls);
    }

    private class BaseListener(CallLog calls)
    {
        [EventListener]
        public void FromBase(Ping evt) => calls.Record("base", evt);

        [EventListener]
        public virtual void Overridable(Ping evt) => calls.Record("base-virtual", evt);
    }

    private sealed class DerivedListener(CallLog calls) : BaseListener(calls)
    {
        private readonly CallLog _calls = calls;

        // No attribute here: it is inherited from the base method.
        public override void Overridable(Ping evt) => _calls.Record("override", evt);
    }

    private sealed class GenericListener<T>(CallLog calls)
    {
        [EventListener]
        public void On(T evt) => calls.Record(typeof(T).Name, evt!);
    }

    [Fact]
    public void Calling_AddEventEmitter_twice_shares_one_registry_and_applies_both_option_delegates()
    {
        var services = new ServiceCollection();
        services.AddEventEmitter(o => o.MaxDegreeOfParallelism = 3).AddListener<PingListener>();
        services.AddEventEmitter(o => o.CompletionMode = CompletionMode.Delete).AddListener<SingletonListener>();

        Assert.Single(services, d => d.ServiceType == typeof(ListenerRegistry));
        Assert.Single(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(EventDispatcherHostedService));
        Assert.Single(services, d => d.ServiceType == typeof(IEventPublisher));

        var registry = (ListenerRegistry)services.Single(d => d.ServiceType == typeof(ListenerRegistry)).ImplementationInstance!;
        Assert.Equal(2, registry.ListenerTypes.Count);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<EventEmitterOptions>>().Value;
        Assert.Equal(3, options.MaxDegreeOfParallelism);
        Assert.Equal(CompletionMode.Delete, options.CompletionMode);
    }

    [Fact]
    public void The_registry_services_are_one_singleton()
    {
        using var provider = new ServiceCollection().AddLogging().AddEventEmitter().Services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<IIncompleteEventPublications>(),
            provider.GetRequiredService<ICompletedEventPublications>());
    }

    [Fact]
    public async Task UsePublicationRepository_of_T_replaces_the_default()
    {
        await using var app = await TestApp.StartAsync(b => b
            .AddListener<PingListener>()
            .UsePublicationRepository<CustomRepository>());

        var repository = Assert.IsType<CustomRepository>(app.Services.GetRequiredService<IEventPublicationRepository>());
        await app.PublishAsync(new Ping(1));

        Assert.Equal(1, repository.Created);
        await Eventually.AssertAsync(() => app.Calls.All.Count == 1, "the listener ran");
    }

    [Fact]
    public async Task UseTransactionSynchronization_replaces_System_Transactions()
    {
        await using var app = await TestApp.StartAsync(b => b
            .AddListener<PingListener>()
            .UseTransactionSynchronization<ManualTransactions>());
        var transactions = (ManualTransactions)app.Services.GetRequiredService<ITransactionSynchronization>();

        transactions.Begin();
        await app.PublishAsync(new Ping(1));
        await Task.Delay(100);
        Assert.Empty(app.Calls.All);

        transactions.End(committed: true);
        await Eventually.AssertAsync(() => app.Calls.All.Count == 1, "delivered after the commit");

        transactions.Begin();
        await app.PublishAsync(new Ping(2));
        transactions.End(committed: false);
        await Task.Delay(100);

        Assert.Single(app.Calls.All);
        Assert.Empty(await app.Incomplete.FindAllAsync());
    }

    [Fact]
    public async Task A_listener_you_registered_yourself_keeps_its_lifetime()
    {
        await using var app = await TestApp.StartAsync(
            b => b.AddListener<SingletonListener>(),
            services: s => s.AddSingleton<SingletonListener>());

        await app.PublishAsync(new Ping(1));
        await app.PublishAsync(new Ping(2));

        Assert.Equal(2, app.Services.GetRequiredService<SingletonListener>().Calls);
    }

    [Fact]
    public async Task Listener_methods_are_inherited_from_base_classes()
    {
        await using var app = await TestApp.StartAsync(b => b.AddListener<DerivedListener>());

        await app.PublishAsync(new Ping(1));

        Assert.Equal(["base", "override"], app.Calls.Listeners.Order());
    }

    [Fact]
    public async Task Closed_generic_listener_types_are_supported()
    {
        await using var app = await TestApp.StartAsync(b => b.AddListener<GenericListener<Ping>>());

        await app.PublishAsync(new Ping(1));
        await app.PublishAsync(new OrderCompleted(Guid.NewGuid()));

        Assert.Equal(["Ping"], app.Calls.Listeners);
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        var builder = new ServiceCollection().AddEventEmitter();

        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddEventEmitter());
        Assert.Throws<ArgumentNullException>(() => builder.AddListener(null!));
        Assert.Throws<ArgumentNullException>(() => builder.AddListenersFromAssembly(null!));
        Assert.Throws<ArgumentNullException>(() => builder.UsePublicationRepository(null!));
    }
}
