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
    /// Represents a project. Members and their roles are embedded directly.
    /// Collection: projects
    /// </summary>
    public class Project : BaseDocument
    {
        [BsonElement("name")]
        public string Name { get; set; } = string.Empty;

        [BsonElement("description")]
        [BsonIgnoreIfNull]
        public string? Description { get; set; }

        /// <summary>
        /// The user who created the project. Denormalized from members for fast ownership queries.
        /// </summary>
        [BsonElement("ownerId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string OwnerId { get; set; } = string.Empty;

        [BsonElement("status")]
        [BsonRepresentation(BsonType.String)]
        public ProjectStatus Status { get; set; } = ProjectStatus.Active;

        /// <summary>
        /// Embedded member roster. Each entry includes the user's role.
        /// The owner is always present here with role = Owner.
        /// </summary>
        [BsonElement("members")]
        public List<ProjectMember> Members { get; set; } = new();

        [BsonElement("tags")]
        public List<string> Tags { get; set; } = new();
    }


    /// <summary>
    /// Embedded subdocument inside Project.Members.
    /// Holds denormalized user info + their role in this project.
    /// </summary>
    public class ProjectMember
    {
        [BsonElement("userId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// Denormalized from User for display without extra lookups.
        /// </summary>
        [BsonElement("email")]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Denormalized from User for display without extra lookups.
        /// </summary>
        [BsonElement("fullName")]
        public string FullName { get; set; } = string.Empty;

        [BsonElement("role")]
        [BsonRepresentation(BsonType.String)]
        public ProjectRole Role { get; set; }


        [BsonElement("joinedAt")]
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UserId of the person who added this member.
        /// </summary>
        [BsonElement("addedBy")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string AddedBy { get; set; } = string.Empty;
    }

    public enum ProjectStatus
    {
        Active,
        Archived,
        Completed
    }

    public enum ProjectRole
    {
        /// <summary>Full privileges: manage members, edit/delete project, all CRUD.</summary>
        Owner,

        /// <summary>Can create/edit bugs, create test cases, log test runs, add comments.</summary>
        Tester,

        /// <summary>Read-only access to everything in the project.</summary>
        Viewer
    }
}
