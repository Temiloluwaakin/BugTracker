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
    /// Immutable audit trail document. One document per action.
    /// NEVER update or delete from this collection.
    /// Powers the project activity feed and change history.
    /// Collection: activityLogs
    /// </summary>
    public class ActivityLog
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        [BsonElement("projectId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string ProjectId { get; set; } = string.Empty;

        [BsonElement("actorId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string ActorId { get; set; } = string.Empty;

        /// <summary>
        /// Denormalized for display — avoids lookup when rendering activity feeds.
        /// </summary>
        [BsonElement("actorName")]
        public string ActorName { get; set; } = string.Empty;

        [BsonElement("action")]
        [BsonRepresentation(BsonType.String)]
        public ActivityAction Action { get; set; }

        [BsonElement("entityType")]
        [BsonRepresentation(BsonType.String)]
        public ActivityEntityType EntityType { get; set; }

        /// <summary>
        /// The specific document that was acted on.
        /// </summary>
        [BsonElement("entityId")]
        [BsonRepresentation(BsonType.ObjectId)]
        [BsonIgnoreIfNull]
        public string? EntityId { get; set; }

        /// <summary>
        /// Denormalized title or name of the entity (e.g. bug title, test case title).
        /// Useful for rendering activity feed entries without extra lookups.
        /// </summary>
        [BsonElement("entityTitle")]
        [BsonIgnoreIfNull]
        public string? EntityTitle { get; set; }

        /// <summary>
        /// Flexible key-value payload for action-specific data.
        /// E.g. for BugStatusChanged: { "fromStatus": "Open", "toStatus": "Resolved" }
        /// E.g. for MemberInvited: { "invitedEmail": "user@example.com", "role": "Tester" }
        /// </summary>
        [BsonElement("metadata")]
        public Dictionary<string, string> Metadata { get; set; } = new();

        /// <summary>
        /// Logs are immutable — only createdAt is needed, no updatedAt.
        /// </summary>
        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public enum ActivityAction
    {
        // Bug actions
        BugCreated,
        BugUpdated,
        BugStatusChanged,
        BugAssigned,
        BugDeleted,

        // Test case actions
        TestCaseCreated,
        TestCaseUpdated,
        TestCaseDeleted,

        // Test run actions
        TestRunLogged,

        // Comment actions
        CommentAdded,
        CommentEdited,
        CommentDeleted,

        // Project/member actions
        ProjectCreated,
        ProjectUpdated,
        MemberInvited,
        MemberAdded,
        MemberRemoved,
        MemberRoleChanged
    }

    public enum ActivityEntityType
    {
        Bug,
        TestCase,
        TestRun,
        Comment,
        Project,
        Member
    }
}
