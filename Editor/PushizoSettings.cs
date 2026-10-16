using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PushizoSdk.Editor
{
    /// <summary>
    /// Derleme ayarları (Project Settings → Pushizo). Çalışma zamanı ayarları kodda
    /// <c>Pushizo.Initialize(appId, options)</c> ile verilir; bu varlık yalnızca derleme sonrası işlemler (iOS NSE,
    /// yetenekler, Android google-services) içindir. Gizli bilgi içermez.
    /// </summary>
    public sealed class PushizoSettings : ScriptableObject
    {
        public const string DefaultAssetPath = "Assets/Pushizo/PushizoSettings.asset";

        [Tooltip("Dashboard'daki uygulama kimliği (app_…); iOS Notification Service Extension'a yazılır.")]
        public string appId = string.Empty;

        [Tooltip("Pushizo API adresi; iOS Notification Service Extension teslimat onayı (received) için kullanır.")]
        public string apiUrl = "https://api.pushizo.com";

        [Tooltip("iOS: zengin medya, rozet ve teslimat onayı için Notification Service Extension hedefi eklensin.")]
        public bool iosNotificationServiceExtension = true;

        [Tooltip("iOS: aps-environment (development: geliştirme profili, production: TestFlight/App Store).")]
        public bool iosApnsDevelopment = true;

        [Tooltip("iOS: uygulama ve NSE arasında rozet sayacı paylaşımı için App Group (ör. group.com.example.app). Boşsa eklenmez.")]
        public string iosAppGroup = string.Empty;

        [Tooltip("Android: Firebase konsolundan indirilen google-services.json yolu (proje köküne göre).")]
        public string androidGoogleServicesJson = "Assets/google-services.json";

        /// <summary>Projedeki ayar varlığı; yoksa varsayılan değerlerle bellek içi örnek (diske yazılmaz).</summary>
        public static PushizoSettings Load()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(PushizoSettings));
            if (guids.Length > 0)
            {
                var settings = AssetDatabase.LoadAssetAtPath<PushizoSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
                if (settings != null) return settings;
            }
            return CreateInstance<PushizoSettings>();
        }

        /// <summary>Ayar varlığını bulur, yoksa <see cref="DefaultAssetPath"/> altında oluşturur.</summary>
        public static PushizoSettings LoadOrCreate()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(PushizoSettings));
            if (guids.Length > 0)
            {
                var existing = AssetDatabase.LoadAssetAtPath<PushizoSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
                if (existing != null) return existing;
            }
            if (!AssetDatabase.IsValidFolder("Assets/Pushizo")) AssetDatabase.CreateFolder("Assets", "Pushizo");
            var settings = CreateInstance<PushizoSettings>();
            AssetDatabase.CreateAsset(settings, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            return settings;
        }
    }

    /// <summary>Project Settings → Pushizo sayfası.</summary>
    internal static class PushizoSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider Create() => new SettingsProvider("Project/Pushizo", SettingsScope.Project)
        {
            label = "Pushizo",
            guiHandler = _ =>
            {
                var serialized = new SerializedObject(PushizoSettings.LoadOrCreate());
                EditorGUILayout.PropertyField(serialized.FindProperty(nameof(PushizoSettings.appId)), new GUIContent("App ID"));
                EditorGUILayout.PropertyField(serialized.FindProperty(nameof(PushizoSettings.apiUrl)), new GUIContent("API URL"));
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("iOS", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serialized.FindProperty(nameof(PushizoSettings.iosNotificationServiceExtension)), new GUIContent("Notification Service Extension"));
                EditorGUILayout.PropertyField(serialized.FindProperty(nameof(PushizoSettings.iosApnsDevelopment)), new GUIContent("APNs development"));
                EditorGUILayout.PropertyField(serialized.FindProperty(nameof(PushizoSettings.iosAppGroup)), new GUIContent("App Group"));
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Android", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serialized.FindProperty(nameof(PushizoSettings.androidGoogleServicesJson)), new GUIContent("google-services.json"));
                serialized.ApplyModifiedProperties();
            },
            keywords = new HashSet<string>(new[] { "Pushizo", "push", "APNs", "FCM", "notification" }),
        };
    }
}
