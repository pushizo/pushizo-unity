using System;
using System.Collections.Generic;
using HeroSignalSdk.Unity.Internal;
using NUnit.Framework;

namespace HeroSignalSdk.Unity.Tests
{
    public sealed class EventPropertiesTests
    {
        private enum Tier
        {
            Gold,
        }

        [Test]
        public void Normalizes_supported_types()
        {
            var result = EventProperties.Normalize(new Dictionary<string, object>
            {
                ["s"] = "metin",
                ["i"] = 7,
                ["ul"] = ulong.MaxValue,
                ["f"] = 1.5f,
                ["m"] = 2.25m,
                ["e"] = Tier.Gold,
                ["dto"] = new DateTimeOffset(2026, 10, 8, 12, 30, 0, TimeSpan.FromHours(3)),
                ["list"] = new object[] { 1, "a", null },
                ["map"] = new Dictionary<string, int> { ["k"] = 1 },
            }, "properties");

            Assert.AreEqual("metin", result["s"]);
            Assert.AreEqual(7L, result["i"]);
            Assert.AreEqual((double)ulong.MaxValue, result["ul"]);
            Assert.AreEqual(1.5, result["f"]);
            Assert.AreEqual(2.25, result["m"]);
            Assert.AreEqual("Gold", result["e"]);
            Assert.AreEqual("2026-10-08T09:30:00.000Z", result["dto"]);
            CollectionAssert.AreEqual(new List<object> { 1L, "a", null }, (List<object>)result["list"]);
            Assert.AreEqual(1L, ((Dictionary<string, object>)result["map"])["k"]);
        }

        [Test]
        public void Rejects_invalid_values()
        {
            Assert.Throws<ArgumentException>(() => EventProperties.Normalize(new Dictionary<string, object> { [""] = 1 }, "p"));
            Assert.Throws<ArgumentException>(() => EventProperties.Normalize(new Dictionary<string, object> { ["x"] = float.NaN }, "p"));
            Assert.Throws<ArgumentException>(() => EventProperties.Normalize(new Dictionary<string, object> { ["x"] = new object() }, "p"));
            object deep = 1;
            for (var i = 0; i < 17; i++) deep = new List<object> { deep };
            Assert.Throws<ArgumentException>(() => EventProperties.Normalize(new Dictionary<string, object> { ["x"] = deep }, "p"));
            Assert.AreEqual(0, EventProperties.Normalize(null, "p").Count);
        }

        [Test]
        public void Writes_json_for_ios_bridge()
        {
            var tree = EventProperties.Normalize(new Dictionary<string, object> { ["a"] = 1, ["b"] = new[] { true }, ["c"] = null, ["d"] = 0.5 }, "p");
            Assert.AreEqual("{\"a\":1,\"b\":[true],\"c\":null,\"d\":0.5}", EventProperties.ToJson(tree));
        }
    }
}
