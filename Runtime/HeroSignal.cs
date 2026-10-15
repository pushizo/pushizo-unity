#nullable enable
using System;
using System.Threading.Tasks;
using HeroSignalSdk.Internal;

namespace HeroSignalSdk
{
    /// <summary>
    /// HeroSignal Unity SDK (eski sağlayıcı v5 yüzeyiyle uyumlu). Android'de yerel <c>com.herosignal:herosignal-android</c>,
    /// iOS'ta yerel <c>HeroSignal</c> Swift SDK'sı kullanılır; Editor'de çağrılar günlüğe yazılır. Tüm olaylar ve
    /// eşzamansız sonuçlar Unity ana iş parçacığında teslim edilir.
    /// <code>
    /// HeroSignal.Initialize("app_…", new HeroSignalOptions { ApiUrl = "https://api.example.com" });
    /// await HeroSignal.Notifications.RequestPermissionAsync();
    /// HeroSignal.Login("user_42");
    /// HeroSignal.User.AddTag("plan", "pro");
    /// var page = await HeroSignal.Inbox.ListAsync(limit: 20);
    /// </code>
    /// </summary>
    public static class HeroSignal
    {
        /// <summary>Bu C# katmanının sürümü.</summary>
        public const string SdkVersion = "0.1.0";

        private static readonly object s_gate = new object();
        private static INativeBridge? s_bridge;
        private static IMainThreadDispatcher? s_dispatcher;
        private static volatile bool s_initialized;
        private static string? s_appId;

        /// <summary><see cref="Initialize"/> çağrıldı mı.</summary>
        public static bool IsInitialized => s_initialized;

        /// <summary>Bu platformda yerel SDK var mı (Android/iOS cihaz derlemesi; Editor'de false).</summary>
        public static bool IsSupported => (s_bridge ?? PlatformFactory.Create()).IsNative;

        public static UserManager User { get; } = new UserManager();
        public static NotificationsManager Notifications { get; } = new NotificationsManager();
        public static InAppMessagesManager InAppMessages { get; } = new InAppMessagesManager();
        public static InboxManager Inbox { get; } = new InboxManager();
        public static SessionManager Session { get; } = new SessionManager();
        public static LiveActivitiesManager LiveActivities { get; } = new LiveActivitiesManager();
        public static DebugManager Debug { get; } = new DebugManager();

        /// <summary>
        /// SDK'yı başlatır (uygulama açılışında bir kez, ana iş parçacığından; ör. ilk sahnedeki bir MonoBehaviour'un
        /// <c>Awake</c>/<c>Start</c>'ı). Aynı App ID ile tekrar çağrı etkisizdir; farklı App ID hata üretir.
        /// </summary>
        public static void Initialize(string appId, HeroSignalOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(appId)) throw new ArgumentException("appId gerekli.", nameof(appId));
            var opts = (options ?? new HeroSignalOptions()).Clone();
            if (string.IsNullOrWhiteSpace(opts.ApiUrl) || !Uri.TryCreate(opts.ApiUrl, UriKind.Absolute, out var api) ||
                (api.Scheme != Uri.UriSchemeHttps && api.Scheme != Uri.UriSchemeHttp))
                throw new ArgumentException("ApiUrl http(s) ile başlayan mutlak bir adres olmalı.", nameof(options));
            if (Debug.ExplicitLevel is LogLevel explicitLevel) opts.LogLevel = explicitLevel;
            lock (s_gate)
            {
                if (s_initialized)
                {
                    if (s_appId == appId) return;
                    throw new InvalidOperationException("HeroSignal farklı bir App ID ile zaten başlatıldı; App ID uygulama ömrü boyunca değiştirilemez.");
                }
                var bridge = s_bridge ??= PlatformFactory.Create();
                var dispatcher = s_dispatcher ??= UnityMainThreadDispatcher.Create();
                SdkLog.Level = opts.LogLevel;
                bridge.Start(appId, BridgeJson.OptionsToJson(opts), (type, json) => dispatcher.Post(() => OnNativeEvent(type, json)));
                s_appId = appId;
                s_initialized = true;
                bridge.SetForegroundHandlerEnabled(Notifications.HasForegroundHandlers);
                // Başlangıç durumları tohumlanır; sonraki olaylar yalnızca gerçek değişiklikte üretilir.
                User.PushSubscription.Seed(BridgeJson.ParseSubscriptionState(bridge.GetPushSubscription()));
                Notifications.DispatchPermission(bridge.GetPermission());
            }
            if (!s_bridge!.IsNative)
                SdkLog.Write(LogLevel.Warn, "bu platformda yerel HeroSignal SDK'sı yok (Editor/desteklenmeyen platform); çağrılar günlüğe yazılır, push çalışmaz");
        }

