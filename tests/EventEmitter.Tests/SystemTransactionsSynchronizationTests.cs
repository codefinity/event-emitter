using System.Transactions;

namespace EventEmitter.Tests;

public class SystemTransactionsSynchronizationTests
{
    private readonly SystemTransactionsSynchronization _synchronization = new();

    [Fact]
    public void Without_a_transaction_nothing_is_active_and_callbacks_cannot_be_registered()
    {
        Assert.False(_synchronization.IsTransactionActive);
        Assert.Throws<InvalidOperationException>(() => _synchronization.RegisterAfterCompletion(_ => { }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reports_whether_the_ambient_transaction_committed(bool complete)
    {
        bool? committed = null;

        using (var scope = new TransactionScope())
        {
            Assert.True(_synchronization.IsTransactionActive);
            _synchronization.RegisterAfterCompletion(c => committed = c);
            Assert.Null(committed);

            if (complete)
            {
                scope.Complete();
            }
        }

        Assert.Equal(complete, committed);
        Assert.False(_synchronization.IsTransactionActive);
    }

    [Fact]
    public void A_suppressed_scope_has_no_active_transaction()
    {
        using (new TransactionScope())
        using (new TransactionScope(TransactionScopeOption.Suppress))
        {
            Assert.False(_synchronization.IsTransactionActive);
        }
    }
}
