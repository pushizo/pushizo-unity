#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PushizoSdk.Internal;

namespace PushizoSdk
{
    /// <summary>Sohbet mesajı eki (<see cref="ChatManager.GetAttachmentUrlAsync"/> ile görüntülenir).</summary>
    public sealed class ChatAttachment
    {
        internal ChatAttachment(string id, string name, string type, long size)
        {
            Id = id;
            Name = name;
            Type = type;
            Size = size;
        }

        public string Id { get; }
        public string Name { get; }
        public string Type { get; }
        public long Size { get; }
    }

    /// <summary>Bot sorusunun hızlı yanıt seçeneği; seçim <see cref="ChatManager.SendMessageAsync"/> <c>optionId</c> ile gönderilir.</summary>
    public sealed class ChatQuickReply
    {
        internal ChatQuickReply(string id, string label)
        {
            Id = id;
            Label = label;
        }

        public string Id { get; }
        public string Label { get; }
    }

    /// <summary>
    /// Sohbet mesajı (ADR-0040). <see cref="Seq"/> konuşma içi sunucu sıra numarasıdır; çevrimdışı kuyrukta bekleyen
    /// yerel mesajda 0 ve <see cref="Status"/> <c>pending</c> olur.
    /// </summary>
    public sealed class ChatMessage
    {
        /// <summary>Sunucu kimliği; bekleyen yerel mesajda null.</summary>
        public string? Id { get; internal set; }
        public long Seq { get; internal set; }
        public string? ClientMessageId { get; internal set; }
        /// <summary><c>visitor</c>, <c>agent</c>, <c>bot</c> veya <c>system</c>.</summary>
        public string Sender { get; internal set; } = "system";
        public string? AuthorName { get; internal set; }
        public string? Text { get; internal set; }
        public IReadOnlyList<ChatAttachment> Attachments { get; internal set; } = Array.Empty<ChatAttachment>();
        /// <summary>Bot sorusunun seçenekleri (yoksa null).</summary>
        public IReadOnlyList<ChatQuickReply>? QuickReplies { get; internal set; }
        /// <summary><c>pending</c> (yerel), <c>sent</c>, <c>delivered</c> veya <c>read</c>.</summary>
        public string Status { get; internal set; } = "sent";
        public DateTimeOffset? CreatedAt { get; internal set; }

        /// <summary>Ziyaretçinin (bu cihazın) mesajı mı.</summary>
        public bool IsFromVisitor => Sender == "visitor";
    }

    /// <summary>Ziyaretçinin konuşması (<c>open</c>, <c>pending</c>, <c>resolved</c>).</summary>
    public sealed class ChatConversation
    {
        public string Id { get; internal set; } = string.Empty;
        public string Status { get; internal set; } = "open";
        public long LastSeq { get; internal set; }
        /// <summary>Temsilcinin okuduğu son sıra (ziyaretçi mesajlarında "okundu").</summary>
        public long AgentLastReadSeq { get; internal set; }
        public long VisitorLastReadSeq { get; internal set; }
        public int UnreadCount { get; internal set; }
        public bool BotActive { get; internal set; }
        public string? AssigneeName { get; internal set; }
        public DateTimeOffset? CreatedAt { get; internal set; }
    }

    /// <summary>Konuşma (henüz yoksa null) ve okunmamış sayısı.</summary>
    public sealed class ChatConversationState
    {
        internal ChatConversationState(ChatConversation? conversation, int unreadCount)
        {
            Conversation = conversation;
            UnreadCount = unreadCount;
        }

        public ChatConversation? Conversation { get; }
        public int UnreadCount { get; }
    }

    /// <summary>Mesaj sayfası (kronolojik).</summary>
    public sealed class ChatMessagePage
    {
        internal ChatMessagePage(IReadOnlyList<ChatMessage> messages, bool hasMore)
        {
            Messages = messages;
            HasMore = hasMore;
        }

        public IReadOnlyList<ChatMessage> Messages { get; }
        public bool HasMore { get; }
    }

    /// <summary><see cref="ChatManager.StartAsync"/> sonucu.</summary>
    public sealed class ChatIdentifyResult
    {
        internal ChatIdentifyResult(string? userId, ChatConversation? conversation)
        {
            UserId = userId;
            Conversation = conversation;
        }

        public string? UserId { get; }
        public ChatConversation? Conversation { get; }
    }

