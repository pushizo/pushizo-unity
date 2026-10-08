// UnityEditor.Android modülü yalnızca Android Build Support kuruluyken vardır; derleme hedefi Android iken derlenir.
#if UNITY_ANDROID
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace HeroSignalSdk.Unity.Editor
{
    /// <summary>
    /// Android Gradle projesi üretildikten sonra <c>google-services.json</c>'dan Firebase string kaynaklarını
    /// <c>unityLibrary/src/main/res/values/herosignal_google_services.xml</c> olarak yazar. Firebase Unity SDK'sı
    /// (FirebaseApp.androidlib) varsa aynı kaynakları ürettiği için atlanır.
    /// </summary>
    internal sealed class HeroSignalAndroidPostProcess : IPostGenerateGradleAndroidProject
    {
        private const string ResourceFile = "herosignal_google_services.xml";

        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var settings = HeroSignalSettings.LoadOrDefault();
            if (!settings.androidGenerateGoogleServicesResources) return;
            if (Directory.Exists(Path.Combine(path, "FirebaseApp.androidlib")) || Directory.Exists("Assets/Plugins/Android/FirebaseApp.androidlib"))
            {
                Debug.Log("[HeroSignal] Firebase Unity SDK kaynakları bulundu; google-services kaynakları üretilmedi.");
                return;
            }
            var jsonPath = settings.googleServicesJsonPath;
            if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
            {
                Debug.LogWarning($"[HeroSignal] {jsonPath} bulunamadı: FCM push alınamaz. Firebase Console'dan indirip bu yola koyun (HeroSignalSettings ile değiştirilebilir).");
                return;
            }
            try
            {
                var packageName = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
                var xml = GoogleServicesXml.Generate(File.ReadAllText(jsonPath), packageName);
                var valuesDir = Path.Combine(path, "src", "main", "res", "values");
                Directory.CreateDirectory(valuesDir);
                File.WriteAllText(Path.Combine(valuesDir, ResourceFile), xml);
            }
            catch (ArgumentException e)
            {
                throw new BuildFailedException("[HeroSignal] " + e.Message);
            }
        }
    }
}
#endif
