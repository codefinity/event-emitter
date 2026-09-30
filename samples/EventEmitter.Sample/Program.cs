using EventEmitter.Sample.Examples;

// Runnable examples of every EventEmitter capability.
//
//   dotnet run --project samples/EventEmitter.Sample                 runs all examples
//   dotnet run --project samples/EventEmitter.Sample -- resubmit     runs one (or several) by name
//   dotnet run --project samples/EventEmitter.Sample -- --list       lists them

(string Name, string Title, Func<Task> Run)[] examples =
[
    ("modules", "Events across assemblies: Orders -> Inventory -> Shipping", ModulesExample.RunAsync),
    ("transactions", "Module listeners run after commit, never after rollback", TransactionsExample.RunAsync),
    ("listener-methods", "Every supported listener method shape", ListenerMethodsExample.RunAsync),
    ("supertypes", "Listening for interfaces, base types and object", SupertypesExample.RunAsync),
    ("ordering", "Ordering synchronous listeners", OrderingExample.RunAsync),
    ("sync-failure", "A failing synchronous listener stops the publish", SynchronousFailureExample.RunAsync),
    ("resubmit", "Inspecting and resubmitting failed publications", ResubmitExample.RunAsync),
    ("retry-job", "Automatic retries with a background job and an attempt limit", RetryJobExample.RunAsync),
    ("completion-modes", "Keeping or deleting completed publications", CompletionModesExample.RunAsync),
    ("durable-storage", "Custom repository, shutdown timeout and republishing on startup", DurableStorageExample.RunAsync),
    ("unit-of-work", "Custom transaction synchronization", UnitOfWorkExample.RunAsync),
    ("parallelism", "Limiting concurrent module listeners", ParallelismExample.RunAsync),
    ("validation", "Listener ids and registration errors", ValidationExample.RunAsync),
];

if (args.Contains("--list"))
{
    foreach (var example in examples)
    {
        Console.WriteLine($"{example.Name,-18} {example.Title}");
    }

    return 0;
}

var selected = args.Length == 0
    ? examples
    : examples.Where(e => args.Contains(e.Name, StringComparer.OrdinalIgnoreCase)).ToArray();

if (selected.Length == 0)
{
    Console.WriteLine($"Unknown example '{string.Join(' ', args)}'. Use --list to see the names.");
    return 1;
}

foreach (var example in selected)
{
    Console.WriteLine();
    Console.WriteLine($"=== {example.Name}: {example.Title} ===");
    await example.Run();
}

return 0;
