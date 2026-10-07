using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HeroSignalSdk.Editor
{
    /// <summary>
    /// Derleme ayarları (Project Settings → HeroSignal). Çalışma zamanı ayarları kodda
    /// <c>HeroSignal.Initialize(appId, options)</c> ile verilir; bu varlık yalnızca derleme sonrası işlemler (iOS NSE,
    /// yetenekler, Android google-services) içindir. Gizli bilgi içermez.
    /// </summary>
    public sealed class HeroSignalSettings : ScriptableObject
    {
        public const string DefaultAssetPath = "Assets/HeroSignal/HeroSignalSettings.asset";

        [Tooltip("Dashboard'daki uygulama kimliği (app_…); iOS Notification Service Extension'a yazılır.")]
        public string appId = string.Empty;

        [Tooltip("HeroSignal API adresi; iOS Notification Service Extension teslimat onayı (received) için kullanır.")]
        public string apiUrl = "https://api.herosignal.dev";

        [Tooltip("iOS: zengin medya, rozet ve teslimat onayı için Notification Service Extension hedefi eklensin.")]
        public bool iosNotificationServiceExtension = true;

        [Tooltip("iOS: aps-environment (development: geliştirme profili, production: TestFlight/App Store).")]
        public bool iosApnsDevelopment = true;

        [Tooltip("iOS: uygulama ve NSE arasında rozet sayacı paylaşımı için App Group (ör. group.com.example.app). Boşsa eklenmez.")]
        public string iosAppGroup = string.Empty;

        [Tooltip("Android: Firebase konsolundan indirilen google-services.json yolu (proje köküne göre).")]
        public string androidGoogleServicesJson = "Assets/google-services.json";

        /// <summary>Projedeki ayar varlığı; yoksa varsayılan değerlerle bellek içi örnek (diske yazılmaz).</summary>
        public static HeroSignalSettings Load()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(HeroSignalSettings));
            if (guids.Length > 0)
            {
                var settings = AssetDatabase.LoadAssetAtPath<HeroSignalSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
                if (settings != null) return settings;
            }
            return CreateInstance<HeroSignalSettings>();
        }

        /// <summary>Ayar varlığını bulur, yoksa <see cref="DefaultAssetPath"/> altında oluşturur.</summary>
        public static HeroSignalSettings LoadOrCreate()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(HeroSignalSettings));
            if (guids.Length > 0)
            {
                var existing = AssetDatabase.LoadAssetAtPath<HeroSignalSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
                if (existing != null) return existing;
            }
            if (!AssetDatabase.IsValidFolder("Assets/HeroSignal")) AssetDatabase.CreateFolder("Assets", "HeroSignal");
            var settings = CreateInstance<HeroSignalSettings>();
            AssetDatabase.CreateAsset(settings, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            return settings;
        }
    }

    /// <summary>Project Settings → HeroSignal sayfası.</summary>
    internal static class HeroSignalSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider Create() => new SettingsProvider("Project/HeroSignal", SettingsScope.Project)
        {
            label = "HeroSignal",
            guiHandler = _ =>
            {
                var serialized = new SerializedObject(HeroSignalSettings.LoadOrCreate());
                EditorGUILayout.PropertyField(serialized.FindProperty(nameof(HeroSignalSettings.appId)), new GUIContent("App ID"));
                EditorGUILayout.PropertyField(serialized.FindProperty(nameof(HeroSignalSettings.apiUrl)), new GUIContent("API URL"));
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("iOS", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serialized.FindProperty(nameof(HeroSignalSettings.iosNotificationServiceExtension)), new GUIContent("Notification Service Extension"));
                EditorGUILayout.PropertyField(serialized.FindProperty(nameof(HeroSignalSettings.iosApnsDevelopment)), new GUIContent("APNs development"));
                EditorGUILayout.PropertyField(serialized.FindProperty(nameof(HeroSignalSettings.iosAppGroup)), new GUIContent("App Group"));
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Android", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serialized.FindProperty(nameof(HeroSignalSettings.androidGoogleServicesJson)), new GUIContent("google-services.json"));
                serialized.ApplyModifiedProperties();
            },
            keywords = new HashSet<string>(new[] { "HeroSignal", "push", "APNs", "FCM", "notification" }),
        };
    }
}
