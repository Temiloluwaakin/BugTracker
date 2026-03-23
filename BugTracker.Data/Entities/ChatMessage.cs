using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BugTracker.Data.Entities
{

    // ═══════════════════════════════════════════════════════════
    // CHAT MESSAGE
    // One document per message sent in a project group chat.
    // We do NOT embed messages inside a room document because
    // chats grow unbounded — embedding would hit MongoDB's
    // 16MB document limit and make pagination impossible.
    //
    // There is no separate ChatRoom entity — the room IS the
    // project. The projectId is the room identifier.
    // One project = one group chat. Always.
    // ═══════════════════════════════════════════════════════════
    public class ChatMessage
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId Id { get; set; }

        /// <summary>
        /// The project this message belongs to.
        /// This IS the chat room identifier — no separate room document needed.
        /// Index this — every query scopes to a project.
        /// </summary>
        [BsonElement("projectId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId ProjectId { get; set; }

        // ── Sender info (denormalised for display without extra lookups) ──
        [BsonElement("senderId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId SenderId { get; set; }

        [BsonElement("senderName")]
        public string SenderName { get; set; } = string.Empty;

        [BsonElement("senderEmail")]
        public string SenderEmail { get; set; } = string.Empty;

        /// <summary>The message content. Plain text or markdown.</summary>
        [BsonElement("content")]
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// True if the sender edited the message after sending.
        /// We keep the latest content only — no edit history.
        /// </summary>
        [BsonElement("isEdited")]
        public bool IsEdited { get; set; } = false;

        /// <summary>
        /// True if the message was deleted. We soft-delete so the
        /// chat timeline doesn't get gaps — the UI shows
        /// "This message was deleted." in place of the content.
        /// </summary>
        [BsonElement("isDeleted")]
        public bool IsDeleted { get; set; } = false;

        /// <summary>
        /// Optional: reply to another message.
        /// Stores the parent message ID so the UI can show
        /// a quoted reply without fetching the full thread.
        /// </summary>
        [BsonElement("replyToId")]
        [BsonIgnoreIfNull]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId? ReplyToId { get; set; }

        /// <summary>
        /// Denormalised snippet of the replied-to message for display.
        /// Avoids a second DB call just to show the quote preview.
        /// </summary>
        [BsonElement("replyToSnippet")]
        [BsonIgnoreIfNull]
        public string? ReplyToSnippet { get; set; }

        [BsonElement("sentAt")]
        public DateTime SentAt { get; set; }

        /// <summary>Only set if the message was edited.</summary>
        [BsonElement("editedAt")]
        [BsonIgnoreIfNull]
        public DateTime? EditedAt { get; set; }
    }
}
