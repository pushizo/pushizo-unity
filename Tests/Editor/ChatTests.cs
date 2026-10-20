#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using PushizoSdk.Internal;
using NUnit.Framework;

namespace PushizoSdk.Tests
{
    /// <summary>Canlı sohbet (Pushizo.Chat): köprü argümanları, yanıt ayrıştırma ve olay yönlendirme.</summary>
    public sealed class ChatTests
    {
        private const string Message =
            @"{""id"":""cmsg_1"",""seq"":12,""clientMessageId"":null,""sender"":""bot"",""authorName"":""Asistan"",""text"":""Seçin""," +
            @"""attachments"":[{""id"":""att_1"",""name"":""a.png"",""type"":""image/png"",""size"":2048}]," +
            @"""quickReplies"":[{""id"":""o1"",""label"":""Satış""}],""status"":""delivered"",""createdAt"":1792490400000}";

        private FakeNativeBridge _bridge = null!;

        [SetUp]
        public void SetUp()
        {
            _bridge = new FakeNativeBridge();
            Pushizo.ResetForTests(_bridge, new ImmediateDispatcher());
            Pushizo.Initialize("app_test", new PushizoOptions { ApiUrl = "https://api.example.com" });
        }

        [TearDown]
        public void TearDown() => Pushizo.ResetForTests(new EditorNativeBridge(), new ImmediateDispatcher());

        private Dictionary<string, object?> LastArgs(string method)
        {
            var call = _bridge.ChatCalls[_bridge.ChatCalls.Count - 1];
            Assert.AreEqual(method, call.Key);
            return Json.ParseObject(call.Value);
        }

        [Test]
        public void Start_config_and_messages_are_parsed()
        {
            _bridge.ChatResponses["start"] = new KeyValuePair<string?, string?>(
                @"{""userId"":""usr_1"",""conversation"":{""id"":""conv_1"",""status"":""open"",""lastSeq"":12,""unreadCount"":1,""botActive"":true,""assigneeName"":null}}", null);
            _bridge.ChatResponses["getConfig"] = new KeyValuePair<string?, string?>(
                @"{""enabled"":true,""online"":false,""offlineMessage"":""Şu an çevrimdışıyız"",""attachmentMaxBytes"":10485760,""attachmentTypes"":[""image/png""],""language"":""tr""}", null);
            _bridge.ChatResponses["getMessages"] = new KeyValuePair<string?, string?>(
                @"{""messages"":[" + Message + @",{""id"":""cmsg_0"",""seq"":11,""sender"":""visitor"",""status"":""read""}],""hasMore"":true}", null);

            var started = Pushizo.Chat.StartAsync(name: "Ali").Result;
            Assert.AreEqual("Ali", LastArgs("start")["name"]);
            Assert.AreEqual("usr_1", started.UserId);
            Assert.IsTrue(started.Conversation!.BotActive);

            var config = Pushizo.Chat.GetConfigAsync().Result;
            Assert.IsFalse(config.Online);
            Assert.AreEqual("Şu an çevrimdışıyız", config.OfflineMessage);
            Assert.AreEqual(10485760L, config.AttachmentMaxBytes);

            var page = Pushizo.Chat.GetMessagesAsync(afterSeq: 5).Result;
            var args = LastArgs("getMessages");
            Assert.AreEqual(5L, args["afterSeq"]);
            Assert.AreEqual(50L, args["limit"]);
            CollectionAssert.AreEqual(new long[] { 11, 12 }, page.Messages.Select(m => m.Seq).ToArray());
            Assert.IsTrue(page.Messages[0].IsFromVisitor);
            Assert.AreEqual("Satış", page.Messages[1].QuickReplies![0].Label);
            Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1792490400000), page.Messages[1].CreatedAt);
        }

        [Test]
        public void Send_read_offline_and_validation()
        {
            _bridge.ChatResponses["sendMessage"] = new KeyValuePair<string?, string?>(Message, null);
            _bridge.ChatResponses["sendAttachment"] = new KeyValuePair<string?, string?>(Message, null);
            _bridge.ChatResponses["markRead"] = new KeyValuePair<string?, string?>(@"{""unreadCount"":0}", null);

            Assert.AreEqual(12L, Pushizo.Chat.SendMessageAsync("Merhaba", "o1").Result.Seq);
            Assert.AreEqual("o1", LastArgs("sendMessage")["optionId"]);
            Pushizo.Chat.SendAttachmentAsync(new byte[] { 1, 2, 3 }, "a.png", "image/png").Wait();
            Assert.AreEqual("AQID", LastArgs("sendAttachment")["base64"]);
            Assert.AreEqual(0, Pushizo.Chat.MarkReadAsync(12).Result);
            Assert.AreEqual(12L, LastArgs("markRead")["seq"]);

            var count = _bridge.ChatCalls.Count;
            Assert.Throws<ArgumentOutOfRangeException>(() => Pushizo.Chat.GetMessagesAsync(limit: 101));
            Assert.Throws<ArgumentException>(() => Pushizo.Chat.SendMessageAsync("   "));
            Assert.Throws<ArgumentException>(() => Pushizo.Chat.SendMessageAsync(new string('a', 4001)));
            Assert.Throws<ArgumentException>(() => Pushizo.Chat.SendAttachmentAsync(new byte[] { 1 }, "a.exe", "application/x-msdownload"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Pushizo.Chat.MarkReadAsync(-1));
            Assert.AreEqual(count, _bridge.ChatCalls.Count);

            _bridge.ChatResponses["leaveOfflineMessage"] = new KeyValuePair<string?, string?>(null, "HS_PLAN_FEATURE_REQUIRED");
            var failed = Pushizo.Chat.LeaveOfflineMessageAsync("ali@example.com", "Arayın");
            Assert.IsTrue(failed.IsFaulted);
            Assert.AreEqual("HS_PLAN_FEATURE_REQUIRED", ((PushizoException)failed.Exception!.InnerException!).Code);
        }

        [Test]
        public void Native_events_are_routed_and_open_is_buffered()
        {
            var events = new List<ChatEventArgs>();
            Pushizo.Chat.EventReceived += e => events.Add(e);

            _bridge.Emit("chat", @"{""type"":""message"",""message"":" + Message + "}");
            _bridge.Emit("chat", @"{""type"":""typing"",""typing"":true,""name"":""Ayşe""}");
            _bridge.Emit("chat", @"{""type"":""unread"",""count"":2}");
            _bridge.Emit("chat", @"{""type"":""gelecekteki_tur""}");

            CollectionAssert.AreEqual(new[] { ChatEventType.Message, ChatEventType.Typing, ChatEventType.UnreadCount }, events.Select(e => e.Type).ToArray());
            Assert.AreEqual("Seçin", events[0].Message!.Text);
            Assert.AreEqual("Ayşe", events[1].Name);
            Assert.AreEqual(2, events[2].Count);

            _bridge.Emit("chatOpen", @"{""conversationId"":""conv_9"",""seq"":4}");
            var opened = new List<ChatOpenedEventArgs>();
            Pushizo.Chat.Opened += e => opened.Add(e);
            Assert.AreEqual(1, opened.Count);
            Assert.AreEqual("conv_9", opened[0].ConversationId);
        }
    }
}
