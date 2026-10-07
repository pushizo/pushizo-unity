#nullable enable
using System;

namespace HeroSignalSdk.Internal
{
    /// <summary>
    /// Yerel köprü sonucu: başarıda <paramref name="json"/> (işleme göre null olabilir), hatada
    /// <paramref name="errorCode"/> + <paramref name="errorMessage"/>. Herhangi bir iş parçacığından çağrılabilir.
    /// </summary>
    internal delegate void NativeResult(string? json, string? errorCode, string? errorMessage);

    /// <summary>
    /// Yerel köprü sözleşmesi: Android'de <c>com.herosignal.unity.HeroSignalUnityBridge</c> (Java,
    /// Runtime/Plugins/Android), iOS'ta <c>hs_*</c> C fonksiyonları (Swift, Runtime/Plugins/iOS), Editor'de
    /// <see cref="EditorNativeBridge"/>. Karmaşık değerler JSON metni olarak taşınır (biçimler <see cref="BridgeJson"/>).
    /// </summary>
    internal interface INativeBridge
    {
        bool IsNative { get; }

        /// <summary>Yerel SDK'yı başlatır; olaylar <c>(tür, json)</c> olarak herhangi bir iş parçacığından gelir.</summary>
        void Start(string appId, string optionsJson, Action<string, string> onEvent);

        void SetConsentGiven(bool given);
        void Login(string externalId, string? identityHash);
        void Logout();
        void SetLogLevel(int level);
        string? GetExternalId();
        string? GetUserId();
        void AddTags(string tagsJson);
        void RemoveTags(string keysJson);
        string GetTags();
        void AddAliases(string aliasesJson);
        void RemoveAlias(string label);
        void SetLanguage(string languageCode);
        void AddEmail(string email);
        void RemoveEmail(string email);
        void AddSms(string phoneNumber);
        void RemoveSms(string phoneNumber);
        string GetEmails();
        string GetSmsNumbers();
        string GetPushSubscription();
        void OptIn();
        void OptOut();
        bool GetPermission();
        void RequestPermission(bool fallbackToSettings, Action<bool> completion);
        void ClearAllNotifications();

        /// <summary>
        /// true: ön planda gelen bildirim yerelde ertelenir ve <c>foreground</c> olayı (<c>eventId</c> ile) üretilir;
        /// karar <see cref="ForegroundDisplay"/> ile verilir. false: SDK varsayılanı (bildirim gösterilir).
        /// </summary>
        void SetForegroundHandlerEnabled(bool enabled);

        void ForegroundDisplay(string eventId);
        void AddTriggers(string triggersJson);
        void RemoveTriggers(string keysJson);
        void ClearTriggers();
        string GetTriggers();
        void SetScreen(string? name);
        bool GetInAppPaused();
        void SetInAppPaused(bool paused);
        void InboxList(int limit, string? cursor, NativeResult result);
        void InboxMarkRead(string id, NativeResult result);
        void InboxMarkAllRead(NativeResult result);
        void InboxArchive(string id, NativeResult result);
        void InboxUnreadCount(NativeResult result);
        void SendEvent(string name, string propertiesJson, double value, bool hasValue);
    }
}
