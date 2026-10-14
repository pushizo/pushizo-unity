#nullable enable
using System;
using System.Collections.Generic;
using HeroSignalSdk.Internal;

namespace HeroSignalSdk.Tests
{
    /// <summary>Yerel köprünün test ikizi: çağrıları kaydeder, olayları yerel köprülerle aynı JSON biçiminde üretir.</summary>
    internal sealed class FakeNativeBridge : INativeBridge
    {
        private Action<string, string>? _onEvent;

        public List<string> Calls { get; } = new List<string>();
        public string? OptionsJson { get; private set; }
        public int StartCount { get; private set; }
        public bool ForegroundEnabled { get; private set; }
        public string? ExternalIdValue { get; set; }
        public string TagsJson { get; set; } = "{}";
        public string SubscriptionJson { get; set; } = @"{""id"":null,""token"":null,""optedIn"":false,""permission"":false}";
        public bool PermissionValue { get; set; }
        public bool PermissionResult { get; set; } = true;
        public string? InboxJson { get; set; } = "{}";
        public string? InboxErrorCode { get; set; }
        public string? PreferencesJson { get; set; } = @"{""preferences"":[],""categories"":[]}";
        public string? PreferencesErrorCode { get; set; }
        public string? LastEventJson { get; private set; }
        public bool LastEventHasValue { get; private set; }

        public bool IsNative => true;

        public void Emit(string type, string json) => (_onEvent ?? throw new InvalidOperationException("başlatılmadı"))(type, json);

        private void Record(string call) => Calls.Add(call);

        public void Start(string appId, string optionsJson, Action<string, string> onEvent)
        {
            StartCount++;
            OptionsJson = optionsJson;
            _onEvent = onEvent;
        }

        public void SetConsentGiven(bool given) => Record("consent:" + given);
        public void Login(string externalId, string? identityHash) => Record("login:" + externalId + ":" + (identityHash ?? "<null>"));
        public void Logout() => Record("logout");
        public void SetLogLevel(int level) => Record("logLevel:" + level);
        public string? GetExternalId() => ExternalIdValue;
        public string? GetUserId() => null;
        public void AddTags(string tagsJson) => Record("addTags:" + tagsJson);
        public void RemoveTags(string keysJson) => Record("removeTags:" + keysJson);
        public string GetTags() => TagsJson;
        public void AddAliases(string aliasesJson) => Record("addAliases:" + aliasesJson);
        public void RemoveAlias(string label) => Record("removeAlias:" + label);
        public void SetLanguage(string languageCode) => Record("language:" + languageCode);
        public void AddEmail(string email) => Record("addEmail:" + email);
        public void RemoveEmail(string email) => Record("removeEmail:" + email);
        public void AddSms(string phoneNumber) => Record("addSms:" + phoneNumber);
        public void RemoveSms(string phoneNumber) => Record("removeSms:" + phoneNumber);
        public string GetEmails() => "[]";
        public string GetSmsNumbers() => "[]";
        public string GetPushSubscription() => SubscriptionJson;
        public void OptIn() => Record("optIn");
        public void OptOut() => Record("optOut");
        public bool GetPermission() => PermissionValue;

        public void RequestPermission(bool fallbackToSettings, Action<bool> completion)
        {
            Record("requestPermission:" + fallbackToSettings);
            completion(PermissionResult);
        }

        public void ClearAllNotifications() => Record("clearAll");
        public void RemoveNotification(string id) => Record("removeNotification:" + id);
        public void RemoveGroupedNotifications(string group) => Record("removeGrouped:" + group);

        public void SetForegroundHandlerEnabled(bool enabled)
        {
            ForegroundEnabled = enabled;
            Record("foregroundEnabled:" + enabled);
        }

        public void ForegroundDisplay(string eventId) => Record("display:" + eventId);
        public void AddTriggers(string triggersJson) => Record("addTriggers:" + triggersJson);
        public void RemoveTriggers(string keysJson) => Record("removeTriggers:" + keysJson);
        public void ClearTriggers() => Record("clearTriggers");
        public string GetTriggers() => "{}";
        public void SetScreen(string? name) => Record("screen:" + (name ?? "<null>"));
        public bool GetInAppPaused() => false;
        public void SetInAppPaused(bool paused) => Record("paused:" + paused);

        private void Respond(string call, NativeResult result)
        {
            Record(call);
            result(InboxErrorCode == null ? InboxJson : null, InboxErrorCode, InboxErrorCode);
        }

        public void InboxList(int limit, string? cursor, NativeResult result) => Respond("inboxList:" + limit + ":" + (cursor ?? "<null>"), result);
        public void InboxMarkRead(string id, NativeResult result) => Respond("inboxMarkRead:" + id, result);
        public void InboxMarkAllRead(NativeResult result) => Respond("inboxMarkAllRead", result);
        public void InboxArchive(string id, NativeResult result) => Respond("inboxArchive:" + id, result);
        public void InboxUnreadCount(NativeResult result) => Respond("inboxUnreadCount", result);

        private void RespondPreferences(string call, NativeResult result)
        {
            Record(call);
            result(PreferencesErrorCode == null ? PreferencesJson : null, PreferencesErrorCode, PreferencesErrorCode);
        }

        public void GetPreferences(NativeResult result) => RespondPreferences("getPreferences", result);
        public void SetPreferences(string preferencesJson, NativeResult result) => RespondPreferences("setPreferences:" + preferencesJson, result);

        public void SendEvent(string name, string propertiesJson, double value, bool hasValue)
        {
            Record("sendEvent:" + name);
            LastEventJson = propertiesJson;
            LastEventHasValue = hasValue;
        }
    }

    /// <summary>Testlerde olaylar çağıran iş parçacığında hemen çalışır.</summary>
    internal sealed class ImmediateDispatcher : IMainThreadDispatcher
    {
        public void Post(Action action) => action();
    }
}
