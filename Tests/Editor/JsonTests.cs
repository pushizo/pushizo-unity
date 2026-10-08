using System;
using System.Collections.Generic;
using HeroSignalSdk.Unity.Internal;
using NUnit.Framework;

namespace HeroSignalSdk.Unity.Tests
{
    public sealed class JsonTests
    {
        [Test]
        public void Parses_all_value_kinds()
        {
            var root = (Dictionary<string, object>)Json.Parse(
                "{\"s\":\"ş\\u00e7\\n\\\"q\\\"\",\"i\":42,\"n\":-7,\"d\":1.5e2,\"t\":true,\"f\":false,\"z\":null,\"a\":[1,\"x\",{}],\"o\":{\"k\":\"v\"}}");
            Assert.AreEqual("şç\n\"q\"", root["s"]);
            Assert.AreEqual(42L, root["i"]);
            Assert.AreEqual(-7L, root["n"]);
            Assert.AreEqual(150.0, root["d"]);
            Assert.AreEqual(true, root["t"]);
            Assert.AreEqual(false, root["f"]);
            Assert.IsNull(root["z"]);
            var list = (List<object>)root["a"];
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual("v", ((Dictionary<string, object>)root["o"])["k"]);
        }

        [TestCase("")]
        [TestCase("{")]
        [TestCase("{\"a\":}")]
        [TestCase("[1,]")]
        [TestCase("{\"a\":1} x")]
        [TestCase("\"kapanmamış")]
        [TestCase("tru")]
        [TestCase("-")]
        public void Rejects_malformed_json(string json) =>
            Assert.Throws<FormatException>(() => Json.Parse(json));

        [Test]
        public void Rejects_excessive_nesting()
        {
            var deep = new string('[', 70) + new string(']', 70);
            Assert.Throws<FormatException>(() => Json.Parse(deep));
        }

        [Test]
        public void Serializes_with_escaping_and_invariant_numbers()
        {
            var json = Json.Serialize(new Dictionary<string, object>
            {
                ["s"] = "a\"b\\c\n\u0001",
                ["d"] = 0.1,
                ["l"] = 9007199254740993L,
                ["b"] = true,
                ["n"] = null,
                ["list"] = new List<object> { 1, "x" },
            });
            Assert.AreEqual("{\"s\":\"a\\\"b\\\\c\\n\\u0001\",\"d\":0.1,\"l\":9007199254740993,\"b\":true,\"n\":null,\"list\":[1,\"x\"]}", json);
        }

        [Test]
        public void Round_trips_unicode_and_line_separators()
        {
            var text = "Merhaba dünya ğüşıöç İ \u2028 \u2029 \U0001F600";
            var json = Json.Serialize(new List<object> { text });
            StringAssert.Contains("\\u2028", json);
            Assert.AreEqual(text, ((List<object>)Json.Parse(json))[0]);
        }

        [Test]
        public void Rejects_non_finite_numbers_and_unknown_types()
        {
            Assert.Throws<ArgumentException>(() => Json.Serialize(double.NaN));
            Assert.Throws<ArgumentException>(() => Json.Serialize(new object()));
            Assert.Throws<ArgumentException>(() => Json.Serialize(new Dictionary<int, int> { [1] = 1 }));
        }
    }
}
