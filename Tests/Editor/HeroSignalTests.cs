#nullable enable
using System;
using System.Collections.Generic;
using HeroSignalSdk.Internal;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HeroSignalSdk.Tests
{
    /// <summary>Yönetilen katmanın EditMode testleri: sahte köprü ile doğrulama, JSON sözleşmesi ve olay yönlendirme.</summary>
    public sealed class HeroSignalTests
    {
        private const string Notification =
            @"{""title"":""Kargo"",""body"":""Yolda"",""imageUrl"":null,""url"":""app://orders/1"",""deliveryId"":""dlv_1"",""messageId"":""msg_1"",""actions"":[{""id"":""track"",""title"":""Takip"",""url"":""app://track"",""icon"":null}],""data"":{""order"":""1""}}";

        private FakeNativeBridge _bridge = null!;

        [SetUp]
        public void SetUp()
        {
            _bridge = new FakeNativeBridge();
            HeroSignal.ResetForTests(_bridge, new ImmediateDispatcher());
        }

        [TearDown]
        public void TearDown() => HeroSignal.ResetForTests(new EditorNativeBridge(), new ImmediateDispatcher());

        private static void Init(HeroSignalOptions? options = null) =>
            HeroSignal.Initialize("app_test", options ?? new HeroSignalOptions { ApiUrl = "https://api.example.com" });

        [Test]
        public void Calls_before_initialize_throw_and_reads_are_safe()
        {
            Assert.Throws<InvalidOperationException>(() => HeroSignal.Login("user_1"));
            Assert.Throws<InvalidOperationException>(() => HeroSignal.User.AddTag("a", "b"));
            Assert.Throws<InvalidOperationException>(() => HeroSignal.Notifications.RequestPermissionAsync());
            Assert.IsNull(HeroSignal.User.ExternalId);
            Assert.IsEmpty(HeroSignal.User.GetTags());
            Assert.IsFalse(HeroSignal.Notifications.Permission);
        }

        [Test]
        public void Initialize_validates_and_passes_options()
        {
            Assert.Throws<ArgumentException>(() => HeroSignal.Initialize(" "));
            Assert.Throws<ArgumentException>(() => HeroSignal.Initialize("app_test", new HeroSignalOptions { ApiUrl = "api.example.com" }));

            HeroSignal.Debug.SetLogLevel(LogLevel.Info);
            Init(new HeroSignalOptions { ApiUrl = "http://10.0.2.2:4000", RequiresPrivacyConsent = true, LogLevel = LogLevel.Error, IosAppGroup = "group.x" });
            Init();

            Assert.AreEqual(1, _bridge.StartCount);
            var options = Json.ParseObject(_bridge.OptionsJson!);
            Assert.AreEqual("http://10.0.2.2:4000", options["apiUrl"]);
            Assert.AreEqual(true, options["requiresPrivacyConsent"]);
            Assert.AreEqual(3L, options["logLevel"]);
            Assert.AreEqual("group.x", options["iosAppGroup"]);
            Assert.IsNull(options["iosApnsEnvironment"]);
            Assert.Throws<InvalidOperationException>(() => HeroSignal.Initialize("app_other"));
        }

        [Test]
        public void User_operations_are_validated_and_serialized()
        {
            Init();
            HeroSignal.Login("user_42", "");
            HeroSignal.User.AddTag("plan", "pro");
            HeroSignal.User.RemoveTag("plan");
            HeroSignal.User.AddAlias("crm_id", " 42 ");
            HeroSignal.User.AddEmail(" Ali.Veli@Example.COM ");
            HeroSignal.User.AddSms("+90 (532) 123-45-67");
            HeroSignal.User.SetLanguage(" tr ");

            CollectionAssert.AreEqual(new[]
            {
                "foregroundEnabled:False",
                "login:user_42:<null>",
                @"addTags:{""plan"":""pro""}",
                @"removeTags:[""plan""]",
                @"addAliases:{""crm_id"":""42""}",
                "addEmail:ali.veli@example.com",
                "addSms:+905321234567",
                "language:tr",
            }, _bridge.Calls);

            Assert.Throws<ArgumentException>(() => HeroSignal.User.AddEmail("geçersiz"));
            Assert.Throws<ArgumentException>(() => HeroSignal.User.AddSms("05321234567"));
            Assert.Throws<ArgumentException>(() => HeroSignal.User.AddAlias("external_id", "x"));
            Assert.Throws<ArgumentException>(() => HeroSignal.User.AddTags(new Dictionary<string, string> { ["k"] = null! }));
            Assert.Throws<ArgumentException>(() => HeroSignal.InAppMessages.AddTrigger(new string('k', 65), "v"));

            _bridge.TagsJson = @"{""plan"":""pro"",""score"":10,""vip"":true}";
            var tags = HeroSignal.User.GetTags();
            Assert.AreEqual("pro", tags["plan"]);
            Assert.AreEqual("10", tags["score"]);
            Assert.AreEqual("true", tags["vip"]);
        }

        [Test]
        public void Send_event_serializes_nested_properties()
        {
            Init();
            HeroSignal.Session.SendEvent("purchase", new Dictionary<string, object?>
            {
                ["amount"] = 129.9,
                ["count"] = 2,
                ["note"] = null,
                ["items"] = new[] { "a", "b" },
                ["meta"] = new Dictionary<string, object?> { ["level"] = LogLevel.Info },
                ["at"] = new DateTimeOffset(2026, 10, 7, 12, 30, 0, TimeSpan.FromHours(3)),
            }, 129.9);

            Assert.IsTrue(_bridge.LastEventHasValue);
            var p = Json.ParseObject(_bridge.LastEventJson!);
            Assert.AreEqual(129.9, (double)p["amount"]!, 1e-9);
            Assert.AreEqual(2L, p["count"]);
            Assert.IsNull(p["note"]);
            Assert.AreEqual(2, ((List<object?>)p["items"]!).Count);
            Assert.AreEqual("Info", ((Dictionary<string, object?>)p["meta"]!)["level"]);
            Assert.AreEqual("2026-10-07T09:30:00.000Z", p["at"]);

            HeroSignal.Session.SendEvent("opened");
            Assert.IsFalse(_bridge.LastEventHasValue);
            Assert.AreEqual("{}", _bridge.LastEventJson);
            Assert.Throws<ArgumentException>(() => HeroSignal.Session.SendEvent("x", value: double.NaN));
            Assert.Throws<ArgumentException>(() => HeroSignal.Session.SendEvent("x", new Dictionary<string, object?> { ["o"] = new object() }));
        }

        [Test]
        public void Click_is_dispatched_and_replayed_to_first_late_subscriber()
        {
            Init();
            _bridge.Emit("click", @"{""notification"":" + Notification + @",""actionId"":""track"",""url"":""app://track""}");

            var received = new List<NotificationClickedEventArgs>();
            HeroSignal.Notifications.Clicked += e => received.Add(e);
            HeroSignal.Notifications.Clicked += e => received.Add(e);

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual("track", received[0].ActionId);
            Assert.AreEqual("Kargo", received[0].Notification.Title);
            Assert.AreEqual("1", received[0].Notification.Data["order"]);
            Assert.AreEqual("Takip", received[0].Notification.Actions[0].Title);
        }

        [Test]
        public void Foreground_handler_enables_native_deferral_and_decides_display()
        {
            Init();
            Assert.IsFalse(_bridge.ForegroundEnabled);

            var prevent = false;
            Action<NotificationWillDisplayEventArgs> handler = e => { if (prevent) e.PreventDefault(); };
            HeroSignal.Notifications.ForegroundWillDisplay += handler;
            Assert.IsTrue(_bridge.ForegroundEnabled);

            _bridge.Emit("foreground", @"{""eventId"":""evt_1"",""notification"":" + Notification + "}");
            Assert.AreEqual("display:evt_1", _bridge.Calls[_bridge.Calls.Count - 1]);

            prevent = true;
            _bridge.Emit("foreground", @"{""eventId"":""evt_2"",""notification"":" + Notification + "}");
            CollectionAssert.DoesNotContain(_bridge.Calls, "display:evt_2");

            HeroSignal.Notifications.ForegroundWillDisplay -= handler;
            Assert.IsFalse(_bridge.ForegroundEnabled);
        }

        [Test]
        public void Subscription_and_permission_events_fire_only_on_change()
        {
            Init();
            var changes = new List<PushSubscriptionChangedEventArgs>();
            var permissions = new List<bool>();
            HeroSignal.User.PushSubscription.Changed += e => changes.Add(e);
            HeroSignal.Notifications.PermissionChanged += p => permissions.Add(p);

            _bridge.Emit("subscription", @"{""id"":null,""token"":null,""optedIn"":false,""permission"":false}");
            _bridge.Emit("subscription", @"{""id"":""sub_1"",""token"":""tok"",""optedIn"":true,""permission"":true}");
            _bridge.Emit("subscription", @"{""id"":""sub_1"",""token"":""tok"",""optedIn"":true,""permission"":true}");
            _bridge.Emit("subscription", @"{""id"":""sub_1"",""token"":""tok"",""optedIn"":false,""permission"":false}");

            Assert.AreEqual(2, changes.Count);
            Assert.AreEqual("sub_1", changes[0].Current.Id);
            CollectionAssert.AreEqual(new[] { true, false }, permissions);
        }

        [Test]
        public void Native_permission_observer_event_raises_permission_changed_once()
        {
            Init();
            var permissions = new List<bool>();
            HeroSignal.Notifications.PermissionChanged += p => permissions.Add(p);

            // Başlangıç durumu (izin yok) abonelik olayıyla gelir; yerel izin gözlemcisi olayı yalnızca değişiklikte iletilir.
            _bridge.Emit("subscription", @"{""id"":""sub_1"",""token"":""tok"",""optedIn"":true,""permission"":false}");
            _bridge.Emit("permission", @"{""permission"":true}");
            _bridge.Emit("subscription", @"{""id"":""sub_1"",""token"":""tok"",""optedIn"":true,""permission"":true}");
            _bridge.Emit("permission", @"{""permission"":true}");
            _bridge.Emit("permission", @"{""permission"":false}");

            CollectionAssert.AreEqual(new[] { true, false }, permissions);
        }

        [Test]
        public void In_app_events_are_mapped_and_faulty_payloads_are_ignored()
        {
            Init();
            InAppMessageClickedEventArgs? click = null;
            var phases = new List<string>();
            HeroSignal.InAppMessages.Clicked += e => click = e;
            HeroSignal.InAppMessages.WillDisplay += e => phases.Add("will:" + e.Message.Id);
            HeroSignal.InAppMessages.DidDismiss += e => phases.Add("didDismiss");
            HeroSignal.InAppMessages.DidDisplay += e => throw new InvalidOperationException("uygulama hatası");

            _bridge.Emit("inAppClick", @"{""messageId"":""iam_1"",""actionId"":""buy"",""url"":null}");
            _bridge.Emit("inAppLifecycle", @"{""event"":""willDisplay"",""messageId"":""iam_1"",""json"":""{}""}");
            // Uygulama işleyicisinin hatası SDK günlüğüne yazılır (Unity test koşucusu beklenmeyen Error günlüğünü hata sayar).
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("DidDisplay olay işleyicisi hata verdi"));
            _bridge.Emit("inAppLifecycle", @"{""event"":""didDisplay"",""messageId"":""iam_1""}");
            _bridge.Emit("inAppLifecycle", @"{""event"":""didDismiss"",""messageId"":""iam_1""}");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"yerel olay işlenemedi \(click\)"));
            _bridge.Emit("click", "{bozuk");
            _bridge.Emit("unknown", "{}");

            Assert.AreEqual("buy", click!.ActionId);
            Assert.IsNull(click.Url);
            CollectionAssert.AreEqual(new[] { "will:iam_1", "didDismiss" }, phases);
        }

        [Test]
        public void Remove_notification_and_group_are_forwarded_to_bridge()
        {
            Init();
            _bridge.Calls.Clear();
            HeroSignal.Notifications.RemoveNotification(-42);
            HeroSignal.Notifications.RemoveNotification(" ios-request-1 ");
            HeroSignal.Notifications.RemoveGroupedNotifications("orders");
            CollectionAssert.AreEqual(new[] { "removeNotification:-42", "removeNotification:ios-request-1", "removeGrouped:orders" }, _bridge.Calls);

            Assert.Throws<ArgumentException>(() => HeroSignal.Notifications.RemoveNotification(" "));
            Assert.Throws<ArgumentException>(() => HeroSignal.Notifications.RemoveGroupedNotifications(""));
            Assert.AreEqual(3, _bridge.Calls.Count);
        }

        [Test]
        public void Android_notification_id_is_parsed_when_present()
        {
            Init();
            NotificationClickedEventArgs? received = null;
            HeroSignal.Notifications.Clicked += e => received = e;

            _bridge.Emit("click", @"{""notification"":{""title"":""T"",""data"":{},""androidNotificationId"":12345},""actionId"":null,""url"":null}");
            Assert.AreEqual(12345, received!.Notification.AndroidNotificationId);

            _bridge.Emit("click", @"{""notification"":" + Notification + @",""actionId"":null,""url"":null}");
            Assert.IsNull(received!.Notification.AndroidNotificationId);
        }

        [Test]
        public void Permission_and_inbox_results_complete_tasks()
        {
            Init();
            var permission = HeroSignal.Notifications.RequestPermissionAsync(fallbackToSettings: true);
            Assert.IsTrue(permission.IsCompleted);
            Assert.IsTrue(permission.Result);

            _bridge.InboxJson = @"{""items"":[{""id"":""in_1"",""title"":""Hoş geldin"",""read"":false,""createdAt"":1759838400000,""data"":{""k"":""v""}}],""unreadCount"":1,""nextCursor"":""c_2"",""hasMore"":true}";
            var page = HeroSignal.Inbox.ListAsync(20, "c_1");
            Assert.IsTrue(page.IsCompleted);
            Assert.AreEqual(1, page.Result.UnreadCount);
            Assert.AreEqual("Hoş geldin", page.Result.Items[0].Title);
            Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1759838400000), page.Result.Items[0].CreatedAt);
            Assert.AreEqual("c_2", page.Result.NextCursor);

            _bridge.InboxJson = @"{""unreadCount"":0}";
            Assert.AreEqual(0, HeroSignal.Inbox.MarkAllReadAsync().Result);

            _bridge.InboxErrorCode = "NOT_READY";
            var failed = HeroSignal.Inbox.UnreadCountAsync();
            Assert.IsTrue(failed.IsFaulted);
            Assert.AreEqual("NOT_READY", ((HeroSignalException)failed.Exception!.InnerException!).Code);

            Assert.Throws<ArgumentOutOfRangeException>(() => HeroSignal.Inbox.ListAsync(0));
            Assert.Throws<ArgumentException>(() => HeroSignal.Inbox.ArchiveAsync(" "));
        }

        [Test]
        public void Preferences_round_trip_through_bridge_json()
        {
            Init();
            _bridge.PreferencesJson =
                @"{""preferences"":[" +
                @"{""category"":""promo"",""channel"":null,""optedIn"":false,""explicit"":true,""source"":""sdk"",""updatedAt"":1759838400000}," +
                @"{""category"":""news"",""channel"":""email"",""optedIn"":true,""explicit"":false,""source"":null,""updatedAt"":null}]," +
                @"""categories"":[" +
                @"{""key"":""promo"",""name"":{""tr"":""Kampanyalar"",""en"":""Promotions""},""description"":{""tr"":""İndirim duyuruları""},""channels"":[""android_push"",""email""],""defaultOptedIn"":false}," +
                @"{""key"":""news"",""name"":{""de"":""Neuigkeiten""},""channels"":null}," +
                @"{""name"":{""tr"":""Anahtarsız""}}]}";

            var get = HeroSignal.User.GetPreferencesAsync();
            Assert.IsTrue(get.IsCompleted);
            var result = get.Result;
            Assert.AreEqual("getPreferences", _bridge.Calls[_bridge.Calls.Count - 1]);
            Assert.AreEqual(2, result.Preferences.Count);
            Assert.AreEqual("promo", result.Preferences[0].Category);
            Assert.IsNull(result.Preferences[0].Channel);
            Assert.IsFalse(result.Preferences[0].OptedIn);
            Assert.IsTrue(result.Preferences[0].Explicit);
            Assert.AreEqual("sdk", result.Preferences[0].Source);
            Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1759838400000), result.Preferences[0].UpdatedAt);
            Assert.AreEqual("email", result.Preferences[1].Channel);
            Assert.IsNull(result.Preferences[1].UpdatedAt);
            Assert.AreEqual(2, result.Categories.Count);
            Assert.AreEqual("Kampanyalar", result.Categories[0].LocalizedName("tr"));
            Assert.AreEqual("Promotions", result.Categories[0].LocalizedName("fr"));
            Assert.AreEqual("İndirim duyuruları", result.Categories[0].Description["tr"]);
            CollectionAssert.AreEqual(new[] { "android_push", "email" }, result.Categories[0].Channels);
            Assert.IsFalse(result.Categories[0].DefaultOptedIn);
            Assert.IsNull(result.Categories[1].Channels);
            Assert.IsTrue(result.Categories[1].DefaultOptedIn);
            Assert.AreEqual("Neuigkeiten", result.Categories[1].LocalizedName("tr"));

            var set = HeroSignal.User.SetPreferencesAsync(new[]
            {
                new PreferenceInput("promo", optedIn: false),
                new PreferenceInput("news", optedIn: true, channel: "email"),
            });
            Assert.IsTrue(set.IsCompleted);
            Assert.AreEqual(2, set.Result.Preferences.Count);
            Assert.AreEqual(
                @"setPreferences:[{""category"":""promo"",""channel"":null,""optedIn"":false},{""category"":""news"",""channel"":""email"",""optedIn"":true}]",
                _bridge.Calls[_bridge.Calls.Count - 1]);

            _bridge.PreferencesErrorCode = "NOT_READY";
            var failed = HeroSignal.User.GetPreferencesAsync();
            Assert.IsTrue(failed.IsFaulted);
            Assert.AreEqual("NOT_READY", ((HeroSignalException)failed.Exception!.InnerException!).Code);

            _bridge.PreferencesErrorCode = null;
            _bridge.PreferencesJson = @"{""preferences"":[{""optedIn"":true}]}";
            var invalid = HeroSignal.User.GetPreferencesAsync();
            Assert.AreEqual(HeroSignalException.InvalidResponse, ((HeroSignalException)invalid.Exception!.InnerException!).Code);
        }

        [Test]
        public void Preferences_inputs_are_validated_before_bridge()
        {
            Init();
            var calls = _bridge.Calls.Count;
            Assert.Throws<ArgumentNullException>(() => HeroSignal.User.SetPreferencesAsync(null!));
            Assert.Throws<ArgumentException>(() => HeroSignal.User.SetPreferencesAsync(new PreferenceInput[0]));
            var tooMany = new List<PreferenceInput>();
            for (var i = 0; i < 101; i++) tooMany.Add(new PreferenceInput("c" + i, true));
            Assert.Throws<ArgumentException>(() => HeroSignal.User.SetPreferencesAsync(tooMany));
            Assert.Throws<ArgumentException>(() => HeroSignal.User.SetPreferencesAsync(new[] { new PreferenceInput(" ", true) }));
            Assert.Throws<ArgumentException>(() => HeroSignal.User.SetPreferencesAsync(new[] { new PreferenceInput(new string('k', 65), true) }));
            Assert.Throws<ArgumentException>(() => HeroSignal.User.SetPreferencesAsync(new[] { new PreferenceInput("promo", true, "push") }));
            Assert.Throws<ArgumentException>(() => HeroSignal.User.SetPreferencesAsync(new PreferenceInput[] { null! }));
            Assert.AreEqual(calls, _bridge.Calls.Count);

            Assert.IsTrue(HeroSignal.User.SetPreferencesAsync(new[] { new PreferenceInput(new string('k', 64), true, "sms") }).IsCompleted);
            Assert.AreEqual(calls + 1, _bridge.Calls.Count);
        }

        [Test]
        public void Editor_bridge_simulates_state_without_native_sdk()
        {
            HeroSignal.ResetForTests(new EditorNativeBridge(), new ImmediateDispatcher());
            Init();
            Assert.IsFalse(HeroSignal.IsSupported);
            HeroSignal.Login("user_1");
            HeroSignal.User.AddTags(new Dictionary<string, string> { ["plan"] = "pro" });
            HeroSignal.User.AddEmail("ali@example.com");
            HeroSignal.InAppMessages.AddTrigger("level", "gold");

            Assert.AreEqual("user_1", HeroSignal.User.ExternalId);
            Assert.AreEqual("pro", HeroSignal.User.GetTags()["plan"]);
            CollectionAssert.AreEqual(new[] { "ali@example.com" }, HeroSignal.User.Emails);
            Assert.AreEqual("gold", HeroSignal.InAppMessages.GetTriggers()["level"]);
            Assert.IsFalse(HeroSignal.Notifications.RequestPermissionAsync().Result);
            var inbox = HeroSignal.Inbox.ListAsync();
            Assert.AreEqual(HeroSignalException.PlatformNotSupported, ((HeroSignalException)inbox.Exception!.InnerException!).Code);
            var preferences = HeroSignal.User.GetPreferencesAsync();
            Assert.AreEqual(HeroSignalException.PlatformNotSupported, ((HeroSignalException)preferences.Exception!.InnerException!).Code);
            var setPreferences = HeroSignal.User.SetPreferencesAsync(new[] { new PreferenceInput("promo", true) });
            Assert.AreEqual(HeroSignalException.PlatformNotSupported, ((HeroSignalException)setPreferences.Exception!.InnerException!).Code);
        }
    }
}
