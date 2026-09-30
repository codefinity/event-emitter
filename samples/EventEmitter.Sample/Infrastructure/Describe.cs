namespace Codefinity.EventEmitter.Sample.Infrastructure;

internal static class Describe
{
    /// <summary>A one-line summary of a publication for the example output.</summary>
    public static string Publication(EventPublication p)
    {
        var state = p.IsCompleted ? "completed" : "incomplete";
        var failure = p.LastFailure is null ? "" : $", last failure: {FirstLine(p.LastFailure)}";
        return $"{p.ListenerId} <- {p.EventType.Name} ({state}, attempts: {p.Attempts}{failure})";
    }

    // Exception.ToString() starts with "Type: message"; the stack trace follows on later lines.
    private static string FirstLine(string text) => text.Split('\n')[0].Trim();
}
