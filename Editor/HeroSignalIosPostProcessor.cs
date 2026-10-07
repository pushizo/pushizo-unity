#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEditor.iOS.Xcode.Extensions;
using UnityEngine;

namespace HeroSignalSdk.Editor
{
    /// <summary>
    /// iOS derleme sonrası: Push Notifications yeteneği (aps-environment), Background Modes → remote-notification,
    /// isteğe bağlı App Group, Swift köprüsü (Runtime/Plugins/iOS) için derleme ayarları ve Notification Service Extension
    /// hedefi (zengin medya, rozet, teslimat onayı). Sıra 45: EDM4U Podfile'ı 40'ta üretir, <c>pod install</c> 50'de
    /// çalışır; NSE hedefi ve Podfile girdisi bu aralıkta eklenir.
    /// </summary>
    public static class HeroSignalIosPostProcessor
    {
        public const string ExtensionTargetName = "HeroSignalNotificationServiceExtension";
        private const string PodVersion = "~> 0.1.0";

        [PostProcessBuild(45)]
        public static void OnPostProcessBuild(BuildTarget target, string buildPath)
        {
            if (target != BuildTarget.iOS) return;
            var settings = HeroSignalSettings.Load();
            var projectPath = PBXProject.GetPBXProjectPath(buildPath);

            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            var mainTarget = project.GetUnityMainTargetGuid();
            var frameworkTarget = project.GetUnityFrameworkTargetGuid();

            // Swift köprüsü UnityFramework hedefinde derlenir; Swift çalışma zamanı uygulamaya gömülür.
            project.SetBuildProperty(frameworkTarget, "SWIFT_VERSION", "5.0");
            project.SetBuildProperty(frameworkTarget, "CLANG_ENABLE_MODULES", "YES");
            project.SetBuildProperty(frameworkTarget, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "NO");
            project.SetBuildProperty(mainTarget, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "YES");
            project.AddFrameworkToProject(frameworkTarget, "UserNotifications.framework", false);

            var entitlementsPath = project.GetBuildPropertyForAnyConfig(mainTarget, "CODE_SIGN_ENTITLEMENTS");
            if (string.IsNullOrEmpty(entitlementsPath)) entitlementsPath = "Unity-iPhone/HeroSignal.entitlements";

            var addExtension = settings.iosNotificationServiceExtension;
            if (addExtension && string.IsNullOrWhiteSpace(settings.appId))
            {
                Debug.LogWarning("[HeroSignal] Notification Service Extension eklenmedi: Project Settings → HeroSignal → App ID boş.");
                addExtension = false;
            }
            if (addExtension && project.TargetGuidByName(ExtensionTargetName) == null)
                AddNotificationServiceExtension(project, mainTarget, buildPath, settings);

            project.WriteToFile(projectPath);

            // Yetenekler projeyi ayrıca okuyup yazar; PBXProject değişiklikleri önce diske yazılmış olmalıdır.
            var capabilities = new ProjectCapabilityManager(projectPath, entitlementsPath, null, mainTarget);
            capabilities.AddPushNotifications(settings.iosApnsDevelopment);
            capabilities.AddBackgroundModes(BackgroundModesOptions.RemoteNotifications);
            if (!string.IsNullOrWhiteSpace(settings.iosAppGroup)) capabilities.AddAppGroups(new[] { settings.iosAppGroup.Trim() });
            capabilities.WriteToFile();

            if (addExtension) AddExtensionPod(buildPath);
        }

        private static void AddNotificationServiceExtension(PBXProject project, string mainTarget, string buildPath, HeroSignalSettings settings)
        {
            var dir = Path.Combine(buildPath, ExtensionTargetName);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "NotificationService.swift"), NotificationServiceSource);
            File.WriteAllText(Path.Combine(dir, "Info.plist"), ExtensionInfoPlist(settings));

