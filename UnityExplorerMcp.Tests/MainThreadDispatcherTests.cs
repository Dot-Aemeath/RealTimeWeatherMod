using System.Collections.Concurrent;
using UnityExplorerMcp.Core;
using Xunit;

namespace UnityExplorerMcp.Tests
{
    public class MainThreadDispatcherTests
    {
        [Fact]
        public void Execute_TimedOutPendingWork_NeverRunsWhenQueueDrains()
        {
            var previous = SynchronizationContext.Current;
            var context = new QueuedContext();
            try
            {
                SynchronizationContext.SetSynchronizationContext(context);
                MainThreadDispatcher.InitializeFromMainThread();
                int calls = 0;
                Exception failure = null;
                var worker = new Thread(() =>
                {
                    try { MainThreadDispatcher.Execute(() => ++calls, 20); }
                    catch (Exception ex) { failure = ex; }
                });
                worker.Start();
                Assert.True(worker.Join(3000));
                Assert.IsType<TimeoutException>(failure);
                context.Drain();
                Assert.Equal(0, calls);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }

        [Fact]
        public void Execute_DispatchesResultAndUnwrapsExceptions()
        {
            var previous = SynchronizationContext.Current;
            var context = new QueuedContext();
            try
            {
                SynchronizationContext.SetSynchronizationContext(context);
                MainThreadDispatcher.InitializeFromMainThread();
                Assert.Equal(42, MainThreadDispatcher.Execute(() => 42));
                Exception failure = null;
                var worker = new Thread(() =>
                {
                    try { MainThreadDispatcher.Execute(() => throw new ArgumentException("expected")); }
                    catch (Exception ex) { failure = ex; }
                });
                worker.Start();
                Assert.True(SpinWait.SpinUntil(() => !context.Queue.IsEmpty, 3000));
                context.Drain();
                Assert.True(worker.Join(3000));
                Assert.IsType<ArgumentException>(failure);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }

        [Fact]
        public void Execute_MissingContext_ReturnsExplicitFailure()
        {
            var previous = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(null);
                MainThreadDispatcher.InitializeFromMainThread();
                Assert.Throws<InvalidOperationException>(() => MainThreadDispatcher.Execute(() => 1));
                Assert.False(MainThreadDispatcher.ExecuteAsync(() => { }));
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }

        private sealed class QueuedContext : SynchronizationContext
        {
            internal readonly ConcurrentQueue<Action> Queue = new();
            public override void Post(SendOrPostCallback callback, object state)
                => Queue.Enqueue(() => callback(state));
            internal void Drain()
            {
                while (Queue.TryDequeue(out var action)) action();
            }
        }
    }
}
