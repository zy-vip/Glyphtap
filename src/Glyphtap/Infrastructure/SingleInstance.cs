namespace Glyphtap.Infrastructure;

public static class SingleInstance
{
    public static bool TryAcquire(string name, out IDisposable? guard)
    {
        guard = null;
        var mutex = new Mutex(true, name, out var createdNew);
        if (!createdNew)
            return false;
        guard = new MutexGuard(mutex);
        return true;
    }

    private sealed class MutexGuard : IDisposable
    {
        private readonly Mutex _mutex;
        public MutexGuard(Mutex mutex) => _mutex = mutex;
        public void Dispose() => _mutex.Dispose();
    }
}