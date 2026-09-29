namespace PcDashboard.Core;

/// <summary>Prevents inventory refreshes and foreground mutations from overlapping.</summary>
public sealed class OperationCoordinator
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private int busy;

    public ActionKind? ActiveMutation { get; private set; }
    public bool IsBusy => Volatile.Read(ref busy) != 0;
    public bool IsRefreshing => IsBusy && ActiveMutation is null;
    public event Action? Changed;

    public Task<IAsyncDisposable?> TryBeginRefreshAsync(CancellationToken ct = default)
        => TryBeginCoreAsync(null, ct);

    public Task<IAsyncDisposable?> TryBeginAsync(ActionKind kind, CancellationToken ct = default)
        => TryBeginCoreAsync(kind, ct);

    private async Task<IAsyncDisposable?> TryBeginCoreAsync(ActionKind? kind, CancellationToken ct)
    {
        if (!await gate.WaitAsync(0, ct)) return null;
        ActiveMutation = kind;
        Volatile.Write(ref busy, 1);
        try { Changed?.Invoke(); }
        catch
        {
            Volatile.Write(ref busy, 0);
            ActiveMutation = null;
            gate.Release();
            throw;
        }
        return new Lease(this);
    }

    private void Release()
    {
        ActiveMutation = null;
        Volatile.Write(ref busy, 0);
        gate.Release();
        Changed?.Invoke();
    }

    private sealed class Lease(OperationCoordinator owner) : IAsyncDisposable
    {
        private int released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref released, 1) == 0) owner.Release();
            return ValueTask.CompletedTask;
        }
    }
}
