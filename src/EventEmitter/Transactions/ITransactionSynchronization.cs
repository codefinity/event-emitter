namespace Codefinity.EventEmitter;

/// <summary>
/// Tells the publisher whether a transaction is in progress and when it finishes, so that
/// <see cref="ApplicationModuleListenerAttribute"/> listeners run only after a commit.
/// The default, <see cref="SystemTransactionsSynchronization"/>, follows <see cref="System.Transactions.Transaction.Current"/>.
/// </summary>
public interface ITransactionSynchronization
{
    bool IsTransactionActive { get; }

    /// <summary>
    /// Registers a callback for the end of the active transaction. The callback receives <c>true</c> if the
    /// transaction committed and <c>false</c> if it rolled back. Only called while <see cref="IsTransactionActive"/> is true.
    /// </summary>
    void RegisterAfterCompletion(Action<bool> onCompleted);
}
