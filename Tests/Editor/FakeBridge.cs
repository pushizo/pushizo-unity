using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HeroSignalSdk.Unity.Internal;

namespace HeroSignalSdk.Unity.Tests
{
    /// <summary>Çağrıları kaydeden sahte köprü; olaylar <see cref="Sink"/> ile tetiklenir.</summary>
    internal sealed class FakeBridge : IHeroSignalBridge
    {
        private readonly Dictionary<string, string> _triggers = new Dictionary<string, string>();

        public List<string> Calls { get; } = new List<string>();
        public IBridgeEventSink Sink { get; private set; }
        public HeroSignalOptions Options { get; private set; }
        public string AppId { get; private set; }
        public int InitializeCount { get; private set; }
        public IReadOnlyDictionary<string, string> LastTags { get; private set; }
        public IReadOnlyList<string> LastKeys { get; private set; }
        public IReadOnlyDictionary<string, string> LastAliases { get; private set; }
        public Dictionary<string, object> LastEventProperties { get; private set; }
        public double? LastEventValue { get; private set; }
        public LogLevel? LastLogLevel { get; private set; }
        public int? LastInboxLimit { get; private set; }
        public string LastInboxCursor { get; private set; }

        public bool IsSupported { get; set; } = true;
        public bool Permission { get; set; }
        public bool PermissionResult { get; set; } = true;

        public void Initialize(string appId, HeroSignalOptions options, IBridgeEventSink sink)
        {
            AppId = appId;
            Options = options;
            Sink = sink;
            InitializeCount++;
        }

        public void SetConsentGiven(bool given) => Calls.Add("consent:" + given);
        public void Login(string externalId, string identityHash) => Calls.Add("login:" + externalId + ":" + (identityHash ?? "-"));
        public void Logout() => Calls.Add("logout");
        public void SetLogLevel(LogLevel level) => LastLogLevel = level;

        public string ExternalId => "user_1";
        public string UserId => "usr_1";
        public void AddTags(IReadOnlyDictionary<string, string> tags) => LastTags = tags;
        public void RemoveTags(IReadOnlyList<string> keys) => LastKeys = keys;
        public IReadOnlyDictionary<string, string> GetTags() => new Dictionary<string, string> { ["plan"] = "pro" };
        public void AddAliases(IReadOnlyDictionary<string, string> aliases) => LastAliases = aliases;
        public void RemoveAlias(string label) => Calls.Add("removeAlias:" + label);
        public void SetLanguage(string languageCode) => Calls.Add("language:" + languageCode);
        public void AddEmail(string email) => Calls.Add("addEmail:" + email);
        public void RemoveEmail(string email) => Calls.Add("removeEmail:" + email);
        public void AddSms(string phoneNumber) => Calls.Add("addSms:" + phoneNumber);
        public void RemoveSms(string phoneNumber) => Calls.Add("removeSms:" + phoneNumber);
        public IReadOnlyList<string> Emails => new[] { "a@example.com" };
        public IReadOnlyList<string> SmsNumbers => new[] { "+905321234567" };

        public PushSubscriptionState PushSubscription { get; set; } = new PushSubscriptionState("sub_1", "tok", true, true);
        public void OptIn() => Calls.Add("optIn");
        public void OptOut() => Calls.Add("optOut");

        public bool NotificationPermission => Permission;

        public Task<bool> RequestPermissionAsync(bool fallbackToSettings)
        {
            Calls.Add("requestPermission:" + fallbackToSettings);
            return Task.FromResult(PermissionResult);
        }

        public void ClearAllNotifications() => Calls.Add("clearAll");
        public void RemoveNotification(string id) => Calls.Add("removeNotification:" + id);
        public void RemoveGroupedNotifications(string group) => Calls.Add("removeGroup:" + group);

        public void AddTriggers(IReadOnlyDictionary<string, string> triggers)
        {
            foreach (var pair in triggers) _triggers[pair.Key] = pair.Value;
        }

        public void RemoveTriggers(IReadOnlyList<string> keys)
        {
            foreach (var key in keys) _triggers.Remove(key);
        }

        public void ClearTriggers() => _triggers.Clear();
        public IReadOnlyDictionary<string, string> GetTriggers() => new Dictionary<string, string>(_triggers);
        public void SetScreen(string name) => Calls.Add("screen:" + (name ?? "-"));
        public bool InAppPaused { get; set; }

        public Task<InboxPage> InboxListAsync(int limit, string cursor)
        {
            LastInboxLimit = limit;
            LastInboxCursor = cursor;
            return Task.FromResult(new InboxPage(Array.Empty<InboxItem>(), 0, null, false));
        }

        public Task<InboxItem> InboxMarkReadAsync(string id) => Task.FromResult(new InboxItem { Id = id, Read = true });
        public Task<int> InboxMarkAllReadAsync() => Task.FromResult(0);

        public Task InboxArchiveAsync(string id)
        {
            Calls.Add("archive:" + id);
            return Task.CompletedTask;
        }

        public Task<int> InboxUnreadCountAsync() => Task.FromResult(3);

        public void SendEvent(string name, Dictionary<string, object> properties, double? value)
        {
            Calls.Add("event:" + name);
            LastEventProperties = properties;
            LastEventValue = value;
        }
    }

    /// <summary>Eylemleri sıraya alan dağıtıcı (ana iş parçacığı döngüsünü taklit eder).</summary>
    internal sealed class QueueDispatcher : IMainThreadDispatcher
    {
        private readonly Queue<Action> _queue = new Queue<Action>();

        public int Pending => _queue.Count;

        public void Post(Action action) => _queue.Enqueue(action);

        public void RunAll()
        {
            while (_queue.Count > 0) _queue.Dequeue()();
        }
    }
}
