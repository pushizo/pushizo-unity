#nullable enable
#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using AOT;

namespace HeroSignalSdk.Internal
{
    /// <summary>
    /// <see cref="INativeBridge"/>'in iOS uygulaması: Runtime/Plugins/iOS/HeroSignalUnityBridge.swift içindeki
    /// <c>hs_*</c> C fonksiyonlarını <c>[DllImport("__Internal")]</c> ile çağırır. Metinler UTF-8 (sonu NUL) bayt dizisi
    /// olarak gönderilir; dönen dizgeler kopyalanıp <c>hs_free</c> ile serbest bırakılır. Geri çağrılar statik
    /// (<see cref="MonoPInvokeCallbackAttribute"/>) olup istek kimliğiyle eşleştirilir.
    /// </summary>
    internal sealed class IosNativeBridge : INativeBridge
    {
        private delegate void EventCallback(IntPtr type, IntPtr json);
        private delegate void ResultCallback(int requestId, IntPtr json, IntPtr errorCode, IntPtr errorMessage);
        private delegate void PermissionCallback(int requestId, int granted);

        // Yerel tarafa verilen temsilciler çöp toplayıcıdan korunur.
        private static readonly EventCallback s_eventCallback = OnEvent;
        private static readonly ResultCallback s_resultCallback = OnResult;
        private static readonly PermissionCallback s_permissionCallback = OnPermission;

        private static readonly ConcurrentDictionary<int, NativeResult> s_results = new ConcurrentDictionary<int, NativeResult>();
        private static readonly ConcurrentDictionary<int, Action<bool>> s_permissions = new ConcurrentDictionary<int, Action<bool>>();
        private static Action<string, string>? s_onEvent;
        private static int s_nextRequestId;

        public bool IsNative => true;

        [DllImport("__Internal")] private static extern void hs_free(IntPtr p);
        [DllImport("__Internal")] private static extern void hs_start(byte[] appId, byte[] optionsJson, EventCallback callback);
        [DllImport("__Internal")] private static extern void hs_set_consent_given(int given);
        [DllImport("__Internal")] private static extern IntPtr hs_login(byte[] externalId, byte[]? identityHash, byte[]? jwt);
        [DllImport("__Internal")] private static extern IntPtr hs_update_user_jwt(byte[] jwt);
        [DllImport("__Internal")] private static extern void hs_logout();
        [DllImport("__Internal")] private static extern void hs_set_log_level(int level);
        [DllImport("__Internal")] private static extern IntPtr hs_external_id();
        [DllImport("__Internal")] private static extern IntPtr hs_user_id();
        [DllImport("__Internal")] private static extern void hs_add_tags(byte[] json);
        [DllImport("__Internal")] private static extern void hs_remove_tags(byte[] json);
        [DllImport("__Internal")] private static extern IntPtr hs_tags();
        [DllImport("__Internal")] private static extern void hs_add_aliases(byte[] json);
        [DllImport("__Internal")] private static extern void hs_remove_alias(byte[] label);
        [DllImport("__Internal")] private static extern void hs_set_language(byte[] code);
        [DllImport("__Internal")] private static extern void hs_add_email(byte[] email);
        [DllImport("__Internal")] private static extern void hs_remove_email(byte[] email);
        [DllImport("__Internal")] private static extern void hs_add_sms(byte[] phone);
        [DllImport("__Internal")] private static extern void hs_remove_sms(byte[] phone);
        [DllImport("__Internal")] private static extern IntPtr hs_emails();
        [DllImport("__Internal")] private static extern IntPtr hs_sms_numbers();
        [DllImport("__Internal")] private static extern IntPtr hs_push_subscription();
        [DllImport("__Internal")] private static extern void hs_opt_in();
        [DllImport("__Internal")] private static extern void hs_opt_out();
        [DllImport("__Internal")] private static extern int hs_permission();
        [DllImport("__Internal")] private static extern void hs_request_permission(int fallbackToSettings, int requestId, PermissionCallback callback);
        [DllImport("__Internal")] private static extern void hs_clear_all_notifications();
        [DllImport("__Internal")] private static extern void hs_remove_notification(byte[] identifier);
        [DllImport("__Internal")] private static extern void hs_remove_grouped_notifications(byte[] group);
        [DllImport("__Internal")] private static extern void hs_set_foreground_handler_enabled(int enabled);
        [DllImport("__Internal")] private static extern void hs_foreground_display(byte[] eventId);
        [DllImport("__Internal")] private static extern void hs_add_triggers(byte[] json);
        [DllImport("__Internal")] private static extern void hs_remove_triggers(byte[] json);
        [DllImport("__Internal")] private static extern void hs_clear_triggers();
        [DllImport("__Internal")] private static extern IntPtr hs_triggers();
        [DllImport("__Internal")] private static extern void hs_set_screen(byte[]? name);
        [DllImport("__Internal")] private static extern int hs_in_app_paused();
        [DllImport("__Internal")] private static extern void hs_set_in_app_paused(int paused);
        [DllImport("__Internal")] private static extern void hs_inbox_list(int limit, byte[]? cursor, int requestId, ResultCallback callback);
        [DllImport("__Internal")] private static extern void hs_inbox_mark_read(byte[] id, int requestId, ResultCallback callback);
        [DllImport("__Internal")] private static extern void hs_inbox_mark_all_read(int requestId, ResultCallback callback);
        [DllImport("__Internal")] private static extern void hs_inbox_archive(byte[] id, int requestId, ResultCallback callback);
        [DllImport("__Internal")] private static extern void hs_inbox_unread_count(int requestId, ResultCallback callback);
        [DllImport("__Internal")] private static extern void hs_get_preferences(int requestId, ResultCallback callback);
        [DllImport("__Internal")] private static extern void hs_set_preferences(byte[] json, int requestId, ResultCallback callback);
        [DllImport("__Internal")] private static extern void hs_send_event(byte[] name, byte[] propertiesJson, double value, int hasValue);
        [DllImport("__Internal")] private static extern void hs_setup_default_live_activity(int requestId, ResultCallback callback);
        [DllImport("__Internal")] private static extern void hs_start_default_live_activity(byte[] activityId, byte[] attributesJson, byte[] contentStateJson, int requestId, ResultCallback callback);
        [DllImport("__Internal")] private static extern int hs_location_is_shared();
        [DllImport("__Internal")] private static extern void hs_location_set_shared(int shared);
        [DllImport("__Internal")] private static extern void hs_location_request_permission(byte[]? rationale, int requestId, PermissionCallback callback);

