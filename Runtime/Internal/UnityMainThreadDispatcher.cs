#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace PushizoSdk.Internal
{
    /// <summary>
    /// Gizli, sahneler arası kalıcı MonoBehaviour: kuyruğu her karede boşaltır. Ana iş parçacığından gelen iş hemen
    /// çalıştırılır (ör. iOS'ta ana iş parçacığında gelen yerel olaylar).
    /// </summary>
    internal sealed class UnityMainThreadDispatcher : MonoBehaviour, IMainThreadDispatcher
    {
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        private int _mainThreadId;

        /// <summary>Ana iş parçacığından çağrılmalıdır (Pushizo.Initialize).</summary>
        public static UnityMainThreadDispatcher Create()
        {
            var go = new GameObject("PushizoDispatcher") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            var dispatcher = go.AddComponent<UnityMainThreadDispatcher>();
            dispatcher._mainThreadId = Thread.CurrentThread.ManagedThreadId;
            return dispatcher;
        }

        public void Post(Action action)
        {
            if (Thread.CurrentThread.ManagedThreadId == _mainThreadId)
            {
                Run(action);
                return;
            }
            _queue.Enqueue(action);
        }

        private void Update()
        {
            while (_queue.TryDequeue(out var action)) Run(action);
        }

        private static void Run(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                SdkLog.Write(LogLevel.Error, "Pushizo olayı işlenemedi", e);
            }
        }
    }
}
