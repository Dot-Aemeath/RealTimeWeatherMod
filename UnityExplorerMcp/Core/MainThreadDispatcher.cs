using System;
using System.Threading;
using System.Threading.Tasks;

namespace UnityExplorerMcp.Core
{
    internal static class MainThreadDispatcher
    {
        private static SynchronizationContext _mainContext;
        private static int _mainThreadId;

        public static void InitializeFromMainThread()
        {
            _mainContext = SynchronizationContext.Current;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public static object Execute(Func<object> func, int timeoutMs = 10000)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            SynchronizationContext context = _mainContext;
            if (context == null)
                throw new InvalidOperationException("Unity main-thread context is not initialized");
            if (Thread.CurrentThread.ManagedThreadId == _mainThreadId) return func();

            // Pending work may be cancelled; code that has started cannot be interrupted safely.
            int state = 0; // pending=0, running=1, cancelled=2
            var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Post(_ =>
            {
                if (Interlocked.CompareExchange(ref state, 1, 0) != 0) return;
                try { completion.TrySetResult(func()); }
                catch (Exception ex) { completion.TrySetException(ex); }
            }, null);

            try
            {
                if (!completion.Task.Wait(timeoutMs))
                {
                    bool cancelled = Interlocked.CompareExchange(ref state, 2, 0) == 0;
                    throw new TimeoutException(cancelled
                        ? "Main thread request timed out and was cancelled before execution"
                        : "Main thread request timed out after execution started; it may still complete. Do not automatically retry mutations.");
                }
            }
            catch (AggregateException)
            {
                return completion.Task.GetAwaiter().GetResult();
            }

            return completion.Task.GetAwaiter().GetResult();
        }

        public static bool ExecuteAsync(Action action)
        {
            SynchronizationContext context = _mainContext;
            if (context == null) return false;
            context.Post(_ => action(), null);
            return true;
        }
    }
}
