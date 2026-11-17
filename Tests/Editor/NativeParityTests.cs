#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using PushizoSdk.Internal;
using NUnit.Framework;

namespace PushizoSdk.Tests
{
    /// <summary>
    /// Yerel SDK eşitliği: profille giriş, reklam rızası ve kimliği, rozet, konum izin durumları, arka plan konum izni, ADM
    /// kaydı ve sohbet dinleme durumu.
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
            StringAssert.Contains("HSContacts.normalizeEmail(email) == nil", swift);
            StringAssert.Contains("HSContacts.normalizePhone(sms) == nil", swift);
            StringAssert.Contains("Pushizo.User.setAdsPersonalizationConsent(value)", swift);
            StringAssert.Contains("Pushizo.User.getConsents()", swift);
            StringAssert.Contains("Pushizo.Ads.setAdvertisingId(hsuNonEmpty(id))", swift);
            StringAssert.Contains("Pushizo.Notifications.setBadgeCount(", swift);
            StringAssert.Contains("Pushizo.Location.requestAlwaysPermission", swift);
            StringAssert.Contains("CLLocationManager().authorizationStatus", swift);
            StringAssert.Contains("Pushizo.Chat.isListening", swift);
            // Profil doğrulaması girişten önce yapılır (Android ile aynı sıra).
            var loginWithProfile = swift.Substring(swift.IndexOf("func hs_login_with_profile", StringComparison.Ordinal));
            Assert.Less(loginWithProfile.IndexOf("HSContacts.normalizeEmail", StringComparison.Ordinal),
                loginWithProfile.IndexOf("Pushizo.login(id", StringComparison.Ordinal));

            // Tek platformlu API'ler diğer platformda yerel çağrı yapmaz.
            var ios = PackageFile("Runtime/Internal/IosNativeBridge.cs");
            StringAssert.Contains("public string? RegisterAdm(string token) => null;", ios);
            var android = PackageFile("Runtime/Internal/AndroidNativeBridge.cs");
            StringAssert.DoesNotContain("\"setBadgeCount\"", android);
        }
    }
}
