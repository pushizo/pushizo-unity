using System;
using System.Collections.Generic;
using System.Threading;
using HeroSignalSdk.Unity.Internal;
using NUnit.Framework;

namespace HeroSignalSdk.Unity.Tests
{
    /// <summary><see cref="SynchronizationContextDispatcher"/>: UnitySynchronizationContext yerine kuyruklu bağlamla.</summary>
    public sealed class DispatcherTests
    {
        private sealed class QueueContext : SynchronizationContext
        {
            public readonly Queue<KeyValuePair<SendOrPostCallback, object>> Posted = new Queue<KeyValuePair<SendOrPostCallback, object>>();

            public override void Post(SendOrPostCallback d, object state) => Posted.Enqueue(new KeyValuePair<SendOrPostCallback, object>(d, state));

            public void RunAll()
            {
                while (Posted.Count > 0)
                {
                    var item = Posted.Dequeue();
                    item.Key(item.Value);
                }
            }
        }

        [Test]
        public void Posts_to_captured_context_from_other_threads()
        {
            var context = new QueueContext();
            var dispatcher = new SynchronizationContextDispatcher(context);
            var ran = 0;
            var worker = new Thread(() => dispatcher.Post(() => ran++));
            worker.Start();
            worker.Join();
            Assert.AreEqual(0, ran);
            Assert.AreEqual(1, context.Posted.Count);
            context.RunAll();
            Assert.AreEqual(1, ran);
        }

        [Test]
        public void Runs_inline_without_context_and_swallows_handler_errors()
        {
            var dispatcher = new SynchronizationContextDispatcher(null);
            var ran = false;
            dispatcher.Post(() => ran = true);
            Assert.IsTrue(ran);
            Assert.DoesNotThrow(() => dispatcher.Post(() => throw new InvalidOperationException("boom")));
        }

        [Test]
        public void Initialize_captures_current_context_for_events()
        {
            var previous = SynchronizationContext.Current;
            var context = new QueueContext();
            var bridge = new FakeBridge();
            try
            {
                HeroSignal.ResetForTests(bridge);
                SynchronizationContext.SetSynchronizationContext(context);
                HeroSignal.Initialize("app_ctx");
                var clicked = 0;
                HeroSignal.Notifications.Clicked += (_, __) => clicked++;

                var worker = new Thread(() => bridge.Sink.OnNotificationClicked(new NotificationClickedEventArgs(new HeroSignalNotification(), null, null)));
                worker.Start();
                worker.Join();
                Assert.AreEqual(0, clicked);
                context.RunAll();
                Assert.AreEqual(1, clicked);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
                HeroSignal.ResetForTests(new FakeBridge());
            }
        }
    }
}