            var bundleId = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.iOS) + "." + ExtensionTargetName;
            var extension = project.AddAppExtension(mainTarget, ExtensionTargetName, bundleId, ExtensionTargetName + "/Info.plist");
            var sources = project.AddSourcesBuildPhase(extension);
            project.AddFrameworksBuildPhase(extension);
            var serviceFile = project.AddFile(ExtensionTargetName + "/NotificationService.swift", ExtensionTargetName + "/NotificationService.swift");
            project.AddFileToBuildSection(extension, sources, serviceFile);
            project.AddFile(ExtensionTargetName + "/Info.plist", ExtensionTargetName + "/Info.plist");
            project.AddFrameworkToProject(extension, "UserNotifications.framework", false);

            project.SetBuildProperty(extension, "TARGETED_DEVICE_FAMILY", "1,2");
            project.SetBuildProperty(extension, "IPHONEOS_DEPLOYMENT_TARGET", "15.0");
            project.SetBuildProperty(extension, "SWIFT_VERSION", "5.0");
            project.SetBuildProperty(extension, "ARCHS", "arm64");
            project.SetBuildProperty(extension, "PRODUCT_BUNDLE_IDENTIFIER", bundleId);
            if (!string.IsNullOrEmpty(PlayerSettings.iOS.appleDeveloperTeamID))
                project.SetBuildProperty(extension, "DEVELOPMENT_TEAM", PlayerSettings.iOS.appleDeveloperTeamID);

            if (!string.IsNullOrWhiteSpace(settings.iosAppGroup))
            {
                var entitlements = new PlistDocument();
                var groups = entitlements.root.CreateArray("com.apple.security.application-groups");
                groups.AddString(settings.iosAppGroup.Trim());
                var relative = ExtensionTargetName + "/" + ExtensionTargetName + ".entitlements";
                entitlements.WriteToFile(Path.Combine(buildPath, relative));
                project.AddFile(relative, relative);
                project.SetBuildProperty(extension, "CODE_SIGN_ENTITLEMENTS", relative);
            }
        }

        /// <summary>NSE hedefi Podfile'a eklenir (EDM4U Podfile'ı üretmiş olmalı); tekrar derlemede çoğaltılmaz.</summary>
        private static void AddExtensionPod(string buildPath)
        {
            var podfile = Path.Combine(buildPath, "Podfile");
            if (!File.Exists(podfile))
            {
                Debug.LogWarning("[HeroSignal] Podfile bulunamadı (EDM4U kurulu mu?). NSE hedefine HeroSignal/Extension pod'unu elle ekleyin.");
                return;
            }
            var content = File.ReadAllText(podfile);
            if (content.Contains("target '" + ExtensionTargetName + "'")) return;
            content += "\ntarget '" + ExtensionTargetName + "' do\n  pod 'HeroSignal/Extension', '" + PodVersion + "'\nend\n";
            File.WriteAllText(podfile, content);
        }

        private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;

        private static string ExtensionInfoPlist(HeroSignalSettings settings) =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
            "<plist version=\"1.0\">\n<dict>\n" +
            "  <key>CFBundleDevelopmentRegion</key><string>$(DEVELOPMENT_LANGUAGE)</string>\n" +
            "  <key>CFBundleDisplayName</key><string>" + ExtensionTargetName + "</string>\n" +
            "  <key>CFBundleExecutable</key><string>$(EXECUTABLE_NAME)</string>\n" +
            "  <key>CFBundleIdentifier</key><string>$(PRODUCT_BUNDLE_IDENTIFIER)</string>\n" +
            "  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>\n" +
            "  <key>CFBundleName</key><string>$(PRODUCT_NAME)</string>\n" +
            "  <key>CFBundlePackageType</key><string>XPC!</string>\n" +
            "  <key>CFBundleShortVersionString</key><string>" + Escape(PlayerSettings.bundleVersion) + "</string>\n" +
            "  <key>CFBundleVersion</key><string>" + Escape(PlayerSettings.iOS.buildNumber) + "</string>\n" +
            "  <key>HeroSignalAppId</key><string>" + Escape(settings.appId.Trim()) + "</string>\n" +
            "  <key>HeroSignalApiUrl</key><string>" + Escape(settings.apiUrl.Trim()) + "</string>\n" +
            "  <key>HeroSignalAppGroup</key><string>" + Escape(settings.iosAppGroup.Trim()) + "</string>\n" +
            "  <key>NSExtension</key>\n  <dict>\n" +
            "    <key>NSExtensionPointIdentifier</key><string>com.apple.usernotifications.service</string>\n" +
            "    <key>NSExtensionPrincipalClass</key><string>$(PRODUCT_MODULE_NAME).NotificationService</string>\n" +
            "  </dict>\n</dict>\n</plist>\n";

        /// <summary>NSE sınıfı: yerel HeroSignalExtension yardımcısını Info.plist'teki App ID/API adresiyle kullanır.</summary>
        private const string NotificationServiceSource =
            "import UserNotifications\n" +
            "import HeroSignal\n\n" +
            "// HeroSignal Unity paketinin ürettiği Notification Service Extension: zengin medya, rozet ve teslimat onayı.\n" +
            "final class NotificationService: UNNotificationServiceExtension {\n" +
            "    private lazy var heroSignal: HeroSignalNotificationServiceExtension = {\n" +
            "        let info = Bundle.main.infoDictionary ?? [:]\n" +
            "        let group = (info[\"HeroSignalAppGroup\"] as? String).flatMap { $0.isEmpty ? nil : $0 }\n" +
            "        return HeroSignalNotificationServiceExtension(\n" +
            "            appId: info[\"HeroSignalAppId\"] as? String ?? \"\",\n" +
            "            apiUrl: info[\"HeroSignalApiUrl\"] as? String ?? \"https://api.herosignal.dev\",\n" +
            "            appGroup: group)\n" +
            "    }()\n\n" +
            "    override func didReceive(_ request: UNNotificationRequest, withContentHandler contentHandler: @escaping (UNNotificationContent) -> Void) {\n" +
            "        heroSignal.didReceive(request, withContentHandler: contentHandler)\n" +
            "    }\n\n" +
            "    override func serviceExtensionTimeWillExpire() {\n" +
            "        heroSignal.serviceExtensionTimeWillExpire()\n" +
            "    }\n" +
            "}\n";
    }
}
#endif
