#if UNITY_IOS
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AOT;

namespace HeroSignalSdk.Unity.Internal
{
    /// <summary>
    /// iOS köprüsü: <c>Plugins/iOS/HeroSignalUnityBridge.swift</c> C ABI'si (<c>[DllImport("__Internal")]</c>). Karmaşık
    /// değerler JSON ile taşınır (<see cref="BridgeJson"/>); metin dönüşleri <c>hs_unity_free</c> ile serbest bırakılır.
    /// Olay ve sonuç geri çağrıları IL2CPP için statik <c>MonoPInvokeCallback</c> metotlarıdır ve ana iş parçacığında gelir.
    /// Yerel SDK EDM4U ile CocoaPods'tan eklenir (Editor/HeroSignalDependencies.xml).
    /// </summary>
    internal sealed class IosBridge : IHeroSignalBridge
    {
        private const string Lib = "__Internal";

        private delegate void EventCallback(IntPtr type, IntPtr json);
        private delegate void ResultCallback(int requestId, IntPtr json, IntPtr errorJson);

        private static readonly object s_gate = new object();
        private static readonly Dictionary<int, Action<string, string>> s_pending = new Dictionary<int, Action<string, string>>();
        private static int s_nextRequestId;
        private static IBridgeEventSink s_sink;
        private static bool s_callbacksRegistered;

        public bool IsSupported => true;

        public void Initialize(string appId, HeroSignalOptions options, IBridgeEventSink sink)
        {
            s_sink = sink;
            if (!s_callbacksRegistered)
            {
                s_callbacksRegistered = true;
                hs_unity_set_result_callback(OnResult);
                hs_unity_set_event_callback(OnEvent);
            }
            hs_unity_start(appId, BridgeJson.OptionsJson(options));
        }

        [MonoPInvokeCallback(typeof(EventCallback))]
        private static void OnEvent(IntPtr typePtr, IntPtr jsonPtr)
        {
            var sink = s_sink;
            if (sink == null) return;
            var type = Marshal.PtrToStringUTF8(typePtr);
            var json = Marshal.PtrToStringUTF8(jsonPtr);
            try
            {
                switch (type)
                {
                    case "click":
                        sink.OnNotificationClicked(BridgeJson.ParseNotificationClick(json));
                        break;
                    case "foreground":
                        var foreground = BridgeJson.ParseForeground(json);
                        var eventId = foreground.Key;
                        sink.OnNotificationWillDisplay(foreground.Value,
                            () => hs_unity_foreground_prevent_default(eventId),
                            () => hs_unity_foreground_display(eventId));
                        break;
                    case "subscription":
                        sink.OnPushSubscriptionChanged(BridgeJson.ParseSubscriptionState(json));
                        break;
                    case "permission":
                        sink.OnPermissionChanged(BridgeJson.ParsePermission(json));
                        break;
                    case "inAppClick":
                        sink.OnInAppClicked(BridgeJson.ParseInAppClick(json));
                        break;
                    case "inAppLifecycle":
                        var lifecycle = BridgeJson.ParseInAppLifecycle(json);
                        sink.OnInAppLifecycle(lifecycle.Key, lifecycle.Value);
                        break;
                    default:
                        SdkLog.Write(LogLevel.Debug, $"bilinmeyen köprü olayı: {type}");
                        break;
                }
            }
            catch (Exception e)
            {
                SdkLog.Write(LogLevel.Error, $"köprü olayı işlenemedi ({type})", e);
            }
        }

        [MonoPInvokeCallback(typeof(ResultCallback))]
        private static void OnResult(int requestId, IntPtr jsonPtr, IntPtr errorPtr)
        {
            Action<string, string> handler;
            lock (s_gate)
            {
                if (!s_pending.TryGetValue(requestId, out handler)) return;
                s_pending.Remove(requestId);
            }
            handler(jsonPtr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(jsonPtr), errorPtr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(errorPtr));
        }

        /// <summary>İstek kaydeder; sonuç geldiğinde <paramref name="parse"/> ile çözümlenir.</summary>
        private static Task<T> Request<T>(Func<string, T> parse, Action<int> start)
        {
            var tcs = new TaskCompletionSource<T>();
            int id;
            lock (s_gate)
            {
                id = ++s_nextRequestId;
                s_pending[id] = (json, error) =>
                {
                    if (error != null)
                    {
                        tcs.TrySetException(BridgeJson.ParseError(error));
                        return;
                    }
                    try
                    {
                        tcs.TrySetResult(parse(json ?? "{}"));
                    }
                    catch (Exception e)
                    {
                        tcs.TrySetException(e is HeroSignalException ? e : new HeroSignalException(HeroSignalException.InvalidResponse, "Köprü yanıtı çözümlenemedi.", e));
                    }
                };
            }
            start(id);
            return tcs.Task;
        }