    /// <summary>Sohbet widget ayarları ve çevrimiçi durumu.</summary>
    public sealed class ChatConfig
    {
        public bool Enabled { get; internal set; }
        /// <summary>Çalışma saatleri içinde ve en az bir temsilci (veya bot) çevrimiçi.</summary>
        public bool Online { get; internal set; }
        public bool WithinBusinessHours { get; internal set; }
        public string? Color { get; internal set; }
        public string? TextColor { get; internal set; }
        public string Position { get; internal set; } = "right";
        public string Theme { get; internal set; } = "auto";
        public string? Title { get; internal set; }
        public string? Greeting { get; internal set; }
        /// <summary>Çevrimdışıyken gösterilecek metin; çevrimdışı mesaj için <see cref="ChatManager.LeaveOfflineMessageAsync"/>.</summary>
        public string? OfflineMessage { get; internal set; }
        public bool CollectEmailOffline { get; internal set; }
        public bool RequireEmail { get; internal set; }
        public bool AttachmentsEnabled { get; internal set; }
        public long AttachmentMaxBytes { get; internal set; }
        public IReadOnlyList<string> AttachmentTypes { get; internal set; } = Array.Empty<string>();
        public string Language { get; internal set; } = "en";
    }

    /// <summary>Canlı sohbet olay türü.</summary>
    public enum ChatEventType
    {
        Message,
        Typing,
        AgentRead,
        UnreadCount,
        Conversation,
        Connection,
        Error,
    }

    /// <summary>Canlı sohbet olayı; türe göre ilgili alanlar doludur.</summary>
    public sealed class ChatEventArgs : EventArgs
    {
        internal ChatEventArgs(ChatEventType type) => Type = type;

        public ChatEventType Type { get; }
        /// <summary><see cref="ChatEventType.Message"/>: yeni mesaj.</summary>
        public ChatMessage? Message { get; internal set; }
        /// <summary><see cref="ChatEventType.Typing"/>: temsilci yazıyor mu ve adı.</summary>
        public bool Typing { get; internal set; }
        public string? Name { get; internal set; }
        /// <summary><see cref="ChatEventType.AgentRead"/>: temsilcinin okuduğu son sıra.</summary>
        public long Seq { get; internal set; }
        /// <summary><see cref="ChatEventType.UnreadCount"/>: okunmamış sayısı.</summary>
        public int Count { get; internal set; }
        /// <summary><see cref="ChatEventType.Conversation"/>: konuşma kimliği, durumu ve bot etkin mi.</summary>
        public string? ConversationId { get; internal set; }
        public string? Status { get; internal set; }
        public bool BotActive { get; internal set; }
        /// <summary><see cref="ChatEventType.Connection"/>: canlı akış bağlı mı.</summary>
        public bool Connected { get; internal set; }
        /// <summary><see cref="ChatEventType.Error"/>: kalıcı hata kodu (dinleme durdu).</summary>
        public string? Code { get; internal set; }
        public string? ErrorMessage { get; internal set; }
    }

    /// <summary>Sohbet bildirimine tıklandı: uygulama sohbet ekranını açmalı.</summary>
    public sealed class ChatOpenedEventArgs : EventArgs
    {
        internal ChatOpenedEventArgs(string conversationId, long seq)
        {
            ConversationId = conversationId;
            Seq = seq;
        }

        public string ConversationId { get; }
        public long Seq { get; }
    }

    /// <summary>
    /// Canlı sohbet (<c>Pushizo.Chat</c>; ADR-0040, Android ve iOS cihaz derlemeleri). Ziyaretçi kimliği, çevrimdışı kuyruk,
    /// canlı akış (SSE + yoklama yedeği) ve push ile gelen mesajlar yerel SDK'dadır. Olaylar ana iş parçacığında çağrılır.
    /// Editor'de çağrılar <see cref="PushizoException.PlatformNotSupported"/> ile sonuçlanır.
    /// </summary>
    public sealed class ChatManager
    {
        private static readonly string[] s_attachmentTypes = { "image/png", "image/jpeg", "image/webp", "image/gif", "application/pdf", "text/plain" };
        private const long MaxAttachmentBytes = 10L * 1024 * 1024;
        private const int MaxText = 4000;

        private readonly List<ChatOpenedEventArgs> _pendingOpens = new List<ChatOpenedEventArgs>();
        private Action<ChatOpenedEventArgs>? _opened;

