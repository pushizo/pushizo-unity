using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace PushizoSdk.Tests
{
    /// <summary>
    /// Kaynak sözleşme testleri: Swift (Runtime/Plugins/iOS) ve Java (Runtime/Plugins/Android) köprüleri Windows Editor'de
    /// derlenmediği için C# köprülerinin çağırdığı her yerel sembolün karşı tarafta tanımlı olduğu metin üzerinden
    /// doğrulanır (eksik sembol iOS'ta bağlama, Android'de çalışma zamanı hatası olurdu).
    /// </summary>
    public sealed class NativeBridgeContractTests
    {
        private static string PackageFile(string relative)
        {
            // Unity, "Packages/<ad>" sanal yolunu paketin fiziksel konumuna (file: bağımlılığı dahil) çözer.
            var path = Path.GetFullPath(Path.Combine("Packages", "com.pushizo.unity", relative));
            Assert.IsTrue(File.Exists(path), "Paket dosyası bulunamadı: " + path);
            return File.ReadAllText(path);
        }

        private static HashSet<string> Matches(string text, string pattern) =>
            new HashSet<string>(Regex.Matches(text, pattern).Cast<Match>().Select(m => m.Groups[1].Value));

        [Test]
        public void Every_ios_dll_import_has_a_swift_cdecl_export()
        {
            var imports = Matches(PackageFile("Runtime/Internal/IosNativeBridge.cs"), @"extern\s+[\w\[\]]+\s+(hs_\w+)\s*\(");
            var exports = Matches(PackageFile("Runtime/Plugins/iOS/PushizoUnityBridge.swift"), @"@_cdecl\(""(hs_\w+)""\)");

            CollectionAssert.IsNotEmpty(imports);
            CollectionAssert.IsSubsetOf(new[]
            {
                "hs_location_is_shared", "hs_location_set_shared", "hs_location_request_permission",
                "hs_location_is_geofencing_enabled", "hs_location_set_geofencing_enabled",
                "hs_feature_flags_refresh", "hs_feature_flags_is_enabled", "hs_feature_flags_value",
                "hs_placements_list", "hs_placements_report",
                "hs_login_with_profile", "hs_set_ads_personalization_consent", "hs_get_consents", "hs_set_advertising_id",
                "hs_set_badge_count", "hs_location_permission_granted", "hs_location_background_permission_granted",
                "hs_location_request_always_permission",
            }, imports);
            CollectionAssert.IsEmpty(imports.Except(exports), "Swift köprüsünde @_cdecl karşılığı olmayan DllImport");
        }

        [Test]
        public void Every_android_call_has_a_public_static_java_method()
        {
            var calls = Matches(PackageFile("Runtime/Internal/AndroidNativeBridge.cs"), @"Call(?:<\w+>)?\(""(\w+)""");
            var methods = Matches(PackageFile("Runtime/Plugins/Android/PushizoUnityBridge.java"), @"public\s+static\s+[\w<>\[\]]+\s+(\w+)\s*\(");

            CollectionAssert.IsNotEmpty(calls);
            CollectionAssert.IsSubsetOf(new[]
            {
                "locationShared", "setLocationShared", "requestLocationPermission",
                "geofencingEnabled", "setGeofencingEnabled",
                "featureFlagsRefresh", "featureFlagIsEnabled", "featureFlagValue",
                "placementsList", "placementsReport",
                "deliveryStatus", "deliveryShouldShowGuidance", "deliveryOpenSettings", "deliveryShowGuidance",
                "loginWithProfile", "registerAdm", "setAdsPersonalizationConsent", "getConsents", "setAdvertisingId",
                "locationPermissionGranted", "locationBackgroundPermissionGranted", "requestBackgroundLocationPermission",
            }, calls);
            CollectionAssert.IsEmpty(calls.Except(methods), "Java köprüsünde public static karşılığı olmayan çağrı");
        }

        [Test]
        public void Delivery_bridge_calls_native_delivery_api_and_ios_stays_inert()
        {
            var java = PackageFile("Runtime/Plugins/Android/PushizoUnityBridge.java");
            StringAssert.Contains("Pushizo.Delivery.status()", java);
            StringAssert.Contains("Pushizo.Delivery.shouldShowGuidance(minIntervalDays)", java);
            StringAssert.Contains("Pushizo.Delivery.openSettings(context, deliverySetting(setting))", java);
            StringAssert.Contains("Pushizo.Delivery.showGuidance(activity, deliverySetting(setting), emptyToNull(message), opened ->", java);
            // Köprü pil optimizasyonu iznini istemez (ADR-0092 §3).
            StringAssert.DoesNotContain("REQUEST_IGNORE_BATTERY_OPTIMIZATIONS", java);

            // C# ayrıştırıcısının okuduğu her alan Java JSON'unda yazılır.
            var parser = PackageFile("Runtime/Internal/BridgeJson.cs");
            var start = parser.IndexOf("public static DeliveryStatus ParseDeliveryStatus", System.StringComparison.Ordinal);
            var body = parser.Substring(start, parser.IndexOf("ParseStringMap", start, System.StringComparison.Ordinal) - start);
            var fields = Matches(body, @"(?:Str|Bool|TryGetValue)\((?:root, )?""(\w+)""");
            Assert.AreEqual(16, fields.Count);
            foreach (var field in fields) StringAssert.Contains('"' + field + '"', java);

            // iOS'ta teşhis yoktur: yerel çağrı (DllImport) eklenmez, köprü sabit yanıt verir.
            var ios = PackageFile("Runtime/Internal/IosNativeBridge.cs");
            StringAssert.Contains("public string? DeliveryStatus() => null;", ios);
            StringAssert.Contains("public bool DeliveryOpenSettings(string setting) => false;", ios);
            StringAssert.Contains("public void DeliveryShowGuidance(string setting, string? message, Action<bool> completion) => completion(false);", ios);
        }
    }
}
