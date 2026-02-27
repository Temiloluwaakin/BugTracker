using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace BugTracker.Data.Entities
{
    /// <summary>
    /// Tracks pending project invitations.
    /// Handles both existing users (accept in-app) and new users (accept via email link).
    /// Collection: invitations
    /// 
    /// At user registration: check invitations where invitedEmail == newUser.Email and status == Pending,
    /// then auto-add them to those projects.
    /// </summary>
    public class Invitation : BaseDocument
    {
        [BsonElement("projectId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string ProjectId { get; set; } = string.Empty;

        /// <summary>
        /// Denormalized for use in invitation email content.
        /// </summary>
        [BsonElement("projectName")]
        public string ProjectName { get; set; } = string.Empty;

        /// <summary>
        /// Lowercased email of the person being invited.
        /// May or may not correspond to an existing user at invite time.
        /// </summary>
        [BsonElement("invitedEmail")]
        public string InvitedEmail { get; set; } = string.Empty;

        [BsonElement("invitedBy")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string InvitedBy { get; set; } = string.Empty;

        /// <summary>
        /// The role this person will receive when they accept.
        /// Owners cannot be invited — only Tester or Viewer.
        /// </summary>
        [BsonElement("role")]
        [BsonRepresentation(BsonType.String)]
        public ProjectRole Role { get; set; }

        /// <summary>
        /// Secure random token used in the invite link URL.
        /// Generate with: Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        /// </summary>
        [BsonElement("token")]
        public string Token { get; set; } = string.Empty;

        [BsonElement("status")]
        [BsonRepresentation(BsonType.String)]
        public InvitationStatus Status { get; set; } = InvitationStatus.Pending;

        /// <summary>
        /// Set to 7 days from creation. A MongoDB TTL index on this field
        /// will automatically delete expired invitation documents.
        /// </summary>
        [BsonElement("expiresAt")]
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(7);

        [BsonElement("respondedAt")]
        [BsonIgnoreIfNull]
        public DateTime? RespondedAt { get; set; }
    }

    public enum InvitationStatus
    {
        Pending,
        Accepted,
        Declined,
        Expired
    }
}