        /// <summary>KVKK/GDPR onayı (<see cref="HeroSignalOptions.RequiresPrivacyConsent"/>). false: kuyruk ve kimlik temizlenir.</summary>
        public static void SetConsentGiven(bool given) => Bridge().SetConsentGiven(given);

        /// <summary>
        /// Cihazı kullanıcı kimliğinize bağlar. Identity verification açıksa app ayarına göre sunucunuzun ürettiği HMAC
        /// <paramref name="identityHash"/> ve/veya ES256 <paramref name="jwt"/> (ADR-0023 §9; <c>sub</c> = externalId,
        /// <c>aud</c> = appId) gerekir. JWT tüm SDK isteklerinde <c>Authorization: Bearer</c> başlığıyla gider; süresi
        /// dolunca <see cref="UpdateUserJwt"/> ile yenilenir.
        /// </summary>
        /// <exception cref="HeroSignalException">Verilen kanıt app'in kimlik doğrulama kipine uymuyorsa (<c>NOT_READY</c>).</exception>
        public static void Login(string externalId, string? identityHash = null, string? jwt = null)
        {
            if (string.IsNullOrWhiteSpace(externalId)) throw new ArgumentException("externalId gerekli.", nameof(externalId));
            ThrowIfFailed(Bridge().Login(
                externalId,
                string.IsNullOrEmpty(identityHash) ? null : identityHash,
                string.IsNullOrWhiteSpace(jwt) ? null : jwt));
        }

        /// <summary>
        /// Süresi dolan veya yenilenen kimlik JWT'sini günceller; sonraki SDK istekleri yeni belirteci taşır, kuyrukta
        /// bekleyen işlemler yeniden denenir.
        /// </summary>
        /// <exception cref="HeroSignalException">Giriş yapılmamışsa (<c>NOT_READY</c>).</exception>
        public static void UpdateUserJwt(string jwt)
        {
            if (string.IsNullOrWhiteSpace(jwt)) throw new ArgumentException("jwt gerekli.", nameof(jwt));
            ThrowIfFailed(Bridge().UpdateUserJwt(jwt));
        }

        /// <summary>Yerel köprünün döndürdüğü kimlik hatası (kanıt eksik, kullanıcı yok) <c>NOT_READY</c> olarak fırlatılır.</summary>
        private static void ThrowIfFailed(string? error)
        {
            if (error != null) throw new HeroSignalException(HeroSignalException.NotReady, error);
        }

        /// <summary>Cihazı kullanıcıdan ayırır (anonim kullanıcıya döner); kimlik JWT'si cihazdan silinir.</summary>
        public static void Logout() => Bridge().Logout();

        /// <summary>Başlatılmış köprü; <see cref="Initialize"/> çağrılmadıysa hata.</summary>
        internal static INativeBridge Bridge()
        {
            if (!s_initialized) throw new InvalidOperationException("Önce HeroSignal.Initialize çağrılmalı.");
            return s_bridge!;
        }

        /// <summary>Başlatılmamışsa null (okuma özellikleri için).</summary>
        internal static INativeBridge? BridgeOrNull() => s_initialized ? s_bridge : null;