        internal ChatManager()
        {
        }

        /// <summary>Canlı sohbet olayları (mesaj, yazıyor, okundu, okunmamış, konuşma, bağlantı, hata).</summary>
        public event Action<ChatEventArgs>? EventReceived;

        /// <summary>Sohbet bildirimine tıklama; abone yokken (soğuk başlangıç) gelen tıklamalar ilk abonede iletilir.</summary>
        public event Action<ChatOpenedEventArgs>? Opened
        {
            add
            {
                _opened += value;
                var pending = _pendingOpens.ToArray();
                _pendingOpens.Clear();
                foreach (var args in pending) Pushizo.Raise(value, args, "Chat.Opened");
            }
            remove => _opened -= value;
        }

        /// <summary>Sohbeti etkinleştirir ve ziyaretçiyi bağlar (giriş yapmış kullanıcı, push aboneliği); konuşma varsa döner.</summary>
        public Task<ChatIdentifyResult> StartAsync(string? name = null, string? email = null, string? language = null)
        {
            var args = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(name)) args["name"] = name;
            if (!string.IsNullOrWhiteSpace(email)) args["email"] = email;
            if (!string.IsNullOrWhiteSpace(language)) args["language"] = language;
            return Call("start", args, json => ChatJson.ParseIdentify(Require(json)));
        }

