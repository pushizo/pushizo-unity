using System;
using System.Threading;
using HeroSignalSdk.Unity.Internal;

namespace HeroSignalSdk.Unity
{
    /// <summary>
    /// HeroSignal Unity SDK (eski sağlayıcı v5 yüzeyiyle uyumlu). Android'de yerel <c>com.herosignal:herosignal-android</c>,
    /// iOS'ta yerel <c>HeroSignal</c> Swift SDK'sı kullanılır; Editör'de çağrılar etkisizdir ve günlüğe yazılır.
    /// Tüm olaylar Unity ana iş parçacığında gelir.
    /// <code>
    /// HeroSignal.Initialize("app_…", new HeroSignalOptions { ApiUrl = "https://api.example.com" });
    /// await HeroSignal.Notifications.RequestPermissionAsync();
    /// HeroSignal.Login("user_42");
    /// HeroSignal.User.AddTag("plan", "pro");
    /// HeroSignal.Notifications.Clicked += (_, e) => Debug.Log(e.Notification.Title);
    /// </code>
    /// </summary>
    public static class HeroSignal
    {
        /// <summary>Bu C# katmanının sürümü.</summary>
        public const string SdkVersion = "0.1.0";

        private static readonly object s_gate = new object();
        private static IHeroSignalBridge s_bridge;
        private static SynchronizationContextDispatcher s_contextDispatcher = new SynchronizationContextDispatcher(null);
        private static IMainThreadDispatcher s_dispatcher = s_contextDispatcher;
        private static volatile bool s_initialized;
        private static string s_appId;

        internal static EventSink Sink { get; } = new EventSink();

        /// <summary><see cref="Initialize"/> çağrıldı mı.</summary>
        public static bool IsInitialized => s_initialized;

        /// <summary>Bu platformda yerel SDK var mı (Android ve iOS oyuncuları; Editör ve diğer platformlarda false).</summary>
        public static bool IsSupported => Bridge.IsSupported;

        public static UserManager User { get; } = new UserManager();
        public static NotificationsManager Notifications { get; } = new NotificationsManager();
        public static InAppMessagesManager InAppMessages { get; } = new InAppMessagesManager();
        public static InboxManager Inbox { get; } = new InboxManager();
        public static SessionManager Session { get; } = new SessionManager();
        public static DebugManager Debug { get; } = new DebugManager();

        private static IHeroSignalBridge Bridge
        {
            get
            {
                if (s_bridge != null) return s_bridge;
                lock (s_gate)
                {
                    if (s_bridge == null) s_bridge = PlatformHooks.CreateBridge();
                    return s_bridge;
                }
            }
        }

        /// <summary>
        /// SDK'yı başlatır (oyun açılışında bir kez, ana iş parçacığından; ör. ilk sahnedeki bir MonoBehaviour'un
        /// <c>Awake</c> metodu). Aynı App ID ile tekrar çağrı etkisizdir.
        /// </summary>
        public static void Initialize(string appId, HeroSignalOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(appId)) throw new ArgumentException("appId gerekli.", nameof(appId));
            var opts = (options ?? new HeroSignalOptions()).Clone();
            Validation.RequireApiUrl(opts.ApiUrl, nameof(options));
            lock (s_gate)
            {
                if (s_initialized && s_appId == appId) return;
                CaptureMainThread(SynchronizationContext.Current);
                PlatformHooks.ConfigureLogging();
                SdkLog.Level = opts.LogLevel;
                Bridge.Initialize(appId, opts, Sink);
                s_appId = appId;
                s_initialized = true;
            }
            if (!Bridge.IsSupported)
                SdkLog.Write(LogLevel.Warn, "bu platformda yerel HeroSignal SDK'sı yok; çağrılar etkisiz (yalnızca Android ve iOS oyuncuları desteklenir)");
        }

        /// <summary>KVKK/GDPR onayı (<see cref="HeroSignalOptions.RequiresPrivacyConsent"/>). false: kuyruk ve kimlik temizlenir.</summary>
        public static void SetConsentGiven(bool given) => Platform().SetConsentGiven(given);

        /// <summary>Cihazı kullanıcı kimliğinize bağlar. Identity verification açıksa sunucunuzun ürettiği <paramref name="identityHash"/> zorunludur.</summary>
        public static void Login(string externalId, string identityHash = null)
        {
            if (string.IsNullOrWhiteSpace(externalId)) throw new ArgumentException("externalId gerekli.", nameof(externalId));
            Platform().Login(externalId, string.IsNullOrEmpty(identityHash) ? null : identityHash);
        }

