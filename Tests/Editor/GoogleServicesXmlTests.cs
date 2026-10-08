using System;
using HeroSignalSdk.Unity.Editor;
using NUnit.Framework;

namespace HeroSignalSdk.Unity.Tests
{
    public sealed class GoogleServicesXmlTests
    {
        private const string Json = @"{
  ""project_info"": { ""project_number"": ""1234567890"", ""project_id"": ""hero-demo"", ""storage_bucket"": ""hero-demo.appspot.com"" },
  ""client"": [
    { ""client_info"": { ""mobilesdk_app_id"": ""1:1234567890:android:other"", ""android_client_info"": { ""package_name"": ""com.example.other"" } },
      ""api_key"": [ { ""current_key"": ""KEY_OTHER"" } ] },
    { ""client_info"": { ""mobilesdk_app_id"": ""1:1234567890:android:abc"", ""android_client_info"": { ""package_name"": ""com.example.game"" } },
      ""api_key"": [ { ""current_key"": ""AIza<&>"" } ] }
  ]
}";

        [Test]
        public void Generates_resources_for_matching_package()
        {
            var xml = GoogleServicesXml.Generate(Json, "com.example.game");
            StringAssert.Contains("<string name=\"google_app_id\" translatable=\"false\">1:1234567890:android:abc</string>", xml);
            StringAssert.Contains("<string name=\"gcm_defaultSenderId\" translatable=\"false\">1234567890</string>", xml);
            StringAssert.Contains("<string name=\"project_id\" translatable=\"false\">hero-demo</string>", xml);
            StringAssert.Contains("<string name=\"google_api_key\" translatable=\"false\">AIza&lt;&amp;&gt;</string>", xml);
            StringAssert.Contains("<string name=\"google_storage_bucket\" translatable=\"false\">hero-demo.appspot.com</string>", xml);
            StringAssert.DoesNotContain("firebase_database_url", xml);
            StringAssert.DoesNotContain("KEY_OTHER", xml);
        }

        [Test]
        public void Rejects_unknown_package_and_invalid_json()
        {
            Assert.Throws<ArgumentException>(() => GoogleServicesXml.Generate(Json, "com.example.missing"));
            Assert.Throws<ArgumentException>(() => GoogleServicesXml.Generate("{bozuk", "com.example.game"));
            Assert.Throws<ArgumentException>(() => GoogleServicesXml.Generate("{\"client\":[]}", "com.example.game"));
            Assert.Throws<ArgumentException>(() => GoogleServicesXml.Generate(Json, " "));
        }
    }
}
