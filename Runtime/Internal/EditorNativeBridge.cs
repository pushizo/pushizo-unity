#nullable enable
using System;
using System.Collections.Generic;

namespace HeroSignalSdk.Internal
{
    /// <summary>
    /// Unity Editor ve yerel SDK'sı olmayan platformlar (Standalone, WebGL…) için köprü: ağ çağrısı ve push yoktur.
    /// Çağrılar günlüğe yazılır (değerler değil, yalnızca işlem adı); tag, trigger, e-posta/SMS ve kimlik bellekte
    /// tutulur ki oyun kodu Editor'de akışı deneyebilsin. İzin isteği false, bildirim kutusu, tercihler ve Live Activities
    /// <see cref="HeroSignalException.PlatformNotSupported"/> döndürür.
    /// </summary>
    internal sealed class EditorNativeBridge : INativeBridge
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, string> _tags = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _triggers = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> _emails = new List<string>();
        private readonly List<string> _sms = new List<string>();
        private string? _externalId;
        private bool _paused;

        public bool IsNative => false;

        private static void Log(string operation) =>
            SdkLog.Write(LogLevel.Info, $"Editor: {operation} (yerel SDK yok; push ve ağ çağrısı yapılmaz)");

        public void Start(string appId, string optionsJson, Action<string, string> onEvent) => Log("Initialize");

        public void SetConsentGiven(bool given) => Log("SetConsentGiven");

        public string? Login(string externalId, string? identityHash, string? jwt)
        {
            lock (_gate) _externalId = externalId;
            Log("Login");
            return null;
        }

        public string? UpdateUserJwt(string jwt)
        {
            lock (_gate)
            {
                if (_externalId == null) return "Önce login ile kullanıcı kimliklendirilmeli";
            }
            Log("UpdateUserJwt");
            return null;
        }

        public void Logout()
        {
            lock (_gate) _externalId = null;
            Log("Logout");
        }

        public void SetLogLevel(int level)
        {
        }

        public string? GetExternalId()
        {
            lock (_gate) return _externalId;
        }

        public string? GetUserId() => null;

        public void AddTags(string tagsJson)
        {
            var tags = BridgeJson.ParseStringMap(tagsJson);
            lock (_gate)
            {
                foreach (var pair in tags) _tags[pair.Key] = pair.Value;
            }
            Log("AddTags");
        }

        public void RemoveTags(string keysJson)
        {
            var keys = BridgeJson.ParseStringList(keysJson);
            lock (_gate)
            {
                foreach (var key in keys) _tags.Remove(key);
            }
            Log("RemoveTags");
        }

        public string GetTags()
        {
            lock (_gate) return BridgeJson.ToJson(_tags);
        }

        public void AddAliases(string aliasesJson) => Log("AddAliases");

        public void RemoveAlias(string label) => Log("RemoveAlias");

        public void SetLanguage(string languageCode) => Log("SetLanguage");

        public void AddEmail(string email)
        {
            lock (_gate)
            {
                if (!_emails.Contains(email)) _emails.Add(email);
            }
            Log("AddEmail");
        }

        public void RemoveEmail(string email)
        {
            lock (_gate) _emails.Remove(email);
            Log("RemoveEmail");
        }

        public void AddSms(string phoneNumber)
        {
            lock (_gate)
            {
                if (!_sms.Contains(phoneNumber)) _sms.Add(phoneNumber);
            }
            Log("AddSms");
        }

        public void RemoveSms(string phoneNumber)
        {
            lock (_gate) _sms.Remove(phoneNumber);
            Log("RemoveSms");
        }

        public string GetEmails()
        {
            lock (_gate) return BridgeJson.ToJson(_emails);
        }

        public string GetSmsNumbers()
        {
            lock (_gate) return BridgeJson.ToJson(_sms);
        }

        public string GetPushSubscription() => @"{""id"":null,""token"":null,""optedIn"":false,""permission"":false}";

        public void OptIn() => Log("OptIn");

        public void OptOut() => Log("OptOut");

        public bool GetPermission() => false;

        public void RequestPermission(bool fallbackToSettings, Action<bool> completion)
        {
            Log("RequestPermission");
            completion(false);
        }

        public void ClearAllNotifications() => Log("ClearAllNotifications");

        public void RemoveNotification(string id) => Log("RemoveNotification");

        public void RemoveGroupedNotifications(string group) => Log("RemoveGroupedNotifications");

        public void SetForegroundHandlerEnabled(bool enabled)
        {
        }

        public void ForegroundDisplay(string eventId)
        {
        }

        public void AddTriggers(string triggersJson)
        {
            var triggers = BridgeJson.ParseStringMap(triggersJson);
            lock (_gate)
            {
                foreach (var pair in triggers) _triggers[pair.Key] = pair.Value;
            }
            Log("AddTriggers");
        }

        public void RemoveTriggers(string keysJson)
        {
            var keys = BridgeJson.ParseStringList(keysJson);
            lock (_gate)
            {
                foreach (var key in keys) _triggers.Remove(key);
            }
            Log("RemoveTriggers");
        }

        public void ClearTriggers()
        {
            lock (_gate) _triggers.Clear();
            Log("ClearTriggers");
        }

        public string GetTriggers()
        {
            lock (_gate) return BridgeJson.ToJson(_triggers);
        }

        public void SetScreen(string? name) => Log("SetScreen");

        public bool GetInAppPaused()
        {
            lock (_gate) return _paused;
        }

        public void SetInAppPaused(bool paused)
        {
            lock (_gate) _paused = paused;
        }

        private static void NotSupported(NativeResult result, string feature = "Bildirim kutusu") =>
            result(null, HeroSignalException.PlatformNotSupported, $"{feature} yalnızca Android ve iOS cihazlarda kullanılabilir.");

        public void InboxList(int limit, string? cursor, NativeResult result) => NotSupported(result);

        public void InboxMarkRead(string id, NativeResult result) => NotSupported(result);

        public void InboxMarkAllRead(NativeResult result) => NotSupported(result);

        public void InboxArchive(string id, NativeResult result) => NotSupported(result);

        public void InboxUnreadCount(NativeResult result) => NotSupported(result);

        public void GetPreferences(NativeResult result)
        {
            Log("GetPreferences");
            NotSupported(result, "Tercihler");
        }

        public void SetPreferences(string preferencesJson, NativeResult result)
        {
            Log("SetPreferences");
            NotSupported(result, "Tercihler");
        }

        public void SendEvent(string name, string propertiesJson, double value, bool hasValue) => Log("SendEvent");

        /// <summary>Live Activities iOS'a özgüdür; Editor'de çağrılar köprüye ulaşmadan <c>PLATFORM_NOT_SUPPORTED</c> olur.</summary>
        public bool SupportsLiveActivities => false;

        public void SetupDefaultLiveActivity(NativeResult result) => LiveActivitiesNotSupported(result);

        public void StartDefaultLiveActivity(string activityId, string attributesJson, string contentStateJson, NativeResult result) =>
            LiveActivitiesNotSupported(result);

        private static void LiveActivitiesNotSupported(NativeResult result) =>
            result(null, HeroSignalException.PlatformNotSupported, LiveActivitiesManager.UnsupportedMessage);
    }
}
