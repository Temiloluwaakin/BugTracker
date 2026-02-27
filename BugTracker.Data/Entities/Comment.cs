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
    /// A comment on a bug or test case.
    /// Uses a polymorphic pattern — EntityType tells you which collection EntityId points to.
    /// Supports threaded replies via ParentCommentId.
    /// Collection: comments
    /// </summary>
    public class Comment : BaseDocument
    {
        /// <summary>
        /// Denormalized for project-scoped queries without extra joins.
        /// </summary>
        [BsonElement("projectId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string ProjectId { get; set; } = string.Empty;

        /// <summary>
        /// Indicates which collection EntityId references.
        /// </summary>
        [BsonElement("entityType")]
        [BsonRepresentation(BsonType.String)]
        public CommentEntityType EntityType { get; set; }

        /// <summary>
        /// The document this comment belongs to (a Bug._id or TestCase._id).
        /// </summary>
        [BsonElement("entityId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string EntityId { get; set; } = string.Empty;

        /// <summary>
        /// Null for top-level comments.
        /// Set to parent Comment._id for threaded replies.
        /// Keep threading to one level deep to avoid complexity.
        /// </summary>
        [BsonElement("parentCommentId")]
        [BsonRepresentation(BsonType.ObjectId)]
        [BsonIgnoreIfNull]
        public string? ParentCommentId { get; set; }

        /// <summary>
        /// Comment body. Supports markdown.
        /// </summary>
        [BsonElement("content")]
        public string Content { get; set; } = string.Empty;

        [BsonElement("authorId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string AuthorId { get; set; } = string.Empty;

        /// <summary>
        /// True if the comment was edited after initial creation.
        /// </summary>
        [BsonElement("isEdited")]
        public bool IsEdited { get; set; } = false;
    }

    public enum CommentEntityType
    {
        Bug,
        TestCase
    }
}
