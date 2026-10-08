using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroSignalSdk.Unity.Internal;
using NUnit.Framework;

namespace HeroSignalSdk.Unity.Tests
{
    /// <summary>Statik <see cref="HeroSignal"/> yüzeyi: doğrulama, köprüye iletim ve ana iş parçacığına olay yönlendirme.</summary>
    public sealed class HeroSignalTests
    {
        private FakeBridge _bridge;
        private QueueDispatcher _main;

        [SetUp]
        public void SetUp()
        {
            _bridge = new FakeBridge();
            _main = new QueueDispatcher();
            HeroSignal.ResetForTests(_bridge, _main);
        }

        [TearDown]
        public void TearDown() => HeroSignal.ResetForTests(new FakeBridge());

        private void Init(Action<HeroSignalOptions> configure = null)
        {
            var options = new HeroSignalOptions { ApiUrl = "https://api.example.com" };
            configure?.Invoke(options);
            HeroSignal.Initialize("app_test", options);
        }

        [Test]
        public void Calls_before_initialize_throw_but_reads_are_safe()
        {
            Assert.IsFalse(HeroSignal.IsInitialized);
            Assert.Throws<InvalidOperationException>(() => HeroSignal.Login("u1"));
            Assert.Throws<InvalidOperationException>(() => HeroSignal.User.AddTag("a", "b"));
            Assert.Throws<InvalidOperationException>(() => HeroSignal.Notifications.ClearAll());
            Assert.IsNull(HeroSignal.User.ExternalId);
            Assert.IsFalse(HeroSignal.Notifications.Permission);
            Assert.AreEqual(0, HeroSignal.User.GetTags().Count);
        }

        [TestCase("")]
        [TestCase("   ")]
        public void Initialize_requires_app_id(string appId) =>
            Assert.Throws<ArgumentException>(() => HeroSignal.Initialize(appId));

        [TestCase("ftp://api.example.com")]
        [TestCase("api.example.com")]
        [TestCase("")]
        public void Initialize_requires_http_api_url(string apiUrl) =>
            Assert.Throws<ArgumentException>(() => HeroSignal.Initialize("app_1", new HeroSignalOptions { ApiUrl = apiUrl }));

        [Test]
        public void Initialize_copies_options_and_is_idempotent_for_same_app()
        {
            var options = new HeroSignalOptions { ApiUrl = "http://10.0.2.2:4000", LogLevel = LogLevel.Debug };
            HeroSignal.Initialize("app_1", options);
            options.ApiUrl = "https://changed.example.com";
            HeroSignal.Initialize("app_1", options);

            Assert.IsTrue(HeroSignal.IsInitialized);
            Assert.AreEqual(1, _bridge.InitializeCount);
            Assert.AreEqual("http://10.0.2.2:4000", _bridge.Options.ApiUrl);
            Assert.AreEqual(LogLevel.Debug, HeroSignal.Debug.LogLevel);

            HeroSignal.Initialize("app_2", options);
            Assert.AreEqual(2, _bridge.InitializeCount);
            Assert.AreEqual("app_2", _bridge.AppId);
        }

        [Test]
        public void Login_logout_and_consent_are_forwarded()
        {
            Init();
            Assert.Throws<ArgumentException>(() => HeroSignal.Login(" "));
            HeroSignal.Login("user_42");
            HeroSignal.Login("user_42", "");
            HeroSignal.Login("user_42", "hash");
            HeroSignal.Logout();
            HeroSignal.SetConsentGiven(true);
            CollectionAssert.AreEqual(new[] { "login:user_42:-", "login:user_42:-", "login:user_42:hash", "logout", "consent:True" }, _bridge.Calls);
        }

