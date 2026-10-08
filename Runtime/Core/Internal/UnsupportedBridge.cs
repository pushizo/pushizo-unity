using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HeroSignalSdk.Unity.Internal
{
    /// <summary>
    /// Yerel SDK'sı olmayan ortamlar (Unity Editör, masaüstü, WebGL): çağrılar etkisizdir ve günlüğe yazılır
    /// (Editör'de oyun akışı test edilebilsin diye Info seviyesinde); okuma özellikleri boş değer döndürür,
    /// bildirim kutusu <see cref="HeroSignalException.PlatformNotSupported"/> verir.
    /// </summary>
    internal sealed class UnsupportedBridge : IHeroSignalBridge
    {
        private readonly string _environment;

        public UnsupportedBridge(string environment) => _environment = environment;

        public bool IsSupported => false;

        public void Initialize(string appId, HeroSignalOptions options, IBridgeEventSink sink) => Skip($"Initialize({appId})");
        public void SetConsentGiven(bool given) => Skip($"SetConsentGiven({given})");
        public void Login(string externalId, string identityHash) => Skip($"Login({externalId})");
        public void Logout() => Skip("Logout");
        public void SetLogLevel(LogLevel level)
        {
        }

        public string ExternalId => null;
        public string UserId => null;
        public void AddTags(IReadOnlyDictionary<string, string> tags) => Skip($"User.AddTags({tags.Count})");
        public void RemoveTags(IReadOnlyList<string> keys) => Skip($"User.RemoveTags({string.Join(",", keys)})");
        public IReadOnlyDictionary<string, string> GetTags() => new Dictionary<string, string>();
        public void AddAliases(IReadOnlyDictionary<string, string> aliases) => Skip($"User.AddAliases({aliases.Count})");
        public void RemoveAlias(string label) => Skip($"User.RemoveAlias({label})");
        public void SetLanguage(string languageCode) => Skip($"User.SetLanguage({languageCode})");
        public void AddEmail(string email) => Skip("User.AddEmail");
        public void RemoveEmail(string email) => Skip("User.RemoveEmail");
        public void AddSms(string phoneNumber) => Skip("User.AddSms");
        public void RemoveSms(string phoneNumber) => Skip("User.RemoveSms");
        public IReadOnlyList<string> Emails => Array.Empty<string>();
        public IReadOnlyList<string> SmsNumbers => Array.Empty<string>();

        public PushSubscriptionState PushSubscription => PushSubscriptionState.Empty;
        public void OptIn() => Skip("PushSubscription.OptIn");
        public void OptOut() => Skip("PushSubscription.OptOut");

        public bool NotificationPermission => false;

        public Task<bool> RequestPermissionAsync(bool fallbackToSettings)
        {
            Skip("Notifications.RequestPermission");
            return Task.FromResult(false);
        }

        public void ClearAllNotifications() => Skip("Notifications.ClearAll");
        public void RemoveNotification(string id) => Skip($"Notifications.RemoveNotification({id})");
        public void RemoveGroupedNotifications(string group) => Skip($"Notifications.RemoveGroupedNotifications({group})");

        public void AddTriggers(IReadOnlyDictionary<string, string> triggers) => Skip($"InAppMessages.AddTriggers({triggers.Count})");
        public void RemoveTriggers(IReadOnlyList<string> keys) => Skip("InAppMessages.RemoveTriggers");
        public void ClearTriggers() => Skip("InAppMessages.ClearTriggers");
        public IReadOnlyDictionary<string, string> GetTriggers() => new Dictionary<string, string>();
        public void SetScreen(string name) => Skip($"InAppMessages.SetScreen({name ?? "null"})");

        public bool InAppPaused
        {
            get => false;
            set => Skip($"InAppMessages.Paused={value}");
        }

        public Task<InboxPage> InboxListAsync(int limit, string cursor) => Failed<InboxPage>();
        public Task<InboxItem> InboxMarkReadAsync(string id) => Failed<InboxItem>();
        public Task<int> InboxMarkAllReadAsync() => Failed<int>();
        public Task InboxArchiveAsync(string id) => Failed<bool>();
        public Task<int> InboxUnreadCountAsync() => Failed<int>();

        public void SendEvent(string name, Dictionary<string, object> properties, double? value) => Skip($"Session.SendEvent({name})");

        private void Skip(string operation) =>
            SdkLog.Write(LogLevel.Info, $"{operation}: {_environment} ortamında yerel SDK yok, çağrı etkisiz");

        private static Task<T> Failed<T>()
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetException(new HeroSignalException(HeroSignalException.PlatformNotSupported, "Bildirim kutusu yalnızca Android ve iOS'ta kullanılabilir."));
            return tcs.Task;
        }
    }
}
