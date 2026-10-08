using System;
using System.Threading;

namespace HeroSignalSdk.Unity.Internal
{
    /// <summary>Olayların ana iş parçacığına taşınması.</summary>
    internal interface IMainThreadDispatcher
    {
        void Post(Action action);
    }

    /// <summary>
    /// <see cref="SynchronizationContext"/> tabanlı dağıtıcı. Unity'de ana iş parçacığının bağlamı
    /// <c>UnitySynchronizationContext</c>'tir; Unity katmanı onu oyun açılışında (<c>RuntimeInitializeOnLoadMethod</c>),
    /// aksi halde <see cref="HeroSignal.Initialize"/> çağrısında yakalar. Bağlam yoksa eylem çağıran iş parçacığında çalışır.
    /// </summary>
    internal sealed class SynchronizationContextDispatcher : IMainThreadDispatcher
    {
        private readonly SynchronizationContext _context;

        public SynchronizationContextDispatcher(SynchronizationContext context) => _context = context;

        public bool HasContext => _context != null;

        public void Post(Action action)
        {
            if (_context == null || SynchronizationContext.Current == _context)
            {
                Run(action);
                return;
            }
            _context.Post(state => Run((Action)state), action);
        }

        private static void Run(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                SdkLog.Write(LogLevel.Error, "ana iş parçacığı olayı hata verdi", e);
            }
        }
    }
}