        [Test]
        public void User_methods_validate_and_normalize()
        {
            Init();
            HeroSignal.User.AddTag("plan", "pro");
            Assert.AreEqual("pro", _bridge.LastTags["plan"]);
            Assert.Throws<ArgumentException>(() => HeroSignal.User.AddTag(" ", "x"));
            Assert.Throws<ArgumentException>(() => HeroSignal.User.AddTag("k", null));
            HeroSignal.User.RemoveTags(new[] { "a", "b" });
            CollectionAssert.AreEqual(new[] { "a", "b" }, _bridge.LastKeys);

            HeroSignal.User.AddAlias("crm_id", "  42 ");
            Assert.AreEqual("42", _bridge.LastAliases["crm_id"]);
            Assert.Throws<ArgumentException>(() => HeroSignal.User.AddAlias("external_id", "1"));
            var tooMany = Enumerable.Range(0, Validation.MaxAliasesPerRequest + 1).ToDictionary(i => "a" + i, i => "v");
            Assert.Throws<ArgumentException>(() => HeroSignal.User.AddAliases(tooMany));

            HeroSignal.User.AddEmail(" Ali@Example.COM ");
            HeroSignal.User.AddSms("+90 (532) 123-45-67");
            HeroSignal.User.SetLanguage(" tr ");
            CollectionAssert.Contains(_bridge.Calls, "addEmail:ali@example.com");
            CollectionAssert.Contains(_bridge.Calls, "addSms:+905321234567");
            CollectionAssert.Contains(_bridge.Calls, "language:tr");
            Assert.Throws<ArgumentException>(() => HeroSignal.User.AddEmail("geçersiz"));
            Assert.Throws<ArgumentException>(() => HeroSignal.User.AddSms("123"));
            Assert.AreEqual("user_1", HeroSignal.User.ExternalId);
            Assert.AreEqual(1, HeroSignal.User.Emails.Count);
        }

        [Test]
        public void Events_are_posted_to_main_thread_dispatcher()
        {
            Init();
            var clicks = new List<NotificationClickedEventArgs>();
            HeroSignal.Notifications.Clicked += (_, e) => clicks.Add(e);
            _bridge.Sink.OnNotificationClicked(new NotificationClickedEventArgs(new HeroSignalNotification(), "btn", null));

            Assert.AreEqual(0, clicks.Count, "olay ana iş parçacığı döngüsünden önce çalışmamalı");
            Assert.AreEqual(1, _main.Pending);
            _main.RunAll();
            Assert.AreEqual("btn", clicks.Single().ActionId);
        }

        [Test]
        public void Click_before_subscription_is_delivered_to_first_handler_only()
        {
            Init();
            var click = new NotificationClickedEventArgs(new HeroSignalNotification { Title = "T" }, null, "https://x");
            _bridge.Sink.OnNotificationClicked(click);
            _main.RunAll();

            var first = new List<NotificationClickedEventArgs>();
            var second = new List<NotificationClickedEventArgs>();
            HeroSignal.Notifications.Clicked += (_, e) => first.Add(e);
            HeroSignal.Notifications.Clicked += (_, e) => second.Add(e);
            Assert.AreSame(click, first.Single());
            Assert.AreEqual(0, second.Count);
        }

        [Test]
        public void Handler_exception_does_not_stop_other_handlers()
        {
            Init();
            var reached = false;
            HeroSignal.Notifications.Clicked += (_, __) => throw new InvalidOperationException("boom");
            HeroSignal.Notifications.Clicked += (_, __) => reached = true;
            _bridge.Sink.OnNotificationClicked(new NotificationClickedEventArgs(new HeroSignalNotification(), null, null));
            _main.RunAll();
            Assert.IsTrue(reached);
        }

        [Test]
        public void Foreground_without_handlers_leaves_native_default()
        {
            Init();
            var prevented = 0;
            var displayed = 0;
            _bridge.Sink.OnNotificationWillDisplay(new HeroSignalNotification(), () => prevented++, () => displayed++);
            _main.RunAll();
            Assert.AreEqual(0, prevented);
            Assert.AreEqual(0, displayed);
        }

        [Test]
        public void Foreground_with_handler_defers_and_displays_unless_prevented()
        {
            Init();
            var prevented = 0;
            var displayed = 0;
            var block = false;
            HeroSignal.Notifications.ForegroundWillDisplay += (_, e) =>
            {
                if (block) e.PreventDefault();
            };

            _bridge.Sink.OnNotificationWillDisplay(new HeroSignalNotification(), () => prevented++, () => displayed++);
            Assert.AreEqual(1, prevented, "karar ana iş parçacığına taşınırken yerel gösterim hemen ertelenmeli");
            Assert.AreEqual(0, displayed);
            _main.RunAll();
            Assert.AreEqual(1, displayed);

            block = true;
            _bridge.Sink.OnNotificationWillDisplay(new HeroSignalNotification(), () => prevented++, () => displayed++);
            _main.RunAll();
            Assert.AreEqual(2, prevented);
            Assert.AreEqual(1, displayed);
        }

        [Test]
        public void Foreground_display_runs_once()
        {
            var count = 0;
            var args = new NotificationWillDisplayEventArgs(new HeroSignalNotification(), () => count++);
            args.PreventDefault();
            args.Display();
            args.Display();
            Assert.IsTrue(args.IsPrevented);
            Assert.AreEqual(1, count);
        }

