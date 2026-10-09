using System;
using System.Collections.Generic;
using System.IO;
using HeroSignalSdk.Unity.Internal;
using NUnit.Framework;

namespace HeroSignalSdk.Unity.Tests
{
    /// <summary>Doğrulama kuralları; ortak vektör dosyası (<c>sdk/testdata/messaging-vectors.json</c>) bulunursa onunla da.</summary>
    public sealed class ValidationTests
    {
        [TestCase(" Ali.Veli@Example.COM ", "ali.veli@example.com")]
        [TestCase("user+tag@sub.example.co", "user+tag@sub.example.co")]
        [TestCase("geçersiz", null)]
        [TestCase("a@b", null)]
        public void Email(string input, string expected) => Assert.AreEqual(expected, Validation.NormalizeEmail(input));

        [TestCase("+90 (532) 123-45-67", "+905321234567")]
        [TestCase("00905321234567", "+905321234567")]
        [TestCase("+1.415.555.2671", "+14155552671")]
        [TestCase("05321234567", null)]
        [TestCase("+12345", null)]
        public void Phone(string input, string expected) => Assert.AreEqual(expected, Validation.NormalizePhone(input));

        [Test]
        public void Shared_vectors_when_available()
        {
            var path = FindVectors();
            if (path == null) Assert.Ignore("sdk/testdata/messaging-vectors.json bu ortamda yok (paket tek başına kurulu).");
            var root = (Dictionary<string, object>)Json.Parse(File.ReadAllText(path));
            var checkedCount = 0;
            foreach (Dictionary<string, object> item in (List<object>)root["email"])
            {
                Assert.AreEqual(item["expected"], Validation.NormalizeEmail((string)item["input"]), (string)item["input"]);
                checkedCount++;
            }
            foreach (Dictionary<string, object> item in (List<object>)root["phone"])
            {
                Assert.AreEqual(item["expected"], Validation.NormalizePhone((string)item["input"]), (string)item["input"]);
                checkedCount++;
            }
            Assert.Greater(checkedCount, 0);
        }

        /// <summary>
        /// Doğrulama projesinde çıktı klasörü; Unity'de paketin fiziksel klasörünün (yerel yol veya gömülü paket) yanındaki
        /// <c>sdk/testdata</c>. Kayıt defterinden kurulan pakette dosya yoktur ve test atlanır.
        /// </summary>
        private static string FindVectors()
        {
            var candidates = new List<string> { Path.Combine(AppContext.BaseDirectory, "testdata", "messaging-vectors.json") };
#if !HEROSIGNAL_DOTNET_VALIDATION
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(HeroSignal).Assembly);
            if (package != null && !string.IsNullOrEmpty(package.resolvedPath))
                candidates.Add(Path.GetFullPath(Path.Combine(package.resolvedPath, "..", "testdata", "messaging-vectors.json")));
#endif
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        [TestCase("crm_id", true)]
        [TestCase("Crm", false)]
        [TestCase("crm-id", false)]
        [TestCase("external_id", false)]
        [TestCase("hs_id", false)]
        public void Alias_labels(string label, bool valid)
        {
            Exception error = null;
            try
            {
                Validation.RequireAliasLabel(label, "label");
            }
            catch (ArgumentException e)
            {
                error = e;
            }
            Assert.AreEqual(valid, error == null);
        }

        [TestCase("purchase", true)]
        [TestCase("level.complete", true)]
        [TestCase("ns:event-1", true)]
        [TestCase("bad name", false)]
        [TestCase("çarşı", false)]
        [TestCase("", false)]
        public void Event_names(string name, bool valid)
        {
            Exception error = null;
            try
            {
                Validation.RequireEventName(name, "name");
            }
            catch (ArgumentException e)
            {
                error = e;
            }
            Assert.AreEqual(valid, error == null);
        }

        [Test]
        public void Alias_values_and_inbox_limits()
        {
            Assert.AreEqual("42", Validation.NormalizeAliases(new Dictionary<string, string> { ["crm_id"] = " 42 " }, "a")["crm_id"]);
            Assert.Throws<ArgumentException>(() => Validation.NormalizeAliases(new Dictionary<string, string>(), "a"));
            Assert.Throws<ArgumentException>(() => Validation.NormalizeAliases(new Dictionary<string, string> { ["k"] = new string('x', 256) }, "a"));
            Validation.RequireInboxLimit(1, "limit");
            Validation.RequireInboxLimit(200, "limit");
            Assert.Throws<ArgumentOutOfRangeException>(() => Validation.RequireInboxLimit(0, "limit"));
        }
    }
}
