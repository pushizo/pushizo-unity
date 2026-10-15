#nullable enable
#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;

namespace HeroSignalSdk.Internal
{
    /// <summary>
    /// <see cref="INativeBridge"/>'in Android uygulaması: <c>com.herosignal.unity.HeroSignalUnityBridge</c>
    /// (Runtime/Plugins/Android) statik metotlarını AndroidJavaClass ile çağırır; geri çağrılar AndroidJavaProxy ile gelir.
    /// </summary>
    internal sealed class AndroidNativeBridge : INativeBridge
    {
        private const string BridgeClass = "com.herosignal.unity.HeroSignalUnityBridge";

        private AndroidJavaClass? _bridge;

        public bool IsNative => true;

        private AndroidJavaClass Bridge => _bridge ??= new AndroidJavaClass(BridgeClass);

        private void Call(string method, params object?[] args) => Bridge.CallStatic(method, args);

        private T Call<T>(string method, params object?[] args) => Bridge.CallStatic<T>(method, args);

        /// <summary>JNI çağrılarında null metin imza çözümünü bozar; null yerine boş metin gönderilir (Java tarafı null'a çevirir).</summary>
        private static string Text(string? value) => value ?? string.Empty;

        private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

        private static AndroidJavaObject CurrentActivity()
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            return player.GetStatic<AndroidJavaObject>("currentActivity");
        }

        public void Start(string appId, string optionsJson, Action<string, string> onEvent)
        {
            using var activity = CurrentActivity();
            Call("start", activity, appId, optionsJson, new EventListener(onEvent));
        }

        public void SetConsentGiven(bool given) => Call("setConsentGiven", given);

        public string? Login(string externalId, string? identityHash, string? jwt) =>
            NullIfEmpty(Call<string>("login", externalId, Text(identityHash), Text(jwt)));

        public string? UpdateUserJwt(string jwt) => NullIfEmpty(Call<string>("updateUserJwt", jwt));

        public void Logout() => Call("logout");

        public void SetLogLevel(int level) => Call("applyLogLevel", level);

        public string? GetExternalId() => NullIfEmpty(Call<string>("externalId"));

        public string? GetUserId() => NullIfEmpty(Call<string>("userId"));

        public void AddTags(string tagsJson) => Call("addTags", tagsJson);

        public void RemoveTags(string keysJson) => Call("removeTags", keysJson);

        public string GetTags() => Call<string>("tags");

        public void AddAliases(string aliasesJson) => Call("addAliases", aliasesJson);

        public void RemoveAlias(string label) => Call("removeAlias", label);

        public void SetLanguage(string languageCode) => Call("setLanguage", languageCode);

        public void AddEmail(string email) => Call("addEmail", email);

        public void RemoveEmail(string email) => Call("removeEmail", email);

        public void AddSms(string phoneNumber) => Call("addSms", phoneNumber);

        public void RemoveSms(string phoneNumber) => Call("removeSms", phoneNumber);

        public string GetEmails() => Call<string>("emails");

        public string GetSmsNumbers() => Call<string>("smsNumbers");

        public string GetPushSubscription() => Call<string>("pushSubscription");

        public void OptIn() => Call("optIn");

        public void OptOut() => Call("optOut");

        public bool GetPermission() => Call<bool>("permission");

        public void RequestPermission(bool fallbackToSettings, Action<bool> completion)
        {
            using var activity = CurrentActivity();
            Call("requestPermission", activity, fallbackToSettings, new PermissionCallback(completion));
        }

        public void ClearAllNotifications() => Call("clearAllNotifications");

        public void RemoveNotification(string id)
        {
            // Android tepsi kimliği tamsayıdır; iOS biçimindeki metin kimliğin burada karşılığı yoktur.
            if (!int.TryParse(id, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var androidNotificationId))
                throw new ArgumentException("Android bildirim kimliği tamsayı olmalı (HeroSignalNotification.AndroidNotificationId).", nameof(id));
            Call("removeNotification", androidNotificationId);
        }

        public void RemoveGroupedNotifications(string group) => Call("removeGroupedNotifications", group);