        [Test]
        public void Push_subscription_and_permission_events_fire_only_on_change()
        {
            Init();
            var changes = new List<PushSubscriptionChangedEventArgs>();
            var permissions = new List<bool>();
            HeroSignal.User.PushSubscription.Changed += (_, e) => changes.Add(e);
            HeroSignal.Notifications.PermissionChanged += (_, e) => permissions.Add(e.Permission);

            var state = new PushSubscriptionState("sub_1", "tok", true, true);
            _bridge.Sink.OnPushSubscriptionChanged(state);
            _bridge.Sink.OnPushSubscriptionChanged(new PushSubscriptionState("sub_1", "tok", true, true));
            _bridge.Sink.OnPushSubscriptionChanged(new PushSubscriptionState("sub_1", "tok", false, false));
            _bridge.Sink.OnPermissionChanged(false);
            _main.RunAll();

            Assert.AreEqual(2, changes.Count);
            Assert.IsNull(changes[0].Previous.Id);
            Assert.IsFalse(changes[1].Current.OptedIn);
            CollectionAssert.AreEqual(new[] { true, false }, permissions);
        }

        [Test]
        public void Notification_tray_methods_validate_and_forward()
        {
            Init();
            HeroSignal.Notifications.ClearAll();
            HeroSignal.Notifications.RemoveNotification(42);
            HeroSignal.Notifications.RemoveNotification(" dlv_1 ");
            HeroSignal.Notifications.RemoveGroupedNotifications("orders");
            CollectionAssert.AreEqual(new[] { "clearAll", "removeNotification:42", "removeNotification:dlv_1", "removeGroup:orders" }, _bridge.Calls);
            Assert.Throws<ArgumentException>(() => HeroSignal.Notifications.RemoveNotification(""));
            Assert.Throws<ArgumentException>(() => HeroSignal.Notifications.RemoveGroupedNotifications(" "));
            Assert.IsTrue(HeroSignal.Notifications.RequestPermissionAsync(true).Result);
            CollectionAssert.Contains(_bridge.Calls, "requestPermission:True");
        }

        [Test]
        public void In_app_triggers_and_lifecycle_are_routed()
        {
            Init();
            HeroSignal.InAppMessages.AddTrigger("level", "gold");
            HeroSignal.InAppMessages.AddTriggers(new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });
            HeroSignal.InAppMessages.RemoveTrigger("a");
            CollectionAssert.AreEquivalent(new Dictionary<string, string> { ["level"] = "gold", ["b"] = "2" }, HeroSignal.InAppMessages.GetTriggers());
            Assert.Throws<ArgumentException>(() => HeroSignal.InAppMessages.AddTrigger(new string('k', 65), "v"));
            Assert.Throws<ArgumentException>(() => HeroSignal.InAppMessages.AddTrigger("k", new string('v', 201)));
            HeroSignal.InAppMessages.SetScreen(" shop ");
            HeroSignal.InAppMessages.SetScreen(null);
            CollectionAssert.AreEqual(new[] { "screen:shop", "screen:-" }, _bridge.Calls);
            HeroSignal.InAppMessages.Paused = true;
            Assert.IsTrue(HeroSignal.InAppMessages.Paused);

            var phases = new List<string>();
            HeroSignal.InAppMessages.WillDisplay += (_, e) => phases.Add("will:" + e.Message.Id);
            HeroSignal.InAppMessages.DidDismiss += (_, e) => phases.Add("dismiss:" + e.Message.Id);
            InAppMessageClickedEventArgs clicked = null;
            HeroSignal.InAppMessages.Clicked += (_, e) => clicked = e;
            _bridge.Sink.OnInAppLifecycle(InAppLifecyclePhase.WillDisplay, new InAppMessage("m1", null));
            _bridge.Sink.OnInAppLifecycle(InAppLifecyclePhase.DidDismiss, new InAppMessage("m1", null));
            _bridge.Sink.OnInAppClicked(new InAppMessageClickedEventArgs("m1", "buy", null));
            _main.RunAll();
            CollectionAssert.AreEqual(new[] { "will:m1", "dismiss:m1" }, phases);
            Assert.AreEqual("buy", clicked.ActionId);
        }

