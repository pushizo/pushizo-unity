using System;
using System.Collections.Generic;
using HeroSignalSdk.Unity.Internal;
using NUnit.Framework;

namespace HeroSignalSdk.Unity.Tests
{
    /// <summary>iOS köprüsü JSON biçimleri (Plugins/iOS/HeroSignalUnityBridge.swift ile eşleşir).</summary>
    public sealed class BridgeJsonTests
    {
        [Test]
        public void Parses_notification_click()
        {
            const string json = "{\"notification\":{\"title\":\"Sipariş\",\"deliveryId\":\"dlv_1\",\"notificationId\":\"dlv_1\",\"group\":\"orders\"," +
                                "\"actions\":[{\"id\":\"track\",\"title\":\"İzle\",\"url\":\"https://t\",\"icon\":null},{\"title\":\"kimliksiz\"}]," +
                                "\"data\":{\"order\":\"42\",\"vip\":true,\"score\":1.5,\"skip\":null}},\"actionId\":\"track\",\"url\":\"https://t\"}";
            var click = BridgeJson.ParseNotificationClick(json);
            Assert.AreEqual("track", click.ActionId);
            Assert.AreEqual("https://t", click.Url);
            var n = click.Notification;
            Assert.AreEqual("Sipariş", n.Title);
            Assert.AreEqual("dlv_1", n.NotificationId);
            Assert.AreEqual("orders", n.Group);
            Assert.AreEqual(1, n.Actions.Count);
            Assert.AreEqual("İzle", n.Actions[0].Title);
            Assert.IsNull(n.Actions[0].Icon);
            Assert.AreEqual("42", n.Data["order"]);
            Assert.AreEqual("true", n.Data["vip"]);
            Assert.AreEqual("1.5", n.Data["score"]);
            Assert.IsFalse(n.Data.ContainsKey("skip"));
        }

        [Test]
        public void Parses_subscription_permission_and_foreground()
        {
            Assert.AreEqual(new PushSubscriptionState("sub", null, true, false),
                BridgeJson.ParseSubscriptionState("{\"id\":\"sub\",\"token\":null,\"optedIn\":true,\"permission\":false}"));
            Assert.IsTrue(BridgeJson.ParsePermission("{\"permission\":true}"));
            Assert.IsTrue(BridgeJson.ParsePermissionResult("{\"granted\":true}"));
            var foreground = BridgeJson.ParseForeground("{\"eventId\":\"e1\",\"notification\":{\"title\":\"T\"}}");
            Assert.AreEqual("e1", foreground.Key);
            Assert.AreEqual("T", foreground.Value.Title);
            Assert.Throws<HeroSignalException>(() => BridgeJson.ParseForeground("{\"notification\":{}}"));
        }

        [TestCase("willDisplay", "WillDisplay")]
        [TestCase("didDisplay", "DidDisplay")]
        [TestCase("willDismiss", "WillDismiss")]
        [TestCase("didDismiss", "DidDismiss")]
        public void Parses_in_app_lifecycle(string phase, string expected)
        {
            var parsed = BridgeJson.ParseInAppLifecycle("{\"phase\":\"" + phase + "\",\"messageId\":\"m1\",\"json\":\"{\\\"id\\\":\\\"m1\\\"}\"}");
            Assert.AreEqual(Enum.Parse(typeof(InAppLifecyclePhase), expected), parsed.Key);
            Assert.AreEqual("m1", parsed.Value.Id);
            Assert.AreEqual("{\"id\":\"m1\"}", parsed.Value.Json);
        }

        [Test]
        public void In_app_parsers_require_ids()
        {
            Assert.Throws<HeroSignalException>(() => BridgeJson.ParseInAppLifecycle("{\"phase\":\"x\",\"messageId\":\"m\"}"));
            Assert.Throws<HeroSignalException>(() => BridgeJson.ParseInAppClick("{\"actionId\":\"a\"}"));
            Assert.AreEqual("buy", BridgeJson.ParseInAppClick("{\"messageId\":\"m\",\"actionId\":\"buy\"}").ActionId);
        }

