using System;
using System.Collections.Generic;
using System.Security;
using System.Text;
using HeroSignalSdk.Unity.Internal;

namespace HeroSignalSdk.Unity.Editor
{
    /// <summary>
    /// <c>google-services.json</c> → Android string kaynakları (Google Services Gradle eklentisinin ürettiği değerlerle
    /// aynı adlar). Firebase Unity SDK'sı olmadan FCM'nin varsayılan FirebaseApp'i bu kaynaklardan başlatılır.
    /// UnityEditor'a bağımlı değildir (Validation~ ile test edilir).
    /// </summary>
    internal static class GoogleServicesXml
    {
        /// <summary>
        /// Kaynak XML'ini üretir. <paramref name="packageName"/> ile eşleşen istemci seçilir; eşleşme yoksa
        /// <see cref="ArgumentException"/> (yanlış Firebase uygulaması dosyası).
        /// </summary>
        public static string Generate(string googleServicesJson, string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName)) throw new ArgumentException("Android paket adı gerekli.", nameof(packageName));
            Dictionary<string, object> root;
            try
            {
                root = Json.Parse(googleServicesJson) as Dictionary<string, object>;
            }
            catch (FormatException e)
            {
                throw new ArgumentException("google-services.json ayrıştırılamadı.", nameof(googleServicesJson), e);
            }
            if (root == null) throw new ArgumentException("google-services.json bir JSON nesnesi olmalı.", nameof(googleServicesJson));

            var project = Obj(root, "project_info") ?? throw new ArgumentException("google-services.json: project_info eksik.", nameof(googleServicesJson));
            var client = FindClient(root, packageName)
                ?? throw new ArgumentException($"google-services.json içinde '{packageName}' paket adlı Android uygulaması yok.", nameof(googleServicesJson));

            var values = new List<KeyValuePair<string, string>>();
            Add(values, "google_app_id", Str(Obj(client, "client_info"), "mobilesdk_app_id"), required: true);
            Add(values, "gcm_defaultSenderId", Str(project, "project_number"), required: true);
            Add(values, "project_id", Str(project, "project_id"), required: true);
            var apiKey = ApiKey(client);
            Add(values, "google_api_key", apiKey, required: true);
            Add(values, "google_crash_reporting_api_key", apiKey, required: false);
            Add(values, "google_storage_bucket", Str(project, "storage_bucket"), required: false);
            Add(values, "firebase_database_url", Str(project, "firebase_url"), required: false);

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
            sb.Append("<!-- HeroSignal tarafından google-services.json'dan üretildi; elle düzenlemeyin. -->\n");
            sb.Append("<resources>\n");
            foreach (var pair in values)
            {
                sb.Append("    <string name=\"").Append(pair.Key).Append("\" translatable=\"false\">")
                    .Append(SecurityElement.Escape(pair.Value)).Append("</string>\n");
            }
            sb.Append("</resources>\n");
            return sb.ToString();
        }

        private static Dictionary<string, object> FindClient(Dictionary<string, object> root, string packageName)
        {
            if (!(root.TryGetValue("client", out var clients) && clients is List<object> list)) return null;
            foreach (var item in list)
            {
                if (!(item is Dictionary<string, object> client)) continue;
                var android = Obj(Obj(client, "client_info"), "android_client_info");
                if (string.Equals(Str(android, "package_name"), packageName, StringComparison.Ordinal)) return client;
            }
            return null;
        }

        private static string ApiKey(Dictionary<string, object> client)
        {
            if (!(client.TryGetValue("api_key", out var keys) && keys is List<object> list)) return null;
            foreach (var item in list)
            {
                var key = Str(item as Dictionary<string, object>, "current_key");
                if (!string.IsNullOrEmpty(key)) return key;
            }
            return null;
        }

        private static void Add(List<KeyValuePair<string, string>> values, string name, string value, bool required)
        {
            if (string.IsNullOrEmpty(value))
            {
                if (required) throw new ArgumentException($"google-services.json: {name} için değer yok.");
                return;
            }
            values.Add(new KeyValuePair<string, string>(name, value));
        }

        private static Dictionary<string, object> Obj(Dictionary<string, object> e, string name) =>
            e != null && e.TryGetValue(name, out var v) ? v as Dictionary<string, object> : null;

        private static string Str(Dictionary<string, object> e, string name) =>
            e != null && e.TryGetValue(name, out var v) ? v as string : null;
    }
}