        private static string TakeString(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero) return null;
            try
            {
                return Marshal.PtrToStringUTF8(pointer);
            }
            finally
            {
                hs_unity_free(pointer);
            }
        }

        public void SetConsentGiven(bool given) => hs_unity_set_consent_given(given ? 1 : 0);
        public void Login(string externalId, string identityHash) => hs_unity_login(externalId, identityHash);
        public void Logout() => hs_unity_logout();
        public void SetLogLevel(LogLevel level) => hs_unity_set_log_level((int)level);

        // Kullanıcı
        public string ExternalId => TakeString(hs_unity_external_id());
        public string UserId => TakeString(hs_unity_user_id());
        public void AddTags(IReadOnlyDictionary<string, string> tags) => hs_unity_add_tags(BridgeJson.ToJson(tags));
        public void RemoveTags(IReadOnlyList<string> keys) => hs_unity_remove_tags(BridgeJson.ToJson(keys));
        public IReadOnlyDictionary<string, string> GetTags() => BridgeJson.ParseStringMap(TakeString(hs_unity_tags()));
        public void AddAliases(IReadOnlyDictionary<string, string> aliases) => hs_unity_add_aliases(BridgeJson.ToJson(aliases));
        public void RemoveAlias(string label) => hs_unity_remove_alias(label);
        public void SetLanguage(string languageCode) => hs_unity_set_language(languageCode);
        public void AddEmail(string email) => hs_unity_add_email(email);
        public void RemoveEmail(string email) => hs_unity_remove_email(email);
        public void AddSms(string phoneNumber) => hs_unity_add_sms(phoneNumber);
        public void RemoveSms(string phoneNumber) => hs_unity_remove_sms(phoneNumber);
        public IReadOnlyList<string> Emails => BridgeJson.ParseStringList(TakeString(hs_unity_emails()));
        public IReadOnlyList<string> SmsNumbers => BridgeJson.ParseStringList(TakeString(hs_unity_sms_numbers()));

        // Push aboneliği
        public PushSubscriptionState PushSubscription => BridgeJson.ParseSubscriptionState(TakeString(hs_unity_push_subscription()) ?? "{}");
        public void OptIn() => hs_unity_opt_in();
        public void OptOut() => hs_unity_opt_out();

        // Bildirimler
        public bool NotificationPermission => hs_unity_notification_permission() != 0;

        public Task<bool> RequestPermissionAsync(bool fallbackToSettings) =>
            Request(json => BridgeJson.ParsePermissionResult(json), id => hs_unity_request_permission(id, fallbackToSettings ? 1 : 0));

        public void ClearAllNotifications() => hs_unity_clear_all_notifications();
        public void RemoveNotification(string id) => hs_unity_remove_notification(id);
        public void RemoveGroupedNotifications(string group) => hs_unity_remove_grouped_notifications(group);

        // In-app
        public void AddTriggers(IReadOnlyDictionary<string, string> triggers) => hs_unity_add_triggers(BridgeJson.ToJson(triggers));
        public void RemoveTriggers(IReadOnlyList<string> keys) => hs_unity_remove_triggers(BridgeJson.ToJson(keys));
        public void ClearTriggers() => hs_unity_clear_triggers();
        public IReadOnlyDictionary<string, string> GetTriggers() => BridgeJson.ParseStringMap(TakeString(hs_unity_triggers()));
        public void SetScreen(string name) => hs_unity_set_screen(name);

        public bool InAppPaused
        {
            get => hs_unity_in_app_paused() != 0;
            set => hs_unity_set_in_app_paused(value ? 1 : 0);
        }

        // Bildirim kutusu
        public Task<InboxPage> InboxListAsync(int limit, string cursor) =>
            Request(BridgeJson.ParseInboxPage, id => hs_unity_inbox_list(id, limit, cursor));

        public Task<InboxItem> InboxMarkReadAsync(string itemId) =>
            Request(BridgeJson.ParseInboxItem, id => hs_unity_inbox_mark_read(id, itemId));

        public Task<int> InboxMarkAllReadAsync() => Request(BridgeJson.ParseCount, hs_unity_inbox_mark_all_read);

