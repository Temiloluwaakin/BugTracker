using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BugTracker.Data.Entities
{
    // DIRECT MESSAGE CONVERSATION
    // One document per unique pair of users.
    // We store both user IDs in a sorted array so we can query
    // "does a conversation exist between user A and user B"
    // without worrying about order.
    public class DmConversation
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId Id { get; set; }

        /// <summary>
        /// Always exactly two participant IDs, stored in ascending string order
        /// so the pair is always unique regardless of who initiated.
        /// Index this as a unique compound index.
        /// </summary>
        [BsonElement("participantIds")]
        [BsonRepresentation(BsonType.ObjectId)]
        public List<ObjectId> ParticipantIds { get; set; } = new();

        /// <summary>Denormalised participant info for display without user lookups.</summary>
        [BsonElement("participants")]
        public List<DmParticipant> Participants { get; set; } = new();

        /// <summary>Snippet of the last message for the conversation list preview.</summary>
        [BsonElement("lastMessageSnippet")]
        [BsonIgnoreIfNull]
        public string? LastMessageSnippet { get; set; }

        [BsonElement("lastMessageAt")]
        [BsonIgnoreIfNull]
        public DateTime? LastMessageAt { get; set; }

        [BsonElement("lastMessageSenderId")]
        [BsonIgnoreIfNull]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId? LastMessageSenderId { get; set; }

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; }
    }

    // ═══════════════════════════════════════════════════════════
    // DM PARTICIPANT — embedded in DmConversation
    // ═══════════════════════════════════════════════════════════
    public class DmParticipant
    {
        [BsonElement("userId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId UserId { get; set; }

        [BsonElement("fullName")]
        public string FullName { get; set; } = string.Empty;

        [BsonElement("email")]
        public string Email { get; set; } = string.Empty;
    }

    // ═══════════════════════════════════════════════════════════
    // DIRECT MESSAGE
    // One document per message. Never embedded in the conversation
    // document — DMs can grow unbounded.
    //
    // NOT YET ACTIVE on the frontend — entity is built and ready.
    // ═══════════════════════════════════════════════════════════
    public class DirectMessage
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId Id { get; set; }

        [BsonElement("conversationId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId ConversationId { get; set; }

        [BsonElement("senderId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId SenderId { get; set; }

        [BsonElement("senderName")]
        public string SenderName { get; set; } = string.Empty;

        [BsonElement("content")]
        public string Content { get; set; } = string.Empty;

        [BsonElement("isEdited")]
        public bool IsEdited { get; set; } = false;

        [BsonElement("isDeleted")]
        public bool IsDeleted { get; set; } = false;

        [BsonElement("sentAt")]
        public DateTime SentAt { get; set; }

        [BsonElement("editedAt")]
        [BsonIgnoreIfNull]
        public DateTime? EditedAt { get; set; }
    }
}
