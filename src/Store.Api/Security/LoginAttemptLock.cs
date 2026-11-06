namespace Store.Api.Security;

// Bounded stripes serialize per-account lockout updates in this single-instance application.
public sealed class LoginAttemptLock : IDisposable
{
    private readonly SemaphoreSlim[] gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public SemaphoreSlim For(string normalizedUserName) => gates[(uint)StringComparer.Ordinal.GetHashCode(normalizedUserName) % (uint)gates.Length];

    public void Dispose()
    {
        foreach (var gate in gates)
        {
            gate.Dispose();
        }
    }
}