        public Task InboxArchiveAsync(string itemId) => Request(_ => true, id => hs_unity_inbox_archive(id, itemId));

        public Task<int> InboxUnreadCountAsync() => Request(BridgeJson.ParseCount, hs_unity_inbox_unread_count);

        // Oturum
        public void SendEvent(string name, Dictionary<string, object> properties, double? value) =>
            hs_unity_send_event(name, EventProperties.ToJson(properties), value.HasValue ? 1 : 0, value ?? 0);

        // MARK: C ABI (HeroSignalUnityBridge.swift)

        [DllImport(Lib)] private static extern void hs_unity_free(IntPtr pointer);
        [DllImport(Lib)] private static extern void hs_unity_set_event_callback(EventCallback callback);
        [DllImport(Lib)] private static extern void hs_unity_set_result_callback(ResultCallback callback);
        [DllImport(Lib)] private static extern void hs_unity_start(string appId, string optionsJson);
        [DllImport(Lib)] private static extern void hs_unity_set_consent_given(int given);
        [DllImport(Lib)] private static extern void hs_unity_login(string externalId, string identityHash);
        [DllImport(Lib)] private static extern void hs_unity_logout();
        [DllImport(Lib)] private static extern void hs_unity_set_log_level(int level);
        [DllImport(Lib)] private static extern IntPtr hs_unity_external_id();
        [DllImport(Lib)] private static extern IntPtr hs_unity_user_id();
        [DllImport(Lib)] private static extern void hs_unity_add_tags(string json);
        [DllImport(Lib)] private static extern void hs_unity_remove_tags(string json);
        [DllImport(Lib)] private static extern IntPtr hs_unity_tags();
        [DllImport(Lib)] private static extern void hs_unity_add_aliases(string json);
        [DllImport(Lib)] private static extern void hs_unity_remove_alias(string label);
        [DllImport(Lib)] private static extern void hs_unity_set_language(string code);
        [DllImport(Lib)] private static extern void hs_unity_add_email(string email);
        [DllImport(Lib)] private static extern void hs_unity_remove_email(string email);
        [DllImport(Lib)] private static extern void hs_unity_add_sms(string phoneNumber);
        [DllImport(Lib)] private static extern void hs_unity_remove_sms(string phoneNumber);
        [DllImport(Lib)] private static extern IntPtr hs_unity_emails();
        [DllImport(Lib)] private static extern IntPtr hs_unity_sms_numbers();
        [DllImport(Lib)] private static extern IntPtr hs_unity_push_subscription();
        [DllImport(Lib)] private static extern void hs_unity_opt_in();
        [DllImport(Lib)] private static extern void hs_unity_opt_out();
        [DllImport(Lib)] private static extern int hs_unity_notification_permission();
        [DllImport(Lib)] private static extern void hs_unity_request_permission(int requestId, int fallbackToSettings);
        [DllImport(Lib)] private static extern void hs_unity_clear_all_notifications();
        [DllImport(Lib)] private static extern void hs_unity_remove_notification(string id);
        [DllImport(Lib)] private static extern void hs_unity_remove_grouped_notifications(string group);
        [DllImport(Lib)] private static extern void hs_unity_foreground_prevent_default(string eventId);
        [DllImport(Lib)] private static extern void hs_unity_foreground_display(string eventId);
        [DllImport(Lib)] private static extern void hs_unity_add_triggers(string json);
        [DllImport(Lib)] private static extern void hs_unity_remove_triggers(string json);
        [DllImport(Lib)] private static extern void hs_unity_clear_triggers();
        [DllImport(Lib)] private static extern IntPtr hs_unity_triggers();
        [DllImport(Lib)] private static extern void hs_unity_set_screen(string name);
        [DllImport(Lib)] private static extern int hs_unity_in_app_paused();
        [DllImport(Lib)] private static extern void hs_unity_set_in_app_paused(int paused);
        [DllImport(Lib)] private static extern void hs_unity_inbox_list(int requestId, int limit, string cursor);
        [DllImport(Lib)] private static extern void hs_unity_inbox_mark_read(int requestId, string id);
        [DllImport(Lib)] private static extern void hs_unity_inbox_mark_all_read(int requestId);
        [DllImport(Lib)] private static extern void hs_unity_inbox_archive(int requestId, string id);
        [DllImport(Lib)] private static extern void hs_unity_inbox_unread_count(int requestId);
        [DllImport(Lib)] private static extern void hs_unity_send_event(string name, string propertiesJson, int hasValue, double value);
    }
}
#endif
