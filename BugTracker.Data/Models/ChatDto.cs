using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BugTracker.Data.Models
{
    // ─────────────────────────────────────────────
    // SEND MESSAGE
    // Used by both the REST endpoint and the SignalR hub.
    // ─────────────────────────────────────────────
    public class SendMessageRequest
    {
        [Required(ErrorMessage = "Message content is required.")]
        [StringLength(2000, MinimumLength = 1, ErrorMessage = "Message cannot exceed 2000 characters.")]
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Optional: ID of the message being replied to.
        /// If provided, the server will validate it exists in the same project
        /// and store a snippet for the quote preview.
        /// </summary>
        public string? ReplyToId { get; set; }
    }

    // ─────────────────────────────────────────────
    // EDIT MESSAGE
    // ─────────────────────────────────────────────
    public class EditMessageRequest
    {
        [Required(ErrorMessage = "Updated content is required.")]
        [StringLength(2000, MinimumLength = 1, ErrorMessage = "Message cannot exceed 2000 characters.")]
        public string Content { get; set; } = string.Empty;
    }

    // ─────────────────────────────────────────────
    // GET MESSAGES QUERY (for history / pagination)
    // Uses cursor-based pagination (before a message ID)
    // rather than page numbers — much better for chat
    // because new messages don't shift page boundaries.
    // ─────────────────────────────────────────────
    public class GetMessagesQuery
    {
        /// <summary>
        /// Load messages older than this message ID.
        /// Pass null to get the most recent messages (initial load).
        /// Pass the oldest message ID currently shown to load more history.
        /// </summary>
        public string? Before { get; set; }

        /// <summary>How many messages to return. Default 50. Max 100.</summary>
        public int Limit { get; set; } = 50;
    }


    // ═════════════════════════════════════════════
    // RESPONSE DTOs
    // ═════════════════════════════════════════════
    public class ChatMessageResponse
    {
        public string Id { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public ChatSenderResponse Sender { get; set; } = new();
        public string Content { get; set; } = string.Empty;
        public bool IsEdited { get; set; }
        public bool IsDeleted { get; set; }
        public ReplyToResponse? ReplyTo { get; set; }
        public DateTime SentAt { get; set; }
        public DateTime? EditedAt { get; set; }

        /// <summary>
        /// True if the requesting user is the sender of this message.
        /// Used by the UI to show edit/delete controls.
        /// </summary>
        public bool IsMine { get; set; }
    }

    public class ChatSenderResponse
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class ReplyToResponse
    {
        public string MessageId { get; set; } = string.Empty;

        /// <summary>
        /// Truncated preview of the replied-to message content.
        /// Max 100 chars — enough for the UI to show a quote bubble.
        /// </summary>
        public string Snippet { get; set; } = string.Empty;
    }

    /// <summary>
    /// Returned when the user loads their chat list —
    /// one entry per project they belong to.
    /// </summary>
    public class ChatRoomSummaryResponse
    {
        public string ProjectId { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string ProjectStatus { get; set; } = string.Empty;
        public int MemberCount { get; set; }

        /// <summary>The most recent message in this room. Null if no messages yet.</summary>
        public ChatMessageResponse? LastMessage { get; set; }

        /// <summary>UTC timestamp of the last message, used for sorting the chat list.</summary>
        public DateTime? LastActivityAt { get; set; }
    }

    /// <summary>
    /// The payload SignalR broadcasts to all room members
    /// when a new message is sent. Same shape as ChatMessageResponse
    /// so the client handles both REST and SignalR responses identically.
    /// </summary>
    public class NewMessageBroadcast
    {
        public string ProjectId { get; set; } = string.Empty;
        public ChatMessageResponse Message { get; set; } = new();
    }

    /// <summary>
    /// Broadcast when a message is edited.
    /// Client finds the message by ID and updates its content in place.
    /// </summary>
    public class MessageEditedBroadcast
    {
        public string ProjectId { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public string NewContent { get; set; } = string.Empty;
        public DateTime EditedAt { get; set; }
    }

    /// <summary>
    /// Broadcast when a message is deleted.
    /// Client finds the message by ID and replaces content
    /// with "This message was deleted."
    /// </summary>
    public class MessageDeletedBroadcast
    {
        public string ProjectId { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
    }
}