        private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value + "\0");

        private static byte[]? Utf8OrNull(string? value) => value is null ? null : Utf8(value);

        private static string? Read(IntPtr p) => p == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(p);

        /// <summary>Yerelde strdup ile ayrılan dizgeyi kopyalar ve serbest bırakır.</summary>
        private static string? Take(IntPtr p)
        {
            if (p == IntPtr.Zero) return null;
            try
            {
                return Marshal.PtrToStringUTF8(p);
            }
            finally
            {
                hs_free(p);
            }
        }

        private static int Register<T>(ConcurrentDictionary<int, T> map, T value)
        {
            var id = Interlocked.Increment(ref s_nextRequestId);
            map[id] = value;
            return id;
        }

        [MonoPInvokeCallback(typeof(EventCallback))]
        private static void OnEvent(IntPtr type, IntPtr json)
        {
            var t = Read(type);
            var j = Read(json);
            if (t != null && j != null) s_onEvent?.Invoke(t, j);
        }

        [MonoPInvokeCallback(typeof(ResultCallback))]
        private static void OnResult(int requestId, IntPtr json, IntPtr errorCode, IntPtr errorMessage)
        {
            if (s_results.TryRemove(requestId, out var result)) result(Read(json), Read(errorCode), Read(errorMessage));
        }

        [MonoPInvokeCallback(typeof(PermissionCallback))]
        private static void OnPermission(int requestId, int granted)
        {
            if (s_permissions.TryRemove(requestId, out var completion)) completion(granted != 0);
        }

        public void Start(string appId, string optionsJson, Action<string, string> onEvent)
        {
            s_onEvent = onEvent;
            hs_start(Utf8(appId), Utf8(optionsJson), s_eventCallback);
        }

        public void SetConsentGiven(bool given) => hs_set_consent_given(given ? 1 : 0);

        public string? Login(string externalId, string? identityHash, string? jwt) =>
            Take(hs_login(Utf8(externalId), Utf8OrNull(identityHash), Utf8OrNull(jwt)));

        public string? UpdateUserJwt(string jwt) => Take(hs_update_user_jwt(Utf8(jwt)));

        public void Logout() => hs_logout();

        public void SetLogLevel(int level) => hs_set_log_level(level);

        public string? GetExternalId() => Take(hs_external_id());

        public string? GetUserId() => Take(hs_user_id());

        public void AddTags(string tagsJson) => hs_add_tags(Utf8(tagsJson));

