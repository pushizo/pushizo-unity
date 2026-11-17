#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PushizoSdk.Internal;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PushizoSdk.Tests
{
    /// <summary>
    /// Yerel SDK eşitliği: profille giriş, reklam rızası ve kimliği, rozet, konum izin durumları, arka plan konum izni, ADM
    /// kaydı, sohbet dinleme durumu; kullanıcı özelliği, olay kimliği, bayrak ve kutu gözlemcileri, hazır sohbet ekranı.
    /// </summary>
    public sealed class NativeParityTests
    {
        private FakeNativeBridge _bridge = null!;

        [SetUp]
        public void SetUp()
        {
            _bridge = new FakeNativeBridge();
            Pushizo.ResetForTests(_bridge, new ImmediateDispatcher());
        }

        [TearDown]
        public void TearDown() => Pushizo.ResetForTests(new EditorNativeBridge(), new ImmediateDispatcher());

        private static void Init() => Pushizo.Initialize("app_test", new PushizoOptions { ApiUrl = "https://api.pushizo.com" });

        private string LastCall => _bridge.Calls[_bridge.Calls.Count - 1];

        private static string FaultCode(System.Threading.Tasks.Task task) => ((PushizoException)task.Exception!.InnerException!).Code;

        [Test]
        public void Login_with_profile_validates_before_login_and_serializes_normalized_profile()
        {
            Assert.Throws<InvalidOperationException>(() => Pushizo.Login("user_1", new PushizoUserProfile { Email = "a@b.co" }));
            Init();

            Pushizo.Login("user_42", new PushizoUserProfile
            {
                Email = " Ali@Ornek.com ",
                Sms = "+90 (532) 123-45-67",
                Tags = new Dictionary<string, string> { ["plan"] = "pro" },
                Aliases = new Dictionary<string, string> { ["crm_id"] = " 42 " },
            }, identityHash: "hash_abc", jwt: "a.b.c");

            StringAssert.StartsWith("loginWithProfile:user_42:", LastCall);
            StringAssert.EndsWith(":hash_abc:jwt=a.b.c", LastCall);
            var json = LastCall.Substring("loginWithProfile:user_42:".Length, LastCall.Length - "loginWithProfile:user_42:".Length - ":hash_abc:jwt=a.b.c".Length);
            var profile = Json.ParseObject(json);
            Assert.AreEqual("ali@ornek.com", profile["email"]);
            Assert.AreEqual("+905321234567", profile["sms"]);
            Assert.AreEqual("pro", ((Dictionary<string, object?>)profile["tags"]!)["plan"]);
            Assert.AreEqual("42", ((Dictionary<string, object?>)profile["aliases"]!)["crm_id"]);

            // Geçersiz profil girişten önce reddedilir: köprüye hiçbir çağrı gitmez.
            var before = _bridge.Calls.Count;
            Assert.Throws<ArgumentException>(() => Pushizo.Login("user_43", new PushizoUserProfile { Email = "gecersiz" }));
            Assert.Throws<ArgumentException>(() => Pushizo.Login("user_43", new PushizoUserProfile { Sms = "12" }));
            Assert.Throws<ArgumentException>(() => Pushizo.Login(" ", new PushizoUserProfile { Email = "a@b.co" }));
            Assert.AreEqual(before, _bridge.Calls.Count);

            // Boş profil düz girişe düşer; kanıt hatası NOT_READY olarak fırlatılır.
            Pushizo.Login("user_44", new PushizoUserProfile());
            Assert.AreEqual("login:user_44:<null>", LastCall);
            _bridge.LoginError = "Bu app için kimlik JWT'si zorunlu.";
            var failed = Assert.Throws<PushizoException>(() => Pushizo.Login("user_45", new PushizoUserProfile { Email = "a@b.co" }));
            Assert.AreEqual(PushizoException.NotReady, failed!.Code);
        }

        [Test]
        public void Existing_login_overload_still_binds_with_null_proof()
        {
            Init();
            Pushizo.Login("user_1", null);
            Assert.AreEqual("login:user_1:<null>", LastCall);
        }

        [Test]
        public void Ads_consent_and_consents_round_trip()
        {
            Init();
            _bridge.PreferencesJson =
                @"{""preferences"":[{""category"":""ads_personalization"",""channel"":null,""optedIn"":true,""explicit"":true,""source"":""sdk"",""updatedAt"":1759838400000}],""categories"":[]}";
            var set = Pushizo.User.SetAdsPersonalizationConsentAsync(true).Result;
            Assert.AreEqual("setAdsPersonalizationConsent:True", LastCall);
            Assert.AreEqual("ads_personalization", set.Preferences[0].Category);
            Assert.IsTrue(set.Preferences[0].OptedIn);

            _bridge.ConsentsJson =
                @"{""consents"":[{""purpose"":""ads_personalization"",""granted"":true,""source"":""sdk"",""updatedAt"":1759838400000},{""purpose"":""other"",""granted"":false,""source"":null,""updatedAt"":null},{""granted"":true}]}";
            var consents = Pushizo.User.GetConsentsAsync().Result;
            Assert.AreEqual("getConsents", LastCall);
            Assert.AreEqual(2, consents.Count);
            Assert.AreEqual("ads_personalization", consents[0].Purpose);
            Assert.IsTrue(consents[0].Granted);
            Assert.AreEqual("sdk", consents[0].Source);
            Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1759838400000), consents[0].UpdatedAt);
            Assert.IsFalse(consents[1].Granted);
            Assert.IsNull(consents[1].Source);
            Assert.IsNull(consents[1].UpdatedAt);

            _bridge.PreferencesErrorCode = "NOT_READY";
            Assert.AreEqual("NOT_READY", FaultCode(Pushizo.User.GetConsentsAsync()));
            Assert.AreEqual("NOT_READY", FaultCode(Pushizo.User.SetAdsPersonalizationConsentAsync(false)));
        }

        [Test]
        public void Advertising_id_badge_and_adm_are_validated_and_forwarded()
        {
            Assert.Throws<InvalidOperationException>(() => Pushizo.Ads.SetAdvertisingId("x"));
            Init();

            Pushizo.Ads.SetAdvertisingId(" 38400000-8cf0-11bd-b23e-10b96e40000d ");
            Assert.AreEqual("advertisingId:38400000-8cf0-11bd-b23e-10b96e40000d", LastCall);
            Pushizo.Ads.SetAdvertisingId(null);
            Assert.AreEqual("advertisingId:<null>", LastCall);
            Pushizo.Ads.SetAdvertisingId("  ");
            Assert.AreEqual("advertisingId:<null>", LastCall);

            Pushizo.Notifications.SetBadgeCount(3);
            Assert.AreEqual("badge:3", LastCall);
            Pushizo.Notifications.ClearBadge();
            Assert.AreEqual("badge:0", LastCall);
            Assert.Throws<ArgumentOutOfRangeException>(() => Pushizo.Notifications.SetBadgeCount(-1));

            Pushizo.User.RegisterAdm("  amzn1.adm-registration.v3.abc  ");
            Assert.AreEqual("registerAdm:amzn1.adm-registration.v3.abc", LastCall);
            var before = _bridge.Calls.Count;
            Assert.Throws<ArgumentException>(() => Pushizo.User.RegisterAdm("kisa"));
            Assert.Throws<ArgumentException>(() => Pushizo.User.RegisterAdm(null!));
            Assert.AreEqual(before, _bridge.Calls.Count);
            _bridge.RegisterAdmError = "Geçersiz ADM kayıt kimliği";
            Assert.Throws<ArgumentException>(() => Pushizo.User.RegisterAdm("amzn1.adm-registration.v3.abc"));
        }

        [Test]
        public void Location_permission_state_and_background_request()
        {
            Assert.IsFalse(Pushizo.Location.PermissionGranted);
            Assert.IsFalse(Pushizo.Location.BackgroundPermissionGranted);
            Assert.Throws<InvalidOperationException>(() => Pushizo.Location.RequestBackgroundPermissionAsync());
            Init();

            _bridge.LocationPermissionGrantedValue = true;
            Assert.IsTrue(Pushizo.Location.PermissionGranted);
            Assert.IsFalse(Pushizo.Location.BackgroundPermissionGranted);
            _bridge.LocationBackgroundPermissionGrantedValue = true;
            Assert.IsTrue(Pushizo.Location.BackgroundPermissionGranted);

            Assert.IsTrue(Pushizo.Location.RequestBackgroundPermissionAsync("  Mağazaya yaklaşınca haber veririz. ").Result);
            Assert.AreEqual("requestBackgroundLocationPermission:Mağazaya yaklaşınca haber veririz.", LastCall);
            _bridge.BackgroundLocationPermissionResult = false;
            Assert.IsFalse(Pushizo.Location.RequestAlwaysPermissionAsync().Result);
            Assert.AreEqual("requestBackgroundLocationPermission:<null>", LastCall);
        }

        [Test]
        public void Chat_is_listening_reads_native_state()
        {
            Init();
            _bridge.ChatResponses["isListening"] = new KeyValuePair<string?, string?>(@"{""listening"":true}", null);
            Assert.IsTrue(Pushizo.Chat.IsListeningAsync().Result);
            Assert.AreEqual("isListening", _bridge.ChatCalls[_bridge.ChatCalls.Count - 1].Key);
            _bridge.ChatResponses["isListening"] = new KeyValuePair<string?, string?>(@"{""listening"":false}", null);
            Assert.IsFalse(Pushizo.Chat.IsListeningAsync().Result);
        }

        [Test]
        public void Editor_bridge_is_inert_for_platform_only_apis()
        {
            Pushizo.ResetForTests(new EditorNativeBridge(), new ImmediateDispatcher());
            Init();

            Pushizo.Login("user_1", new PushizoUserProfile
            {
                Email = "ali@pushizo.com", Sms = "+905321234567", Tags = new Dictionary<string, string> { ["plan"] = "pro" },
            });
            Assert.AreEqual("user_1", Pushizo.User.ExternalId);
            CollectionAssert.AreEqual(new[] { "ali@pushizo.com" }, Pushizo.User.Emails);
            CollectionAssert.AreEqual(new[] { "+905321234567" }, Pushizo.User.SmsNumbers);
            Assert.AreEqual("pro", Pushizo.User.GetTags()["plan"]);

            Assert.DoesNotThrow(() => Pushizo.Ads.SetAdvertisingId("38400000-8cf0-11bd-b23e-10b96e40000d"));
            Assert.DoesNotThrow(() => Pushizo.Notifications.SetBadgeCount(2));
            Assert.DoesNotThrow(() => Pushizo.Notifications.ClearBadge());
            Assert.DoesNotThrow(() => Pushizo.User.RegisterAdm("amzn1.adm-registration.v3.abc"));
            Assert.IsFalse(Pushizo.Location.PermissionGranted);
            Assert.IsFalse(Pushizo.Location.BackgroundPermissionGranted);
            Assert.IsFalse(Pushizo.Location.RequestBackgroundPermissionAsync().Result);
            Assert.IsFalse(Pushizo.Location.RequestAlwaysPermissionAsync().Result);
            Assert.IsFalse(Pushizo.Chat.IsListeningAsync().Result);
            // Tercih ucu Editor'de yoktur: rıza çağrıları diğer tercih çağrılarıyla aynı kodu verir.
            Assert.AreEqual(PushizoException.PlatformNotSupported, FaultCode(Pushizo.User.SetAdsPersonalizationConsentAsync(true)));
            Assert.AreEqual(PushizoException.PlatformNotSupported, FaultCode(Pushizo.User.GetConsentsAsync()));
        }

        private static string PackageFile(string relative)
        {
            var path = Path.GetFullPath(Path.Combine("Packages", "com.pushizo.unity", relative));
            Assert.IsTrue(File.Exists(path), "Paket dosyası bulunamadı: " + path);
            return File.ReadAllText(path);
        }

        [Test]
        public void Native_bridges_call_the_native_sdk_and_single_platform_apis_stay_inert()
        {
            var java = PackageFile("Runtime/Plugins/Android/PushizoUnityBridge.java");
            StringAssert.Contains("Pushizo.login(externalId, profile, emptyToNull(identityHash), emptyToNull(jwt))", java);
            StringAssert.Contains("new HsPreferenceInput(HsConsents.ADS_PERSONALIZATION, null, granted)", java);
            StringAssert.Contains("((HsPreferencesResult) value).getConsents()", java);
            StringAssert.Contains("Pushizo.Ads.setAdvertisingId(emptyToNull(id))", java);
            StringAssert.Contains("Pushizo.User.registerAdm(token)", java);
            StringAssert.Contains("Pushizo.Location.getPermissionGranted()", java);
            StringAssert.Contains("Pushizo.Location.getBackgroundPermissionGranted()", java);
            StringAssert.Contains("Manifest.permission.ACCESS_BACKGROUND_LOCATION", java);
            StringAssert.Contains("R.string.hs_geofence_rationale_title", java);
            StringAssert.Contains("chat.isListening()", java);

            var swift = PackageFile("Runtime/Plugins/iOS/PushizoUnityBridge.swift");
            // Profille giriş yerel çağrıdır; profil ve kanıt doğrulaması (girişten önce) yerel SDK'dadır.
            StringAssert.Contains("try Pushizo.login(id, profile: profile, identityHash: hsuNonEmpty(identityHash), jwt: hsuNonEmpty(jwt))", swift);
            StringAssert.Contains("catch HSError.invalidArgument(let message)", swift);
            StringAssert.DoesNotContain("HSContacts.normalize", swift);
            StringAssert.Contains("Pushizo.User.setAdsPersonalizationConsent(value)", swift);
            StringAssert.Contains("Pushizo.User.getConsents()", swift);
            StringAssert.Contains("Pushizo.Ads.setAdvertisingId(hsuNonEmpty(id))", swift);
            StringAssert.Contains("Pushizo.Notifications.setBadgeCount(", swift);
            StringAssert.Contains("Pushizo.Location.requestAlwaysPermission", swift);
            // Konum izin durumu yerel API'den okunur; köprü kendi CLLocationManager'ını kullanmaz.
            StringAssert.Contains("Pushizo.Location.permissionGranted ? 1 : 0", swift);
            StringAssert.Contains("Pushizo.Location.backgroundPermissionGranted ? 1 : 0", swift);
            StringAssert.DoesNotContain("CLLocationManager", swift);
            StringAssert.DoesNotContain("import CoreLocation", swift);
            StringAssert.Contains("Pushizo.Chat.isListening", swift);

            // Tek platformlu API'ler diğer platformda yerel çağrı yapmaz.
            var ios = PackageFile("Runtime/Internal/IosNativeBridge.cs");
            StringAssert.Contains("public string? RegisterAdm(string token) => null;", ios);
            // Rozet Android'de yerel çağrıya bağlıdır.
            var android = PackageFile("Runtime/Internal/AndroidNativeBridge.cs");
            StringAssert.Contains("Call<bool>(\"setBadgeCount\", count)", android);
            StringAssert.Contains("Pushizo.Notifications.setBadgeCount(count)", java);
            StringAssert.Contains("Pushizo.Notifications.INSTANCE.getBadgeCount()", java);
        }

        [Test]
        public void User_set_property_validates_and_serializes_the_value()
        {
            Assert.Throws<InvalidOperationException>(() => Pushizo.User.SetProperty("plan", "pro"));
            Init();

            Pushizo.User.SetProperty(" plan ", "pro");
            Assert.AreEqual(@"setUserProperty:plan:{""value"":""pro""}", LastCall);
            Pushizo.User.SetProperty("level", 12);
            Assert.AreEqual(@"setUserProperty:level:{""value"":12}", LastCall);
            Pushizo.User.SetProperty("vip", true);
            Assert.AreEqual(@"setUserProperty:vip:{""value"":true}", LastCall);
            Pushizo.User.SetProperty("score", 4.5);
            Assert.AreEqual(@"setUserProperty:score:{""value"":4.5}", LastCall);
            // null özelliği siler.
            Pushizo.User.SetProperty("plan", null);
            Assert.AreEqual(@"setUserProperty:plan:{""value"":null}", LastCall);

            Pushizo.User.SetProperty("prefs", new Dictionary<string, object?>
            {
                ["tags"] = new List<object?> { "a", 2L, false },
                ["nested"] = new Dictionary<string, object?> { ["k"] = "v" },
            });
            var json = LastCall.Substring("setUserProperty:prefs:".Length);
            var value = (Dictionary<string, object?>)Json.ParseObject(json)["value"]!;
            CollectionAssert.AreEqual(new object?[] { "a", 2L, false }, (List<object?>)value["tags"]!);
            Assert.AreEqual("v", ((Dictionary<string, object?>)value["nested"]!)["k"]);

            // Geçersiz anahtar/değer köprüye gitmez.
            var before = _bridge.Calls.Count;
            Assert.Throws<ArgumentException>(() => Pushizo.User.SetProperty(" ", "x"));
            Assert.Throws<ArgumentException>(() => Pushizo.User.SetProperty(null!, "x"));
            Assert.Throws<ArgumentException>(() => Pushizo.User.SetProperty(new string('k', 65), "x"));
            Assert.Throws<ArgumentException>(() => Pushizo.User.SetProperty("o", new object()));
            Assert.Throws<ArgumentException>(() => Pushizo.User.SetProperty("n", double.NaN));
            Assert.Throws<ArgumentException>(() => Pushizo.User.SetProperty("l", new List<object?> { "a", null }));
            Assert.Throws<ArgumentException>(() => Pushizo.User.SetProperty("m", new Dictionary<string, object?> { ["k"] = null }));
            Assert.AreEqual(before, _bridge.Calls.Count);
            Pushizo.User.SetProperty(new string('k', 64), "x");
            Assert.AreEqual(before + 1, _bridge.Calls.Count);

            // Yerel SDK'nın anahtar hatası ArgumentException olur.
            _bridge.UserPropertyError = "Özellik anahtarı boş olamaz.";
            var failed = Assert.Throws<ArgumentException>(() => Pushizo.User.SetProperty("plan", "pro"));
            StringAssert.Contains("Özellik anahtarı boş olamaz.", failed!.Message);
        }

        [Test]
        public void Send_event_forwards_optional_event_id_and_keeps_existing_signature()
        {
            Init();
            var props = new Dictionary<string, object?> { ["sku"] = "gems_100" };

            Pushizo.Session.SendEvent("purchase", props, 4.99);
            Assert.AreEqual("sendEvent:purchase", LastCall);
            Assert.IsNull(_bridge.LastEventId);
            Assert.IsTrue(_bridge.LastEventHasValue);

            Pushizo.Session.SendEvent("purchase", props, 4.99, " order-2026-0001 ");
            Assert.AreEqual("order-2026-0001", _bridge.LastEventId);
            Assert.AreEqual(@"{""sku"":""gems_100""}", _bridge.LastEventJson);

            Pushizo.Session.SendEvent("opened", null, null, "evt_12345678");
            Assert.AreEqual("evt_12345678", _bridge.LastEventId);
            Assert.IsFalse(_bridge.LastEventHasValue);

            // Boş kimlik yok sayılır; biçim denetimi yerel SDK'dadır (geçersizse kendi kimliğini üretir).
            Pushizo.Session.SendEvent("opened", null, null, "  ");
            Assert.IsNull(_bridge.LastEventId);
            Pushizo.Session.SendEvent("opened", null, null, null);
            Assert.IsNull(_bridge.LastEventId);
            Pushizo.Session.SendEvent("opened", null, null, "kısa");
            Assert.AreEqual("kısa", _bridge.LastEventId);

            Assert.Throws<ArgumentException>(() => Pushizo.Session.SendEvent(" ", null, null, "evt_12345678"));
            Assert.Throws<ArgumentException>(() => Pushizo.Session.SendEvent("x", null, double.NaN, "evt_12345678"));
        }

        [Test]
        public void Badge_result_and_stored_count_come_from_the_native_sdk()
        {
            Assert.AreEqual(0, Pushizo.Notifications.BadgeCount);
            Assert.Throws<InvalidOperationException>(() => Pushizo.Notifications.TrySetBadgeCount(1));
            Init();

            Assert.IsTrue(Pushizo.Notifications.TrySetBadgeCount(5));
            Assert.AreEqual("badge:5", LastCall);
            Assert.AreEqual(5, Pushizo.Notifications.BadgeCount);

            // Android: rozet gösterilemiyor (bildirimler kapalı vb.); sayı yine saklanır.
            _bridge.BadgeResult = false;
            Assert.IsFalse(Pushizo.Notifications.TrySetBadgeCount(7));
            Assert.AreEqual(7, Pushizo.Notifications.BadgeCount);

            // Mevcut void imza aynı çağrıyı yapar.
            Pushizo.Notifications.SetBadgeCount(2);
            Assert.AreEqual("badge:2", LastCall);
            Pushizo.Notifications.ClearBadge();
            Assert.AreEqual(0, Pushizo.Notifications.BadgeCount);
            Assert.Throws<ArgumentOutOfRangeException>(() => Pushizo.Notifications.TrySetBadgeCount(-1));
        }

        [Test]
        public void Feature_flags_changed_is_raised_from_the_native_observer()
        {
            Init();
            var count = 0;
            Action handler = () => count++;
            Pushizo.FeatureFlags.Changed += handler;
            // Hata veren abone diğerlerini engellemez.
            Pushizo.FeatureFlags.Changed += () => throw new InvalidOperationException("abone hatası");
            var second = 0;
            Pushizo.FeatureFlags.Changed += () => second++;

            var faulty = new Regex("FeatureFlags.Changed olay işleyicisi hata verdi");
            LogAssert.Expect(LogType.Error, faulty);
            _bridge.Emit("featureFlags", "{}");
            LogAssert.Expect(LogType.Error, faulty);
            _bridge.Emit("featureFlags", "{}");
            Assert.AreEqual(2, count);
            Assert.AreEqual(2, second);

            Pushizo.FeatureFlags.Changed -= handler;
            LogAssert.Expect(LogType.Error, faulty);
            _bridge.Emit("featureFlags", "{}");
            Assert.AreEqual(2, count);
            Assert.AreEqual(3, second);
        }

        [Test]
        public void Inbox_changed_maps_native_observer_events()
        {
            Init();
            var events = new List<InboxChangedEventArgs>();
            Action<InboxChangedEventArgs> handler = e => events.Add(e);
            Pushizo.Inbox.Changed += handler;

            _bridge.Emit("inbox", @"{""type"":""listed"",""itemId"":null,""unreadCount"":4}");
            _bridge.Emit("inbox", @"{""type"":""read"",""itemId"":""itm_1"",""unreadCount"":3}");
            _bridge.Emit("inbox", @"{""type"":""read_all"",""itemId"":null,""unreadCount"":0}");
            _bridge.Emit("inbox", @"{""type"":""archived"",""itemId"":""itm_2"",""unreadCount"":0}");
            _bridge.Emit("inbox", @"{""type"":""count"",""unreadCount"":9}");

            Assert.AreEqual(5, events.Count);
            Assert.AreEqual(InboxChangeType.Listed, events[0].Type);
            Assert.IsNull(events[0].ItemId);
            Assert.AreEqual(4, events[0].UnreadCount);
            Assert.AreEqual(InboxChangeType.Read, events[1].Type);
            Assert.AreEqual("itm_1", events[1].ItemId);
            Assert.AreEqual(3, events[1].UnreadCount);
            Assert.AreEqual(InboxChangeType.ReadAll, events[2].Type);
            Assert.AreEqual(InboxChangeType.Archived, events[3].Type);
            Assert.AreEqual("itm_2", events[3].ItemId);
            Assert.AreEqual(InboxChangeType.Count, events[4].Type);
            Assert.AreEqual(9, events[4].UnreadCount);

            // Tanınmayan tür yok sayılır; eksik alan ve bozuk yük günlüğe yazılır. Abone çağrılmaz.
            _bridge.Emit("inbox", @"{""type"":""yeni_tur"",""unreadCount"":1}");
            var failed = new Regex(@"yerel olay işlenemedi \(inbox\)");
            LogAssert.Expect(LogType.Error, failed);
            _bridge.Emit("inbox", @"{""type"":""read"",""itemId"":""itm_3""}");
            LogAssert.Expect(LogType.Error, failed);
            _bridge.Emit("inbox", "bozuk");
            Assert.AreEqual(5, events.Count);

            Pushizo.Inbox.Changed -= handler;
            _bridge.Emit("inbox", @"{""type"":""count"",""unreadCount"":1}");
            Assert.AreEqual(5, events.Count);
        }

        [Test]
        public void Chat_show_hide_and_is_presented_use_the_native_screen()
        {
            Assert.IsFalse(Pushizo.Chat.IsPresented);
            Assert.Throws<InvalidOperationException>(() => Pushizo.Chat.Show());
            Assert.Throws<InvalidOperationException>(() => Pushizo.Chat.Hide());
            Init();

            Assert.IsFalse(Pushizo.Chat.IsPresented);
            Assert.IsFalse(Pushizo.Chat.Hide());
            Pushizo.Chat.Show();
            Assert.AreEqual("chatShow", LastCall);
            Assert.IsTrue(Pushizo.Chat.IsPresented);
            Assert.IsTrue(Pushizo.Chat.Hide());
            Assert.AreEqual("chatHide", LastCall);
            Assert.IsFalse(Pushizo.Chat.IsPresented);
        }

        [Test]
        public void Editor_bridge_is_inert_for_round_three_apis()
        {
            Pushizo.ResetForTests(new EditorNativeBridge(), new ImmediateDispatcher());
            Init();

            Assert.DoesNotThrow(() => Pushizo.User.SetProperty("plan", "pro"));
            Assert.DoesNotThrow(() => Pushizo.User.SetProperty("plan", null));
            Assert.DoesNotThrow(() => Pushizo.Session.SendEvent("purchase", null, 4.99, "order-2026-0001"));
            Assert.IsFalse(Pushizo.Notifications.TrySetBadgeCount(3));
            Assert.AreEqual(0, Pushizo.Notifications.BadgeCount);
            Assert.DoesNotThrow(() => Pushizo.Chat.Show());
            Assert.IsFalse(Pushizo.Chat.IsPresented);
            Assert.IsFalse(Pushizo.Chat.Hide());
            Assert.DoesNotThrow(() => Pushizo.FeatureFlags.Changed += () => { });
            Assert.DoesNotThrow(() => Pushizo.Inbox.Changed += _ => { });
        }

        [Test]
        public void Reset_clears_observer_subscriptions()
        {
            Init();
            var raised = 0;
            Pushizo.FeatureFlags.Changed += () => raised++;
            Pushizo.Inbox.Changed += _ => raised++;

            Pushizo.ResetForTests(_bridge, new ImmediateDispatcher());
            Init();
            _bridge.Emit("featureFlags", "{}");
            _bridge.Emit("inbox", @"{""type"":""count"",""unreadCount"":1}");
            Assert.AreEqual(0, raised);
        }

        [Test]
        public void Round_three_bridges_call_the_native_sdk()
        {
            var java = PackageFile("Runtime/Plugins/Android/PushizoUnityBridge.java");
            StringAssert.Contains("Pushizo.User.setProperty(key, toValue(parseObject(valueJson).opt(\"value\")))", java);
            StringAssert.Contains("Pushizo.FeatureFlags.addObserver(FEATURE_FLAG_OBSERVER)", java);
            StringAssert.Contains("Pushizo.Inbox.addObserver(INBOX_OBSERVER)", java);
            StringAssert.Contains("event.getType(), \"itemId\", event.getItemId(), \"unreadCount\", event.getUnreadCount()", java);
            StringAssert.Contains("Pushizo.Session.sendEvent(name, properties, amount, id)", java);
            StringAssert.Contains("Pushizo.Session.sendEvent(name, properties, amount)", java);
            StringAssert.Contains("PushizoChat.show(activity)", java);
            StringAssert.Contains("PushizoChat.hide()", java);
            StringAssert.Contains("activity instanceof HsChatActivity", java);

            var swift = PackageFile("Runtime/Plugins/iOS/PushizoUnityBridge.swift");
            StringAssert.Contains("Pushizo.User.setProperty(name, value is NSNull ? nil : value)", swift);
            StringAssert.Contains("Pushizo.FeatureFlags.addObserver { hsuEmit(\"featureFlags\", [:]) }", swift);
            StringAssert.Contains("Pushizo.Inbox.addObserver { event in", swift);
            StringAssert.Contains("\"type\": event.type.rawValue, \"itemId\": event.itemId ?? NSNull(), \"unreadCount\": event.unreadCount", swift);
            StringAssert.Contains("value: hasValue != 0 ? value : nil, eventId: hsuNonEmpty(eventId))", swift);
            StringAssert.Contains("Pushizo.Chat.show()", swift);
            StringAssert.Contains("Pushizo.Chat.hide()", swift);
            StringAssert.Contains("Pushizo.Chat.isPresented", swift);

            // iOS'ta saklanan rozet sayısı yoktur: yerel çağrı (DllImport) eklenmez.
            var ios = PackageFile("Runtime/Internal/IosNativeBridge.cs");
            StringAssert.Contains("public int GetBadgeCount() => 0;", ios);
            StringAssert.DoesNotContain("hs_badge_count", ios);
        }
    }
}