        /// <summary>Widget ayarları, çevrimiçi durumu, çevrimdışı metni ve ek sınırları.</summary>
        public Task<ChatConfig> GetConfigAsync(string? language = null)
        {
            var args = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(language)) args["language"] = language;
            return Call("getConfig", args, json => ChatJson.ParseConfig(Require(json)));
        }

        /// <summary>Konuşma (yoksa null) ve okunmamış sayısı.</summary>
        public Task<ChatConversationState> GetConversationAsync() =>
            Call("getConversation", new Dictionary<string, object?>(), json => ChatJson.ParseConversationState(Require(json)));

        /// <summary>
        /// Mesajlar (kronolojik): parametresiz en yeni <paramref name="limit"/>; <paramref name="afterSeq"/> sonrakiler,
        /// <paramref name="beforeSeq"/> daha eski geçmiş (1-100).
        /// </summary>
        public Task<ChatMessagePage> GetMessagesAsync(long? afterSeq = null, long? beforeSeq = null, int limit = 50)
        {
            if (limit < 1 || limit > 100) throw new ArgumentOutOfRangeException(nameof(limit), "limit 1-100 olmalı.");
            if (afterSeq < 0) throw new ArgumentOutOfRangeException(nameof(afterSeq), "afterSeq negatif olamaz.");
            if (beforeSeq < 1) throw new ArgumentOutOfRangeException(nameof(beforeSeq), "beforeSeq en az 1 olmalı.");
            var args = new Dictionary<string, object?> { ["limit"] = (long)limit };
            if (afterSeq.HasValue) args["afterSeq"] = afterSeq.Value;
            if (beforeSeq.HasValue) args["beforeSeq"] = beforeSeq.Value;
            return Call("getMessages", args, json => ChatJson.ParsePage(Require(json)));
        }

        /// <summary>
        /// Mesaj gönderir (ilk mesaj konuşmayı açar); <paramref name="optionId"/> bot sorusunda seçilen hızlı yanıttır. Ağ
        /// yoksa mesaj yerel kuyruğa alınır ve <c>pending</c> mesaj döner; gönderilince <see cref="EventReceived"/> ile
        /// sunucu kopyası gelir.
        /// </summary>
        public Task<ChatMessage> SendMessageAsync(string text, string? optionId = null)
        {
            var args = new Dictionary<string, object?> { ["text"] = RequireText(text, nameof(text)) };
            if (!string.IsNullOrWhiteSpace(optionId)) args["optionId"] = optionId;
            return Call("sendMessage", args, json => ChatJson.ParseMessage(Require(json)));
        }

        /// <summary>Ek gönderir (PNG/JPEG/WebP/GIF/PDF/düz metin, en çok 10 MB).</summary>
        public Task<ChatMessage> SendAttachmentAsync(byte[] data, string fileName, string mimeType, string? text = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length == 0) throw new ArgumentException("Dosya boş olamaz.", nameof(data));
            if (data.LongLength > MaxAttachmentBytes) throw new ArgumentException("Dosya 10 MB sınırını aşıyor.", nameof(data));
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("fileName gerekli.", nameof(fileName));
            var mime = (mimeType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
            if (Array.IndexOf(s_attachmentTypes, mime) < 0) throw new ArgumentException("Desteklenmeyen dosya türü: " + mime + ".", nameof(mimeType));
            var args = new Dictionary<string, object?> { ["base64"] = Convert.ToBase64String(data), ["fileName"] = fileName, ["mimeType"] = mime };
            if (!string.IsNullOrWhiteSpace(text)) args["text"] = RequireText(text, nameof(text));
            return Call("sendAttachment", args, json => ChatJson.ParseMessage(Require(json)));
        }

        /// <summary><paramref name="seq"/> numarasına kadar okundu işaretler; kalan okunmamış sayısını döndürür.</summary>
        public Task<int> MarkReadAsync(long seq)
        {
            if (seq < 0) throw new ArgumentOutOfRangeException(nameof(seq), "seq negatif olamaz.");
            return Call("markRead", new Dictionary<string, object?> { ["seq"] = seq }, json => ChatJson.ParseUnread(Require(json)));
        }

        /// <summary>Okunmamış sayısını sunucudan yeniler.</summary>
        public Task<int> RefreshUnreadCountAsync() =>
            Call("refreshUnreadCount", new Dictionary<string, object?>(), json => ChatJson.ParseUnread(Require(json)));

        /// <summary>Son bilinen okunmamış sayısı (ağ çağrısı yok).</summary>
        public Task<int> GetUnreadCountAsync() =>
            Call("getUnreadCount", new Dictionary<string, object?>(), json => ChatJson.ParseUnread(Require(json)));

        /// <summary>Çevrimdışı / çalışma saatleri dışında e-postayla mesaj bırakır.</summary>
        public Task<ChatMessage> LeaveOfflineMessageAsync(string email, string text, string? name = null)
        {
            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("email gerekli.", nameof(email));
            var args = new Dictionary<string, object?> { ["email"] = email.Trim(), ["text"] = RequireText(text, nameof(text)) };
            if (!string.IsNullOrWhiteSpace(name)) args["name"] = name;
            return Call("leaveOfflineMessage", args, json => ChatJson.ParseMessage(Require(json)));
        }

        /// <summary>Yazıyor göstergesi (yerel SDK tekrarları seyreltir).</summary>
        public Task SetTypingAsync(bool typing) => Call<object?>("setTyping", new Dictionary<string, object?> { ["typing"] = typing }, _ => null);

        /// <summary>Canlı dinlemeyi başlatır; koparsa yerel SDK geri çekilmeyle yeniden bağlanır ve eksikleri yoklar.</summary>
        public Task StartListeningAsync() => Call<object?>("startListening", new Dictionary<string, object?>(), _ => null);

        /// <summary>Canlı dinlemeyi durdurur (arka plana geçişte önerilir; temsilci mesajı push ile gelir).</summary>
        public Task StopListeningAsync() => Call<object?>("stopListening", new Dictionary<string, object?>(), _ => null);

        /// <summary>Ek dosyasının adresi; ziyaretçi belirtecini taşır, yalnızca görsel yükleyiciye verin ve loglamayın.</summary>
        public Task<string> GetAttachmentUrlAsync(string attachmentId, bool download = false)
        {
            if (string.IsNullOrWhiteSpace(attachmentId)) throw new ArgumentException("attachmentId gerekli.", nameof(attachmentId));
            return Call("attachmentUrl", new Dictionary<string, object?> { ["attachmentId"] = attachmentId, ["download"] = download },
                json => ChatJson.ParseString(Require(json), "url") ?? throw new PushizoException(PushizoException.InvalidResponse, "Ek adresi alınamadı."));
        }

        /// <summary>Bu cihazın ziyaretçi kimliği (sohbet henüz kullanılmadıysa null).</summary>
        public Task<string?> GetVisitorIdAsync() =>
            Call("getVisitorId", new Dictionary<string, object?>(), json => ChatJson.ParseString(Require(json), "visitorId"));

        private static Task<T> Call<T>(string method, Dictionary<string, object?> args, Func<string?, T> map)
        {
            var bridge = Pushizo.Bridge();
            var argsJson = Json.Serialize(args);
            return Pushizo.Call(r => bridge.ChatCall(method, argsJson, r), map);
        }

        private static string Require(string? json) =>
            json ?? throw new PushizoException(PushizoException.InvalidResponse, "Köprü yanıtı boş.");

        private static string RequireText(string? text, string name)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Mesaj metni boş olamaz.", name);
            if (text!.Trim().Length > MaxText) throw new ArgumentException("Mesaj en fazla " + MaxText + " karakter olabilir.", name);
            return text;
        }

        /// <summary>Ana iş parçacığında: yerel <c>chat</c> olayı.</summary>
        internal void Dispatch(ChatEventArgs args) => Pushizo.Raise(EventReceived, args, "Chat.EventReceived");

        /// <summary>Ana iş parçacığında: yerel <c>chatOpen</c> olayı; abone yoksa bekletilir.</summary>
        internal void DispatchOpened(ChatOpenedEventArgs args)
        {
            if (_opened == null)
            {
                if (_pendingOpens.Count >= 20) _pendingOpens.RemoveAt(0);
                _pendingOpens.Add(args);
                return;
            }
            Pushizo.Raise(_opened, args, "Chat.Opened");
        }

        internal void Reset()
        {
            EventReceived = null;
            _opened = null;
            _pendingOpens.Clear();
        }
    }

    /// <summary>Yerel köprünün sohbet JSON biçimleri (.NET MAUI köprüsüyle aynı; zamanlar epoch ms).</summary>
    internal static class ChatJson
    {
        public static ChatMessage ParseMessage(string json) => ReadMessage(Json.ParseObject(json));

        public static ChatMessagePage ParsePage(string json)
        {
            var root = Json.ParseObject(json);
            var messages = new List<ChatMessage>();
            if (root.TryGetValue("messages", out var arr) && arr is List<object?> list)
                foreach (var item in list)
                    if (item is Dictionary<string, object?> m) messages.Add(ReadMessage(m));
            messages.Sort((a, b) => a.Seq.CompareTo(b.Seq));
            return new ChatMessagePage(messages, Bool(root, "hasMore"));
        }

        public static ChatConversationState ParseConversationState(string json)
        {
            var root = Json.ParseObject(json);
            return new ChatConversationState(ReadConversation(root, "conversation"), (int)Long(root, "unreadCount"));
        }

        public static ChatIdentifyResult ParseIdentify(string json)
        {
            var root = Json.ParseObject(json);
            return new ChatIdentifyResult(Str(root, "userId"), ReadConversation(root, "conversation"));
        }

        public static ChatConfig ParseConfig(string json)
        {
            var e = Json.ParseObject(json);
            var types = new List<string>();
            if (e.TryGetValue("attachmentTypes", out var arr) && arr is List<object?> list)
                foreach (var t in list)
                    if (t is string s) types.Add(s);
            return new ChatConfig
            {
                Enabled = Bool(e, "enabled"),
                Online = Bool(e, "online"),
                WithinBusinessHours = Bool(e, "withinBusinessHours"),
                Color = Str(e, "color"),
                TextColor = Str(e, "textColor"),
                Position = Str(e, "position") ?? "right",
                Theme = Str(e, "theme") ?? "auto",
                Title = Str(e, "title"),
                Greeting = Str(e, "greeting"),
                OfflineMessage = Str(e, "offlineMessage"),
                CollectEmailOffline = Bool(e, "collectEmailOffline"),
                RequireEmail = Bool(e, "requireEmail"),
                AttachmentsEnabled = Bool(e, "attachmentsEnabled"),
                AttachmentMaxBytes = Long(e, "attachmentMaxBytes"),
                AttachmentTypes = types,
                Language = Str(e, "language") ?? "en",
            };
        }

        public static int ParseUnread(string json) => (int)Long(Json.ParseObject(json), "unreadCount");

        public static string? ParseString(string json, string name) => Str(Json.ParseObject(json), name);

        /// <summary><c>chat</c> olayı; bilinmeyen tür (daha yeni yerel köprü) null döner.</summary>
        public static ChatEventArgs? ParseEvent(string json)
        {
            var e = Json.ParseObject(json);
            switch (Str(e, "type"))
            {
                case "message":
                    return e.TryGetValue("message", out var m) && m is Dictionary<string, object?> message
                        ? new ChatEventArgs(ChatEventType.Message) { Message = ReadMessage(message) }
                        : null;
                case "typing": return new ChatEventArgs(ChatEventType.Typing) { Typing = Bool(e, "typing"), Name = Str(e, "name") };
                case "read": return new ChatEventArgs(ChatEventType.AgentRead) { Seq = Long(e, "seq") };
                case "unread": return new ChatEventArgs(ChatEventType.UnreadCount) { Count = (int)Long(e, "count") };
                case "conversation":
                    return new ChatEventArgs(ChatEventType.Conversation)
                    {
                        ConversationId = Str(e, "conversationId"),
                        Status = Str(e, "status") ?? "open",
                        BotActive = Bool(e, "botActive"),
                    };
                case "connection": return new ChatEventArgs(ChatEventType.Connection) { Connected = Bool(e, "connected") };
                case "error": return new ChatEventArgs(ChatEventType.Error) { Code = Str(e, "code") ?? "HS_ERROR", ErrorMessage = Str(e, "message") };
                default: return null;
            }
        }

        /// <summary><c>chatOpen</c> olayı: <c>{"conversationId","seq"}</c>.</summary>
        public static ChatOpenedEventArgs ParseOpened(string json)
        {
            var e = Json.ParseObject(json);
            return new ChatOpenedEventArgs(Str(e, "conversationId") ?? string.Empty, Long(e, "seq"));
        }

        private static ChatMessage ReadMessage(Dictionary<string, object?> e)
        {
            var attachments = new List<ChatAttachment>();
            if (e.TryGetValue("attachments", out var arr) && arr is List<object?> list)
                foreach (var item in list)
                    if (item is Dictionary<string, object?> a)
                        attachments.Add(new ChatAttachment(Str(a, "id") ?? string.Empty, Str(a, "name") ?? string.Empty, Str(a, "type") ?? string.Empty, Long(a, "size")));
            List<ChatQuickReply>? replies = null;
            if (e.TryGetValue("quickReplies", out var qr) && qr is List<object?> qrList)
            {
                replies = new List<ChatQuickReply>();
                foreach (var item in qrList)
                    if (item is Dictionary<string, object?> q) replies.Add(new ChatQuickReply(Str(q, "id") ?? string.Empty, Str(q, "label") ?? string.Empty));
            }
            return new ChatMessage
            {
                Id = Str(e, "id"),
                Seq = Long(e, "seq"),
                ClientMessageId = Str(e, "clientMessageId"),
                Sender = Str(e, "sender") ?? "system",
                AuthorName = Str(e, "authorName"),
                Text = Str(e, "text"),
                Attachments = attachments,
                QuickReplies = replies,
                Status = Str(e, "status") ?? "sent",
                CreatedAt = Millis(e, "createdAt"),
            };
        }

        private static ChatConversation? ReadConversation(Dictionary<string, object?> root, string name)
        {
            if (!root.TryGetValue(name, out var value) || !(value is Dictionary<string, object?> c)) return null;
            var id = Str(c, "id");
            if (id == null) return null;
            return new ChatConversation
            {
                Id = id,
                Status = Str(c, "status") ?? "open",
                LastSeq = Long(c, "lastSeq"),
                AgentLastReadSeq = Long(c, "agentLastReadSeq"),
                VisitorLastReadSeq = Long(c, "visitorLastReadSeq"),
                UnreadCount = (int)Long(c, "unreadCount"),
                BotActive = Bool(c, "botActive"),
                AssigneeName = Str(c, "assigneeName"),
                CreatedAt = Millis(c, "createdAt"),
            };
        }

        private static string? Str(Dictionary<string, object?> e, string name) =>
            e.TryGetValue(name, out var v) ? v as string : null;

        private static bool Bool(Dictionary<string, object?> e, string name) =>
            e.TryGetValue(name, out var v) && v is bool b && b;

        private static long Long(Dictionary<string, object?> e, string name)
        {
            if (!e.TryGetValue(name, out var v)) return 0L;
            if (v is long l) return l;
            if (v is double d) return (long)d;
            return 0L;
        }

        private static DateTimeOffset? Millis(Dictionary<string, object?> e, string name)
        {
            if (!e.TryGetValue(name, out var v)) return null;
            if (v is long ms) return DateTimeOffset.FromUnixTimeMilliseconds(ms);
            if (v is double d) return DateTimeOffset.FromUnixTimeMilliseconds((long)d);
            return null;
        }
    }
}
