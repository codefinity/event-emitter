using System.Transactions;

namespace EventEmitter;

/// <summary>
/// Uses the ambient <see cref="Transaction.Current"/>. Create transaction scopes with
/// <see cref="TransactionScopeAsyncFlowOption.Enabled"/> so the transaction flows across <c>await</c>s.
/// </summary>
public sealed class SystemTransactionsSynchronization : ITransactionSynchronization
{
    public bool IsTransactionActive => Transaction.Current?.TransactionInformation.Status == TransactionStatus.Active;

    public void RegisterAfterCompletion(Action<bool> onCompleted)
    {
        var transaction = Transaction.Current ?? throw new InvalidOperationException("There is no ambient transaction.");

        transaction.TransactionCompleted += (_, e) =>
            onCompleted(e.Transaction?.TransactionInformation.Status == TransactionStatus.Committed);
    }
}
