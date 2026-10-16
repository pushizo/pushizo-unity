#if UNITY_ANDROID
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using PushizoSdk.Internal;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PushizoSdk.Editor
{
    /// <summary>
    /// Android derleme öncesi/sonrası: minSdk denetimi ve <c>google-services.json</c> → Firebase kaynak değerleri.
    /// Firebase Unity SDK'sı veya google-services Gradle eklentisi olmadan FCM'nin başlaması için gereken
    /// <c>google_app_id</c>, <c>gcm_defaultSenderId</c>, <c>google_api_key</c>, <c>project_id</c> değerleri
    /// unityLibrary modülüne kaynak dosyası olarak yazılır (google-services eklentisinin ürettiğinin aynısı).
    /// Yerel SDK bağımlılıkları EDM4U ile çözülür (Editor/PushizoDependencies.xml).
    /// </summary>
    public sealed class PushizoAndroidPostProcessor : IPreprocessBuildWithReport, IPostGenerateGradleAndroidProject
    {
        private const int MinSdk = 23;
        private const string ValuesFileName = "pushizo_google_services.xml";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            if ((int)PlayerSettings.Android.minSdkVersion < MinSdk)
                Debug.LogWarning($"[Pushizo] Android minimum API {MinSdk} gerekir (Player Settings → Minimum API Level).");
            var settings = PushizoSettings.Load();
            if (!File.Exists(settings.androidGoogleServicesJson))
                Debug.LogWarning($"[Pushizo] {settings.androidGoogleServicesJson} bulunamadı; FCM token alınamaz. Firebase konsolundan indirip ekleyin (Project Settings → Pushizo).");
        }

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            var settings = PushizoSettings.Load();
            var source = settings.androidGoogleServicesJson;
            if (!File.Exists(source)) return;
            var values = FirebaseValues(File.ReadAllText(source), PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android));
            if (values.Count == 0)
            {
                Debug.LogWarning($"[Pushizo] {source} içinde bu uygulama için Firebase istemcisi bulunamadı.");
                return;
            }
            var dir = Path.Combine(unityLibraryPath, "src", "main", "res", "values");
            Directory.CreateDirectory(dir);
            var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!-- Pushizo: google-services.json'dan üretildi. -->\n<resources>\n");
            foreach (var pair in values)
                xml.Append("  <string name=\"").Append(pair.Key).Append("\" translatable=\"false\">").Append(SecurityElement.Escape(pair.Value)).Append("</string>\n");
            xml.Append("</resources>\n");
            File.WriteAllText(Path.Combine(dir, ValuesFileName), xml.ToString());
        }

        /// <summary>google-services eklentisinin kuralı: paket adı eşleşen istemci (yoksa ilk istemci).</summary>
        internal static Dictionary<string, string> FirebaseValues(string json, string packageName)
        {
            var result = new Dictionary<string, string>();
            if (!(Json.Parse(json) is Dictionary<string, object> root)) return result;
            var project = root.TryGetValue("project_info", out var p) ? p as Dictionary<string, object> : null;
            var clients = root.TryGetValue("client", out var c) ? c as List<object> : null;
            if (project is null || clients is null || clients.Count == 0) return result;

            Dictionary<string, object> client = null;
            foreach (var item in clients)
            {
                if (!(item is Dictionary<string, object> candidate)) continue;
                client ??= candidate;
                if (Get(Get(Get(candidate, "client_info"), "android_client_info"), "package_name") as string == packageName)
                {
                    client = candidate;
                    break;
                }
            }
            if (client is null) return result;

            if (Get(Get(client, "client_info"), "mobilesdk_app_id") is string appId) result["google_app_id"] = appId;
            if (Get(project, "project_number") is string number) result["gcm_defaultSenderId"] = number;
            if (Get(project, "project_id") is string projectId) result["project_id"] = projectId;
            if (Get(project, "storage_bucket") is string bucket) result["google_storage_bucket"] = bucket;
            if (Get(client, "api_key") is List<object> keys && keys.Count > 0 && Get(keys[0], "current_key") is string apiKey)
            {
                result["google_api_key"] = apiKey;
                result["google_crash_reporting_api_key"] = apiKey;
            }
            return result;
        }

        private static object Get(object node, string key) =>
            node is Dictionary<string, object> map && map.TryGetValue(key, out var value) ? value : null;
    }
}
#endif