        /// <summary>Cihazı kullanıcıdan ayırır (anonim kullanıcıya döner).</summary>
        public static void Logout() => Platform().Logout();

        /// <summary>
        /// Ana iş parçacığı bağlamını yakalar (Unity'de <c>UnitySynchronizationContext</c>). Unity katmanı oyun açılışında
        /// çağırır; ilk yakalanan bağlam korunur.
        /// </summary>
        internal static void CaptureMainThread(SynchronizationContext context)
        {
            if (context == null || s_contextDispatcher.HasContext) return;
            s_contextDispatcher = new SynchronizationContextDispatcher(context);
            if (s_dispatcher is SynchronizationContextDispatcher) s_dispatcher = s_contextDispatcher;
        }

        /// <summary>Başlatılmış köprüyü döndürür; <see cref="Initialize"/> çağrılmadıysa hata fırlatır.</summary>
        internal static IHeroSignalBridge Platform()
        {
            if (!s_initialized) throw new InvalidOperationException("Önce HeroSignal.Initialize çağrılmalı.");
            return Bridge;
        }

        /// <summary>Başlatılmamışsa null (okuma özellikleri için).</summary>
        internal static IHeroSignalBridge PlatformOrNull() => s_initialized ? Bridge : null;

        internal static void Post(Action action) => s_dispatcher.Post(action);

        /// <summary>
        /// Oynatma oturumu başında durumu sıfırlar (Editör'de alan yeniden yüklemesi kapalıyken statik durum ve olay
        /// abonelikleri önceki oturumdan kalmasın). Köprü ilk kullanımda yeniden oluşturulur.
        /// </summary>
        internal static void ResetForPlayMode() => ResetForTests(null);

        /// <summary>Birim testleri için: köprüyü ve dağıtıcıyı değiştirir, durumu sıfırlar.</summary>
        internal static void ResetForTests(IHeroSignalBridge bridge, IMainThreadDispatcher dispatcher = null)
        {
            lock (s_gate)
            {
                s_bridge = bridge;
                s_contextDispatcher = new SynchronizationContextDispatcher(null);
                s_dispatcher = dispatcher ?? s_contextDispatcher;
                s_initialized = false;
                s_appId = null;
                User.PushSubscription.Reset();
                Notifications.Reset();
                InAppMessages.Reset();
            }
        }

        /// <summary>Olay işleyicisi hatası yerel SDK geri çağrısını bozmaz; günlüğe yazılır.</summary>
        internal static void Raise<T>(EventHandler<T> handler, T args, string eventName) where T : EventArgs
        {
            if (handler == null) return;
            foreach (var d in handler.GetInvocationList())
            {
                try
                {
                    ((EventHandler<T>)d)(null, args);
                }
                catch (Exception e)
                {
                    SdkLog.Write(LogLevel.Error, $"{eventName} olay işleyicisi hata verdi", e);
                }
            }
        }

        /// <summary>Yerel olayları ana iş parçacığına taşıyıp ilgili yöneticilere dağıtır.</summary>
        internal sealed class EventSink : IBridgeEventSink
        {
            public void OnNotificationClicked(NotificationClickedEventArgs args) => Post(() => Notifications.DispatchClicked(args));

            public void OnNotificationWillDisplay(HeroSignalNotification notification, Action preventDefault, Action display)
            {
                // İşleyici yoksa yerel SDK varsayılan davranışla gösterir.
                if (!Notifications.HasForegroundHandlers) return;
                preventDefault();
                Post(() =>
                {
                    var args = new NotificationWillDisplayEventArgs(notification, display);
                    Notifications.DispatchWillDisplay(args);
                    if (!args.IsPrevented) args.Display();
                });
            }

            public void OnPermissionChanged(bool permission) => Post(() => Notifications.DispatchPermission(permission));

            public void OnPushSubscriptionChanged(PushSubscriptionState state) => Post(() => User.PushSubscription.Dispatch(state));

            public void OnInAppClicked(InAppMessageClickedEventArgs args) => Post(() => InAppMessages.DispatchClicked(args));

            public void OnInAppLifecycle(InAppLifecyclePhase phase, InAppMessage message) => Post(() => InAppMessages.DispatchLifecycle(phase, message));
        }
    }
}
