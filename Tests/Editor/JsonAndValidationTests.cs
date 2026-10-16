#nullable enable
using System;
using System.Collections.Generic;
using PushizoSdk.Internal;
using NUnit.Framework;

namespace PushizoSdk.Tests
{
    /// <summary>JSON yardımcısı ve normalizasyon (vektörler sdk/testdata/messaging-vectors.json ile aynı).</summary>
    public sealed class JsonAndValidationTests
    {
        [Test]
        public void Json_round_trips_nested_values()
        {
            var json = Json.Serialize(new Dictionary<string, object?>
            {
                ["s"] = "Merhaba \"dünya\"\n\t\u0001",
                ["i"] = 42L,
                ["d"] = 1.5,
                ["b"] = true,
                ["n"] = null,
                ["a"] = new List<object?> { 1L, "x", false },
                ["o"] = new Dictionary<string, object?> { ["k"] = "v" },
            });
            var back = Json.ParseObject(json);
            Assert.AreEqual("Merhaba \"dünya\"\n\t\u0001", back["s"]);
            Assert.AreEqual(42L, back["i"]);
            Assert.AreEqual(1.5, back["d"]);
            Assert.AreEqual(true, back["b"]);
            Assert.IsNull(back["n"]);
            CollectionAssert.AreEqual(new object?[] { 1L, "x", false }, (List<object?>)back["a"]!);
            Assert.AreEqual("v", ((Dictionary<string, object?>)back["o"]!)["k"]);
            Assert.AreEqual("é", Json.Parse(@"""é"""));
            Assert.AreEqual(-1.25e3, Json.Parse("-1.25e3"));
        }

        [TestCase("{")]
        [TestCase("{\"a\":}")]
        [TestCase("[1,]")]
        [TestCase("tru")]
        [TestCase("{} x")]
        public void Json_rejects_invalid_input(string json) => Assert.Throws<FormatException>(() => Json.Parse(json));

        [TestCase(" Ali.Veli@Example.COM ", "ali.veli@example.com")]
        [TestCase("user+tag@sub.example.co", "user+tag@sub.example.co")]
        [TestCase("geçersiz", null)]
        [TestCase("a@b", null)]
        [TestCase("a b@example.com", null)]
        [TestCase("a@-example.com", null)]
        [TestCase("", null)]
        public void Email_vectors(string input, string? expected) => Assert.AreEqual(expected, Validation.NormalizeEmail(input));

        [TestCase("+90 (532) 123-45-67", "+905321234567")]
        [TestCase("00905321234567", "+905321234567")]
        [TestCase("+1.415.555.2671", "+14155552671")]
        [TestCase(" +44 20 7946 0958 ", "+442079460958")]
        [TestCase("05321234567", null)]
        [TestCase("+0123456789", null)]
        [TestCase("+12345", null)]
        [TestCase("+1234567890123456", null)]
        [TestCase("+90532abc4567", null)]
        public void Phone_vectors(string input, string? expected) => Assert.AreEqual(expected, Validation.NormalizePhone(input));
    }
}
