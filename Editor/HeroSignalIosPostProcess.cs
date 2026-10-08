// UnityEditor.iOS.Xcode modülü yalnızca iOS Build Support kuruluyken vardır; derleme hedefi iOS iken derlenir.
#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEditor.iOS.Xcode.Extensions;
using UnityEngine;

namespace HeroSignalSdk.Unity.Editor
{
    /// <summary>
    /// iOS Xcode projesi düzenlemeleri: Push Notifications yeteneği ve <c>remote-notification</c> arka plan modu, Swift
    /// köprüsü için derleme ayarları, isteğe bağlı App Group ve Notification Service Extension (NSE) hedefi.
    /// EDM4U Podfile'ı 40. sırada üretir ve 50. sırada <c>pod install</c> çalıştırır; bu işlem arada (45) NSE hedefini
    /// Podfile'a ekler.
    /// </summary>
    internal static class HeroSignalIosPostProcess
    {
        private const string ExtensionName = "HeroSignalNotificationServiceExtension";
        private const string PodVersion = "0.1.0";
        private const string TemplatesPath = "Packages/com.herosignal.unity/Editor/Templates";

        [PostProcessBuild(45)]
        public static void OnPostProcessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            var settings = HeroSignalSettings.LoadOrDefault();
            var projectPath = PBXProject.GetPBXProjectPath(path);

            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            var mainTarget = project.GetUnityMainTargetGuid();
            var frameworkTarget = project.GetUnityFrameworkTargetGuid();
            // HeroSignalUnityBridge.swift UnityFramework hedefinde derlenir.
            project.SetBuildProperty(frameworkTarget, "SWIFT_VERSION", "5.0");
            project.SetBuildProperty(frameworkTarget, "CLANG_ENABLE_MODULES", "YES");
            project.SetBuildProperty(frameworkTarget, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "NO");
            project.SetBuildProperty(mainTarget, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "YES");
            var entitlements = project.GetBuildPropertyForAnyConfig(mainTarget, "CODE_SIGN_ENTITLEMENTS");
            project.WriteToFile(projectPath);

            if (string.IsNullOrEmpty(entitlements)) entitlements = "Unity-iPhone/HeroSignal.entitlements";
            var capabilities = new ProjectCapabilityManager(projectPath, entitlements, null, mainTarget);
            capabilities.AddPushNotifications(settings.iosApnsEnvironment != "production");
            capabilities.AddBackgroundModes(BackgroundModesOptions.RemoteNotifications);
            if (!string.IsNullOrEmpty(settings.iosAppGroup)) capabilities.AddAppGroups(new[] { settings.iosAppGroup });
            capabilities.WriteToFile();

            if (!string.IsNullOrEmpty(settings.iosAppGroup))
            {
                // Yerel SDK rozet sayacı için App Group'u Info.plist'ten okur (HeroSignalAppGroup).
                var plistPath = Path.Combine(path, "Info.plist");
                var plist = new PlistDocument();
                plist.ReadFromFile(plistPath);
                plist.root.SetString("HeroSignalAppGroup", settings.iosAppGroup);
                plist.WriteToFile(plistPath);
            }

            if (settings.iosAddNotificationServiceExtension) AddNotificationServiceExtension(path, projectPath, settings);
        }

        private static void AddNotificationServiceExtension(string path, string projectPath, HeroSignalSettings settings)
        {
            if (string.IsNullOrEmpty(settings.appId))
            {
                Debug.LogWarning("[HeroSignal] HeroSignalSettings.appId boş; Notification Service Extension eklenmedi.");
                return;
            }
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            if (!string.IsNullOrEmpty(project.TargetGuidByName(ExtensionName))) return;

            var mainTarget = project.GetUnityMainTargetGuid();
            var extensionDir = Path.Combine(path, ExtensionName);
            Directory.CreateDirectory(extensionDir);
            File.Copy(Path.GetFullPath(Path.Combine(TemplatesPath, "NotificationService.swift.txt")), Path.Combine(extensionDir, "NotificationService.swift"), true);
            var infoPlist = File.ReadAllText(Path.GetFullPath(Path.Combine(TemplatesPath, "NotificationServiceInfo.plist.txt")))
                .Replace("__HS_APP_ID__", Escape(settings.appId))
                .Replace("__HS_API_URL__", Escape(settings.apiUrl))
                .Replace("__HS_APP_GROUP__", Escape(settings.iosAppGroup ?? string.Empty))
                .Replace("__HS_VERSION__", Escape(PlayerSettings.bundleVersion))
                .Replace("__HS_BUILD__", Escape(PlayerSettings.iOS.buildNumber));
            File.WriteAllText(Path.Combine(extensionDir, "Info.plist"), infoPlist);

            var bundleId = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.iOS) + ".HeroSignalNSE";
            var extensionTarget = project.AddAppExtension(mainTarget, ExtensionName, bundleId, ExtensionName + "/Info.plist");
            project.AddFileToBuild(extensionTarget, project.AddFile(ExtensionName + "/NotificationService.swift", ExtensionName + "/NotificationService.swift"));
            project.AddFile(ExtensionName + "/Info.plist", ExtensionName + "/Info.plist");
            project.AddFrameworkToProject(extensionTarget, "UserNotifications.framework", false);
            project.SetBuildProperty(extensionTarget, "SWIFT_VERSION", "5.0");
            project.SetBuildProperty(extensionTarget, "IPHONEOS_DEPLOYMENT_TARGET", "15.0");
            project.SetBuildProperty(extensionTarget, "TARGETED_DEVICE_FAMILY", "1,2");
            project.SetBuildProperty(extensionTarget, "ARCHS", "arm64");
            project.SetBuildProperty(extensionTarget, "PRODUCT_BUNDLE_IDENTIFIER", bundleId);
            project.SetBuildProperty(extensionTarget, "APPLICATION_EXTENSION_API_ONLY", "YES");
            if (!string.IsNullOrEmpty(PlayerSettings.iOS.appleDeveloperTeamID))
                project.SetBuildProperty(extensionTarget, "DEVELOPMENT_TEAM", PlayerSettings.iOS.appleDeveloperTeamID);
            project.WriteToFile(projectPath);

            if (!string.IsNullOrEmpty(settings.iosAppGroup))
            {
                var extensionEntitlements = ExtensionName + "/" + ExtensionName + ".entitlements";
                var capabilities = new ProjectCapabilityManager(projectPath, extensionEntitlements, null, extensionTarget);
                capabilities.AddAppGroups(new[] { settings.iosAppGroup });
                capabilities.WriteToFile();
            }

            AddExtensionPod(path);
        }

        /// <summary>EDM4U'nun ürettiği Podfile'a NSE hedefini (HeroSignal/Extension) ekler.</summary>
        private static void AddExtensionPod(string path)
        {
            var podfile = Path.Combine(path, "Podfile");
            if (!File.Exists(podfile))
            {
                Debug.LogWarning("[HeroSignal] Podfile yok (EDM4U iOS Resolver kapalı mı?); NSE için 'HeroSignal/Extension' pod'unu elle ekleyin.");
                return;
            }
            var content = File.ReadAllText(podfile);
            if (content.Contains("target '" + ExtensionName + "'")) return;
            File.AppendAllText(podfile, "\ntarget '" + ExtensionName + "' do\n  pod 'HeroSignal/Extension', '" + PodVersion + "'\nend\n");
        }

        private static string Escape(string value) => System.Security.SecurityElement.Escape(value ?? string.Empty);
    }
}
#endif
