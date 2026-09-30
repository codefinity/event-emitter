using Codefinity.EventEmitter;
using Codefinity.EventEmitter.Sample.Inventory;
using Codefinity.EventEmitter.Sample.Orders;
using Codefinity.EventEmitter.Sample.Shipping;

// The Orders, Inventory and Shipping modules behind a minimal API, plus admin endpoints for the
// event publication registry. Try it with EventEmitter.Sample.Web.http.

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOrdersModule()
    .AddInventoryModule()
    .AddShippingModule();

// Options from the "EventEmitter" section of appsettings.json.
builder.Services.Configure<EventEmitterOptions>(builder.Configuration.GetSection("EventEmitter"));

var app = builder.Build();

// --- The shop ---

app.MapPost("/orders/{orderId}/complete", async (string orderId, string customer, OrderService orders) =>
{
    await orders.CompleteAsync(orderId, customer);
    return Results.Accepted();   // Inventory and Shipping continue in the background
});

app.MapPost("/orders/{orderId}/cancel", async (string orderId, string reason, OrderService orders) =>
{
    await orders.CancelAsync(orderId, reason);
    return Results.Accepted();
});

app.MapGet("/shipments", (CarrierGateway carrier) => carrier.Shipments);

// Simulate a carrier outage: PUT /carrier?available=false
app.MapPut("/carrier", (bool available, CarrierGateway carrier) =>
{
    carrier.IsAvailable = available;
    return Results.NoContent();
});

// --- Event publication admin ---

var events = app.MapGroup("/admin/events");

events.MapGet("/incomplete", async (IIncompleteEventPublications incomplete) =>
    (await incomplete.FindAllAsync()).Select(Summarize));

events.MapGet("/completed", async (ICompletedEventPublications completed) =>
    (await completed.FindAllAsync()).Select(Summarize));

// POST /admin/events/resubmit                             everything incomplete
// POST /admin/events/resubmit?listenerId=shipping.book-shipment
// POST /admin/events/resubmit?olderThanSeconds=60
events.MapPost("/resubmit", async (IIncompleteEventPublications incomplete, string? listenerId, int? olderThanSeconds) =>
{
    var count = olderThanSeconds is { } seconds
        ? await incomplete.ResubmitOlderThanAsync(TimeSpan.FromSeconds(seconds))
        : await incomplete.ResubmitAsync(p => listenerId is null || p.ListenerId == listenerId);

    return Results.Ok(new { resubmitted = count });
});

// DELETE /admin/events/completed?olderThanDays=7
events.MapDelete("/completed", async (ICompletedEventPublications completed, int olderThanDays) =>
{
    await completed.DeletePublicationsOlderThanAsync(TimeSpan.FromDays(olderThanDays));
    return Results.NoContent();
});

app.Run();

static object Summarize(EventPublication p) => new
{
    p.Id,
    p.ListenerId,
    EventType = p.EventType.Name,
    p.Event,
    p.PublicationDate,
    p.CompletionDate,
    p.Attempts,
    LastFailure = p.LastFailure?.Split('\n')[0].Trim(),
};
