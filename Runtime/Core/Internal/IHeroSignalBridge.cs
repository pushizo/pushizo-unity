using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HeroSignalSdk.Unity.Internal
{
    /// <summary>
    /// Yerel köprü (Android: <c>AndroidJavaClass</c> ile <c>com.herosignal.sdk.HeroSignal</c>; iOS: <c>__Internal</c> C ABI;
    /// Editör/desteklenmeyen: no-op). Girdiler bu katmana ulaşmadan <see cref="HeroSignal"/> tarafından doğrulanır;
    /// olaylar <see cref="IBridgeEventSink"/> ile herhangi bir iş parçacığından bildirilir.
    /// </summary>
    internal interface IHeroSignalBridge
    {
        /// <summary>Bu platformda yerel SDK var mı.</summary>
        bool IsSupported { get; }

        void Initialize(string appId, HeroSignalOptions options, IBridgeEventSink sink);
        void SetConsentGiven(bool given);
        void Login(string externalId, string identityHash);
        void Logout();
        void SetLogLevel(LogLevel level);

        // Kullanıcı
        string ExternalId { get; }
        string UserId { get; }
        void AddTags(IReadOnlyDictionary<string, string> tags);
        void RemoveTags(IReadOnlyList<string> keys);
        IReadOnlyDictionary<string, string> GetTags();
        void AddAliases(IReadOnlyDictionary<string, string> aliases);
        void RemoveAlias(string label);
        void SetLanguage(string languageCode);
        void AddEmail(string email);
        void RemoveEmail(string email);
        void AddSms(string phoneNumber);
        void RemoveSms(string phoneNumber);
        IReadOnlyList<string> Emails { get; }
        IReadOnlyList<string> SmsNumbers { get; }

        // Push aboneliği
        PushSubscriptionState PushSubscription { get; }
        void OptIn();
        void OptOut();

        // Bildirimler
        bool NotificationPermission { get; }
        Task<bool> RequestPermissionAsync(bool fallbackToSettings);
        void ClearAllNotifications();
        /// <summary>Android: sistem bildirim kimliği (sayı); iOS: istek kimliği veya teslimat kimliği.</summary>
        void RemoveNotification(string id);
        void RemoveGroupedNotifications(string group);

        // In-app
        void AddTriggers(IReadOnlyDictionary<string, string> triggers);
        void RemoveTriggers(IReadOnlyList<string> keys);
        void ClearTriggers();
        IReadOnlyDictionary<string, string> GetTriggers();
        void SetScreen(string name);
        bool InAppPaused { get; set; }

        // Bildirim kutusu
        Task<InboxPage> InboxListAsync(int limit, string cursor);
        Task<InboxItem> InboxMarkReadAsync(string id);
        Task<int> InboxMarkAllReadAsync();
        Task InboxArchiveAsync(string id);
        Task<int> InboxUnreadCountAsync();

        // Oturum
        void SendEvent(string name, Dictionary<string, object> properties, double? value);
    }

    /// <summary>Yerel olayların ortak katmana iletimi (herhangi bir iş parçacığından çağrılabilir).</summary>
    internal interface IBridgeEventSink
    {
        void OnNotificationClicked(NotificationClickedEventArgs args);

        /// <summary>
        /// Ön plan bildirimi. İşleyici varsa köprü kararı ana iş parçacığına taşımak için <paramref name="preventDefault"/>
        /// hemen çağrılır, işleyiciler engellemezse <paramref name="display"/> ile gösterilir. İşleyici yoksa dokunulmaz.
        /// </summary>
        void OnNotificationWillDisplay(HeroSignalNotification notification, Action preventDefault, Action display);

        void OnPermissionChanged(bool permission);
        void OnPushSubscriptionChanged(PushSubscriptionState state);
        void OnInAppClicked(InAppMessageClickedEventArgs args);
        void OnInAppLifecycle(InAppLifecyclePhase phase, InAppMessage message);
    }

    internal enum InAppLifecyclePhase
    {
        WillDisplay,
        DidDisplay,
        WillDismiss,
        DidDismiss,
    }
}