        public void RemoveTags(string keysJson) => hs_remove_tags(Utf8(keysJson));

        public string GetTags() => Take(hs_tags()) ?? "{}";

        public void AddAliases(string aliasesJson) => hs_add_aliases(Utf8(aliasesJson));

        public void RemoveAlias(string label) => hs_remove_alias(Utf8(label));

        public void SetLanguage(string languageCode) => hs_set_language(Utf8(languageCode));

        public void AddEmail(string email) => hs_add_email(Utf8(email));

        public void RemoveEmail(string email) => hs_remove_email(Utf8(email));

        public void AddSms(string phoneNumber) => hs_add_sms(Utf8(phoneNumber));

        public void RemoveSms(string phoneNumber) => hs_remove_sms(Utf8(phoneNumber));

        public string GetEmails() => Take(hs_emails()) ?? "[]";

        public string GetSmsNumbers() => Take(hs_sms_numbers()) ?? "[]";

        public string GetPushSubscription() => Take(hs_push_subscription()) ?? "{}";

        public void OptIn() => hs_opt_in();

        public void OptOut() => hs_opt_out();

        public bool GetPermission() => hs_permission() != 0;

        public void RequestPermission(bool fallbackToSettings, Action<bool> completion) =>
            hs_request_permission(fallbackToSettings ? 1 : 0, Register(s_permissions, completion), s_permissionCallback);

        public void ClearAllNotifications() => hs_clear_all_notifications();

        public void RemoveNotification(string id) => hs_remove_notification(Utf8(id));

        public void RemoveGroupedNotifications(string group) => hs_remove_grouped_notifications(Utf8(group));

        public void SetForegroundHandlerEnabled(bool enabled) => hs_set_foreground_handler_enabled(enabled ? 1 : 0);

        public void ForegroundDisplay(string eventId) => hs_foreground_display(Utf8(eventId));

        public void AddTriggers(string triggersJson) => hs_add_triggers(Utf8(triggersJson));

        public void RemoveTriggers(string keysJson) => hs_remove_triggers(Utf8(keysJson));

        public void ClearTriggers() => hs_clear_triggers();

        public string GetTriggers() => Take(hs_triggers()) ?? "{}";

        public void SetScreen(string? name) => hs_set_screen(Utf8OrNull(name));

        public bool GetInAppPaused() => hs_in_app_paused() != 0;

        public void SetInAppPaused(bool paused) => hs_set_in_app_paused(paused ? 1 : 0);

        public void InboxList(int limit, string? cursor, NativeResult result) =>
            hs_inbox_list(limit, Utf8OrNull(cursor), Register(s_results, result), s_resultCallback);

        public void InboxMarkRead(string id, NativeResult result) =>
            hs_inbox_mark_read(Utf8(id), Register(s_results, result), s_resultCallback);

        public void InboxMarkAllRead(NativeResult result) =>
            hs_inbox_mark_all_read(Register(s_results, result), s_resultCallback);

        public void InboxArchive(string id, NativeResult result) =>
            hs_inbox_archive(Utf8(id), Register(s_results, result), s_resultCallback);

        public void InboxUnreadCount(NativeResult result) =>
            hs_inbox_unread_count(Register(s_results, result), s_resultCallback);

        public void GetPreferences(NativeResult result) =>
            hs_get_preferences(Register(s_results, result), s_resultCallback);

        public void SetPreferences(string preferencesJson, NativeResult result) =>
            hs_set_preferences(Utf8(preferencesJson), Register(s_results, result), s_resultCallback);

        public void SendEvent(string name, string propertiesJson, double value, bool hasValue) =>
            hs_send_event(Utf8(name), Utf8(propertiesJson), value, hasValue ? 1 : 0);

        public bool SupportsLiveActivities => true;

        public void SetupDefaultLiveActivity(NativeResult result) =>
            hs_setup_default_live_activity(Register(s_results, result), s_resultCallback);

        public void StartDefaultLiveActivity(string activityId, string attributesJson, string contentStateJson, NativeResult result) =>
            hs_start_default_live_activity(Utf8(activityId), Utf8(attributesJson), Utf8(contentStateJson), Register(s_results, result), s_resultCallback);

        public bool GetLocationShared() => hs_location_is_shared() != 0;

        public void SetLocationShared(bool shared) => hs_location_set_shared(shared ? 1 : 0);

        public void RequestLocationPermission(string? rationale, Action<bool> completion) =>
            hs_location_request_permission(Utf8OrNull(rationale), Register(s_permissions, completion), s_permissionCallback);
    }
}
#endif
