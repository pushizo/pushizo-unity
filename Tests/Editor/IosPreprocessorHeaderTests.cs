using HeroSignalSdk.Editor;
using NUnit.Framework;

namespace HeroSignalSdk.Tests
{
    /// <summary>
    /// iOS derleme sonrası <c>Classes/Preprocessor.h</c> dönüşümü: UnityAppController'ın APNs token bildirimini
    /// yayınlaması için <c>UNITY_USES_REMOTE_NOTIFICATIONS</c> 1 olmalı.
    /// </summary>
    public sealed class IosPreprocessorHeaderTests
    {
        // Unity 2022–6000 sürümlerinin ürettiği dosyanın ilgili bölümü.
        private const string UnityHeader =
            "#pragma once\n\n" +
            "#define UNITY_USES_LOCATION 1\n" +
            "#define UNITY_USES_REMOTE_NOTIFICATIONS 0\n" +
            "#define UNITY_USES_GLES 0\n";

        [Test]
        public void Sets_remote_notifications_define_to_one()
        {
            var result = HeroSignalIosPreprocessorHeader.EnableRemoteNotifications(UnityHeader);
            StringAssert.Contains("#define UNITY_USES_REMOTE_NOTIFICATIONS 1\n", result);
            StringAssert.DoesNotContain("UNITY_USES_REMOTE_NOTIFICATIONS 0", result);
            // Diğer tanımlar korunur.
            StringAssert.Contains("#define UNITY_USES_LOCATION 1\n", result);
            StringAssert.Contains("#define UNITY_USES_GLES 0\n", result);
        }

        [Test]
        public void Is_idempotent()
        {
            var once = HeroSignalIosPreprocessorHeader.EnableRemoteNotifications(UnityHeader);
            Assert.AreEqual(once, HeroSignalIosPreprocessorHeader.EnableRemoteNotifications(once));
        }

        [Test]
        public void Preserves_indentation_and_trailing_comment_with_crlf()
        {
            const string header = "#pragma once\r\n  #define  UNITY_USES_REMOTE_NOTIFICATIONS\t0 // otomatik\r\n";
            var result = HeroSignalIosPreprocessorHeader.EnableRemoteNotifications(header);
            Assert.AreEqual("#pragma once\r\n  #define  UNITY_USES_REMOTE_NOTIFICATIONS\t1 // otomatik\r\n", result);
        }

        [Test]
        public void Appends_define_when_missing()
        {
            Assert.AreEqual(
                "#pragma once\n#define UNITY_USES_REMOTE_NOTIFICATIONS 1\n",
                HeroSignalIosPreprocessorHeader.EnableRemoteNotifications("#pragma once"));
            Assert.AreEqual(
                "#pragma once\r\n#define UNITY_USES_REMOTE_NOTIFICATIONS 1\r\n",
                HeroSignalIosPreprocessorHeader.EnableRemoteNotifications("#pragma once\r\n"));
        }

        [Test]
        public void Does_not_touch_similarly_named_defines()
        {
            const string header = "#define UNITY_USES_REMOTE_NOTIFICATIONS_EXTRA 0\n#define UNITY_USES_REMOTE_NOTIFICATIONS 0\n";
            var result = HeroSignalIosPreprocessorHeader.EnableRemoteNotifications(header);
            Assert.AreEqual("#define UNITY_USES_REMOTE_NOTIFICATIONS_EXTRA 0\n#define UNITY_USES_REMOTE_NOTIFICATIONS 1\n", result);
        }
    }
}