        [Test]
        public void Parses_inbox_page_and_counts()
        {
            var page = BridgeJson.ParseInboxPage(
                "{\"items\":[{\"id\":\"i1\",\"read\":false,\"createdAt\":1700000000000,\"data\":{\"k\":\"v\"}},{\"id\":\"i2\",\"read\":true},\"bozuk\"]," +
                "\"unreadCount\":5,\"nextCursor\":\"c2\",\"hasMore\":true}");
            Assert.AreEqual(2, page.Items.Count);
            Assert.AreEqual(5, page.UnreadCount);
            Assert.AreEqual("c2", page.NextCursor);
            Assert.IsTrue(page.HasMore);
            Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000), page.Items[0].CreatedAt);
            Assert.IsNull(page.Items[0].ReadAt);
            Assert.AreEqual(1, BridgeJson.ParseInboxPage("{\"items\":[{\"id\":\"a\"},{\"id\":\"b\",\"read\":true}]}").UnreadCount);
            Assert.Throws<HeroSignalException>(() => BridgeJson.ParseInboxItem("{\"title\":\"x\"}"));
            Assert.AreEqual(7, BridgeJson.ParseCount("{\"count\":7}"));
            Assert.Throws<HeroSignalException>(() => BridgeJson.ParseCount("{}"));
        }

        [Test]
        public void Parses_errors()
        {
            var e = BridgeJson.ParseError("{\"code\":\"NOT_FOUND\",\"message\":\"yok\",\"status\":404}");
            Assert.AreEqual("NOT_FOUND", e.Code);
            Assert.AreEqual(404, e.Status);
            e = BridgeJson.ParseError("{\"code\":\"NOT_READY\",\"message\":\"x\"}");
            Assert.IsNull(e.Status);
            Assert.AreEqual(HeroSignalException.InvalidResponse, BridgeJson.ParseError("bozuk").Code);
        }

        [Test]
        public void Invalid_json_becomes_invalid_response()
        {
            var e = Assert.Throws<HeroSignalException>(() => BridgeJson.ParseNotification("{bozuk"));
            Assert.AreEqual(HeroSignalException.InvalidResponse, e.Code);
            Assert.IsInstanceOf<FormatException>(e.InnerException);
            Assert.Throws<HeroSignalException>(() => BridgeJson.ParseNotification("[1]"));
        }

        [Test]
        public void String_maps_and_lists_round_trip()
        {
            var map = new Dictionary<string, string> { ["plan"] = "pro", ["dil"] = "tr \"ş\"" };
            CollectionAssert.AreEquivalent(map, BridgeJson.ParseStringMap(BridgeJson.ToJson(map)));
            var list = new List<string> { "a", "ğ" };
            CollectionAssert.AreEqual(list, BridgeJson.ParseStringList(BridgeJson.ToJson(list)));
            Assert.AreEqual(0, BridgeJson.ParseStringMap(null).Count);
            CollectionAssert.AreEqual(new[] { "x" }, BridgeJson.ParseStringList("[\"x\",1,null]"));
        }

        [Test]
        public void Options_json_contains_ios_fields_only_when_set()
        {
            var root = (Dictionary<string, object>)Json.Parse(BridgeJson.OptionsJson(new HeroSignalOptions { ApiUrl = "https://api.example.com", LogLevel = LogLevel.Info }));
            Assert.AreEqual("https://api.example.com", root["apiUrl"]);
            Assert.AreEqual(3L, root["logLevel"]);
            Assert.IsFalse(root.ContainsKey("appGroup"));
            root = (Dictionary<string, object>)Json.Parse(BridgeJson.OptionsJson(new HeroSignalOptions { IosAppGroup = "group.x", IosApnsEnvironment = "sandbox", IosProvisionalAuthorization = true }));
            Assert.AreEqual("group.x", root["appGroup"]);
            Assert.AreEqual("sandbox", root["apnsEnvironment"]);
            Assert.AreEqual(true, root["provisionalAuthorization"]);
        }
    }
}