        /// <summary>Eşzamansız yerel sonucu ana iş parçacığında tamamlanan Task'a çevirir.</summary>
        internal static Task<T> Call<T>(Action<NativeResult> invoke, Func<string?, T> map)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var dispatcher = s_dispatcher!;
            try
            {
                invoke((json, errorCode, errorMessage) => dispatcher.Post(() =>
                {
                    if (errorCode != null)
                    {
                        tcs.TrySetException(new HeroSignalException(errorCode, errorMessage ?? errorCode));
                        return;
                    }
                    try
                    {
                        tcs.TrySetResult(map(json));
                    }
                    catch (Exception e)
                    {
                        tcs.TrySetException(e);
                    }
                }));
            }
            catch (Exception e)
            {
                tcs.TrySetException(e);
            }
            return tcs.Task;
        }

        /// <summary>Yerel izin sonucunu ana iş parçacığına taşır.</summary>
        internal static Task<bool> RequestPermission(bool fallbackToSettings)
        {
            var bridge = Bridge();
            var dispatcher = s_dispatcher!;
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                bridge.RequestPermission(fallbackToSettings, granted => dispatcher.Post(() => tcs.TrySetResult(granted)));
            }
            catch (Exception e)
            {
                tcs.TrySetException(e);
            }
            return tcs.Task;
        }

        /// <summary>Ana iş parçacığında: yerel olayı ilgili yöneticiye yönlendirir. Hatalı yük günlüğe yazılır.</summary>
        internal static void OnNativeEvent(string type, string json)
        {
            try
            {
                switch (type)
                {
                    case "click":
                        Notifications.DispatchClicked(BridgeJson.ParseNotificationClick(json));
                        break;
                    case "foreground":
                    {
                        var parsed = BridgeJson.ParseForeground(json);
                        var bridge = s_bridge!;
                        var eventId = parsed.Key;
                        var args = new NotificationWillDisplayEventArgs(parsed.Value, () => bridge.ForegroundDisplay(eventId));
                        Notifications.DispatchWillDisplay(args);
                        args.Complete();
                        break;
                    }
                    case "subscription":
                        User.PushSubscription.Dispatch(BridgeJson.ParseSubscriptionState(json));
                        break;
                    case "permission":
                        // Yerel SDK izin gözlemcisi; abonelik olayıyla gelen aynı değişiklik NotificationsManager'da elenir.
                        Notifications.DispatchPermission(BridgeJson.ParsePermission(json));
                        break;
                    case "inAppClick":
                        InAppMessages.DispatchClicked(BridgeJson.ParseInAppClick(json));
                        break;
                    case "inAppLifecycle":
                    {
                        var parsed = BridgeJson.ParseInAppLifecycle(json);
                        if (parsed.Key != null) InAppMessages.DispatchLifecycle(parsed.Key, parsed.Value);
                        break;
                    }
                    default:
                        SdkLog.Write(LogLevel.Debug, $"bilinmeyen yerel olay türü yok sayıldı: {type}");
                        break;
                }
            }
            catch (Exception e)
            {
                SdkLog.Write(LogLevel.Error, $"yerel olay işlenemedi ({type})", e);
            }
        }

        /// <summary>Olay işleyicisi hatası diğer işleyicileri ve yerel akışı bozmaz; günlüğe yazılır.</summary>
        internal static void Raise<T>(Action<T>? handler, T args, string eventName)
        {
            if (handler is null) return;
            foreach (var d in handler.GetInvocationList())
            {
                try
                {
                    ((Action<T>)d)(args);
                }
                catch (Exception e)
                {
                    SdkLog.Write(LogLevel.Error, $"{eventName} olay işleyicisi hata verdi", e);
                }
            }
        }

        /// <summary>Birim testleri için: köprüyü ve dağıtıcıyı değiştirir, durumu sıfırlar.</summary>
        internal static void ResetForTests(INativeBridge bridge, IMainThreadDispatcher dispatcher)
        {
            lock (s_gate)
            {
                s_bridge = bridge;
                s_dispatcher = dispatcher;
                s_initialized = false;
                s_appId = null;
                User.PushSubscription.Reset();
                Notifications.Reset();
                InAppMessages.Reset();
                Debug.Reset();
                SdkLog.Level = LogLevel.Warn;
            }
        }
    }
}