        [Test]
        public void Inbox_validates_and_forwards()
        {
            Init();
            Assert.Throws<ArgumentOutOfRangeException>(() => HeroSignal.Inbox.ListAsync(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => HeroSignal.Inbox.ListAsync(201));
            HeroSignal.Inbox.ListAsync(20, "").Wait();
            Assert.AreEqual(20, _bridge.LastInboxLimit);
            Assert.IsNull(_bridge.LastInboxCursor);
            HeroSignal.Inbox.ListAsync(cursor: "c1").Wait();
            Assert.AreEqual(50, _bridge.LastInboxLimit);
            Assert.AreEqual("c1", _bridge.LastInboxCursor);
            Assert.IsTrue(HeroSignal.Inbox.MarkReadAsync("i1").Result.Read);
            Assert.Throws<ArgumentException>(() => HeroSignal.Inbox.MarkReadAsync(" "));
            HeroSignal.Inbox.ArchiveAsync("i2").Wait();
            Assert.AreEqual(3, HeroSignal.Inbox.UnreadCountAsync().Result);
        }

        [Test]
        public void Inbox_reports_not_initialized_and_unsupported()
        {
            var e = Assert.Throws<HeroSignalException>(() => HeroSignal.Inbox.UnreadCountAsync());
            Assert.AreEqual(HeroSignalException.NotInitialized, e.Code);
            _bridge.IsSupported = false;
            Init();
            e = Assert.Throws<HeroSignalException>(() => HeroSignal.Inbox.ListAsync());
            Assert.AreEqual(HeroSignalException.PlatformNotSupported, e.Code);
        }

        [Test]
        public void Session_send_event_validates_and_normalizes()
        {
            Init();
            HeroSignal.Session.SendEvent(" level_complete ", new Dictionary<string, object>
            {
                ["level"] = 3,
                ["score"] = 12.5f,
                ["tags"] = new[] { "a", "b" },
                ["missing"] = null,
            }, 4.99);
            Assert.AreEqual("event:level_complete", _bridge.Calls.Single());
            Assert.AreEqual(3L, _bridge.LastEventProperties["level"]);
            Assert.AreEqual(12.5, _bridge.LastEventProperties["score"]);
            CollectionAssert.AreEqual(new List<object> { "a", "b" }, (List<object>)_bridge.LastEventProperties["tags"]);
            Assert.IsNull(_bridge.LastEventProperties["missing"]);
            Assert.AreEqual(4.99, _bridge.LastEventValue);

            Assert.Throws<ArgumentException>(() => HeroSignal.Session.SendEvent("bad name"));
            Assert.Throws<ArgumentException>(() => HeroSignal.Session.SendEvent(new string('e', 101)));
            Assert.Throws<ArgumentException>(() => HeroSignal.Session.SendEvent("ok", null, double.NaN));
            Assert.Throws<ArgumentException>(() => HeroSignal.Session.SendEvent("ok", new Dictionary<string, object> { ["s"] = new MemoryStream() }));
        }

        [Test]
        public void Debug_log_level_is_forwarded_after_initialize()
        {
            HeroSignal.Debug.SetLogLevel(LogLevel.Info);
            Assert.AreEqual(LogLevel.Info, HeroSignal.Debug.LogLevel);
            Assert.IsNull(_bridge.LastLogLevel);
            Init(o => o.LogLevel = LogLevel.Warn);
            HeroSignal.Debug.SetLogLevel(LogLevel.Debug);
            Assert.AreEqual(LogLevel.Debug, _bridge.LastLogLevel);
            Assert.Throws<ArgumentOutOfRangeException>(() => HeroSignal.Debug.SetLogLevel((LogLevel)42));
        }

        [Test]
        public void Unsupported_bridge_is_no_op()
        {
            HeroSignal.ResetForTests(new UnsupportedBridge("Test"), _main);
            HeroSignal.Initialize("app_1");
            Assert.IsFalse(HeroSignal.IsSupported);
            HeroSignal.Login("user");
            HeroSignal.User.AddTag("a", "b");
            HeroSignal.Notifications.ClearAll();
            HeroSignal.Notifications.RemoveNotification(1);
            HeroSignal.InAppMessages.Paused = true;
            HeroSignal.Session.SendEvent("opened");
            Assert.IsFalse(HeroSignal.InAppMessages.Paused);
            Assert.IsFalse(HeroSignal.Notifications.RequestPermissionAsync().Result);
            Assert.IsNull(HeroSignal.User.PushSubscription.Id);
            var e = Assert.Throws<HeroSignalException>(() => HeroSignal.Inbox.UnreadCountAsync());
            Assert.AreEqual(HeroSignalException.PlatformNotSupported, e.Code);
        }
    }
}
