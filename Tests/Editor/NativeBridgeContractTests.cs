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
            CollectionAssert.IsSubsetOf(new[] { "hs_location_is_shared", "hs_location_set_shared", "hs_location_request_permission" }, imports);
            CollectionAssert.IsEmpty(imports.Except(exports), "Swift köprüsünde @_cdecl karşılığı olmayan DllImport");
        }

        [Test]
        public void Every_android_call_has_a_public_static_java_method()
        {
            var calls = Matches(PackageFile("Runtime/Internal/AndroidNativeBridge.cs"), @"Call(?:<\w+>)?\(""(\w+)""");
            var methods = Matches(PackageFile("Runtime/Plugins/Android/PushizoUnityBridge.java"), @"public\s+static\s+[\w<>\[\]]+\s+(\w+)\s*\(");

            CollectionAssert.IsNotEmpty(calls);
            CollectionAssert.IsSubsetOf(new[] { "locationShared", "setLocationShared", "requestLocationPermission" }, calls);
            CollectionAssert.IsEmpty(calls.Except(methods), "Java köprüsünde public static karşılığı olmayan çağrı");
        }
    }
}