        public void SetForegroundHandlerEnabled(bool enabled) => Call("setForegroundHandlerEnabled", enabled);

        public void ForegroundDisplay(string eventId) => Call("foregroundDisplay", eventId);

        public void AddTriggers(string triggersJson) => Call("addTriggers", triggersJson);

        public void RemoveTriggers(string keysJson) => Call("removeTriggers", keysJson);

        public void ClearTriggers() => Call("clearTriggers");

        public string GetTriggers() => Call<string>("triggers");

        public void SetScreen(string? name) => Call("setScreen", Text(name));

        public bool GetInAppPaused() => Call<bool>("inAppPaused");

        public void SetInAppPaused(bool paused) => Call("updateInAppPaused", paused);

        public void InboxList(int limit, string? cursor, NativeResult result) => Call("inboxList", limit, Text(cursor), new ResultCallback(result));

        public void InboxMarkRead(string id, NativeResult result) => Call("inboxMarkRead", id, new ResultCallback(result));

        public void InboxMarkAllRead(NativeResult result) => Call("inboxMarkAllRead", new ResultCallback(result));

        public void InboxArchive(string id, NativeResult result) => Call("inboxArchive", id, new ResultCallback(result));

        public void InboxUnreadCount(NativeResult result) => Call("inboxUnreadCount", new ResultCallback(result));

        public void GetPreferences(NativeResult result) => Call("getPreferences", new ResultCallback(result));

        public void SetPreferences(string preferencesJson, NativeResult result) => Call("setPreferences", preferencesJson, new ResultCallback(result));

        public void SendEvent(string name, string propertiesJson, double value, bool hasValue) =>
            Call("sendEvent", name, propertiesJson, value, hasValue);

        /// <summary>Live Activities iOS'a özgüdür; Android'de çağrılar köprüye ulaşmadan <c>PLATFORM_NOT_SUPPORTED</c> olur.</summary>
        public bool SupportsLiveActivities => false;

        public void SetupDefaultLiveActivity(NativeResult result) => LiveActivitiesNotSupported(result);

        public void StartDefaultLiveActivity(string activityId, string attributesJson, string contentStateJson, NativeResult result) =>
            LiveActivitiesNotSupported(result);

        private static void LiveActivitiesNotSupported(NativeResult result) =>
            result(null, HeroSignalException.PlatformNotSupported, LiveActivitiesManager.UnsupportedMessage);

        public bool GetLocationShared() => Call<bool>("locationShared");

        public void SetLocationShared(bool shared) => Call("setLocationShared", shared);

        /// <summary>Açıklama diyaloğu ve sistem izni Java köprüsündedir; sonuç etkinlik ön plana döndüğünde gelir.</summary>
        public void RequestLocationPermission(string? rationale, Action<bool> completion)
        {
            using var activity = CurrentActivity();
            Call("requestLocationPermission", activity, Text(rationale), new PermissionCallback(completion));
        }

        /// <summary><c>HeroSignalUnityListener</c>; yerel iş parçacığından çağrılır (ana iş parçacığına HeroSignal taşır).</summary>
        private sealed class EventListener : AndroidJavaProxy
        {
            private readonly Action<string, string> _onEvent;

            public EventListener(Action<string, string> onEvent)
                : base("com.herosignal.unity.HeroSignalUnityListener") => _onEvent = onEvent;

            // AndroidJavaProxy metot adıyla eşleştirir (Java arayüzündeki ad).
            public void onEvent(string type, string json) => _onEvent(type, json);
        }

        private sealed class ResultCallback : AndroidJavaProxy
        {
            private readonly NativeResult _result;

            public ResultCallback(NativeResult result)
                : base("com.herosignal.unity.HeroSignalUnityCallback") => _result = result;

            public void onResult(string? json, string? errorCode, string? errorMessage) => _result(json, errorCode, errorMessage);
        }

        private sealed class PermissionCallback : AndroidJavaProxy
        {
            private readonly Action<bool> _completion;

            public PermissionCallback(Action<bool> completion)
                : base("com.herosignal.unity.HeroSignalUnityPermissionCallback") => _completion = completion;

            public void onResult(bool granted) => _completion(granted);
        }
    }
}
#endif
