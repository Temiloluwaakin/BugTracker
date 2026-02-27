using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BugTracker.Data.Entities
{
    /// <summary>
    /// Represents a bug report filed within a project.
    /// Collection: bugs
    /// </summary>
    public class Bug : BaseDocument
    {
        [BsonElement("projectId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string ProjectId { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable sequential number scoped to the project (e.g. BUG-042).
        /// Generated atomically via the counters collection — do NOT calculate as max+1.
        /// </summary>
        [BsonElement("bugNumber")]
        public int BugNumber { get; set; }

        [BsonElement("title")]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Full description. Supports markdown.
        /// </summary>
        [BsonElement("description")]
        public string Description { get; set; } = string.Empty;

        [BsonElement("stepsToReproduce")]
        public List<string> StepsToReproduce { get; set; } = new();

        [BsonElement("expectedBehavior")]
        [BsonIgnoreIfNull]
        public string? ExpectedBehavior { get; set; }

        [BsonElement("actualBehavior")]
        [BsonIgnoreIfNull]
        public string? ActualBehavior { get; set; }

        [BsonElement("severity")]
        [BsonRepresentation(BsonType.String)]
        public BugSeverity Severity { get; set; } = BugSeverity.Medium;

        [BsonElement("priority")]
        [BsonRepresentation(BsonType.String)]
        public BugPriority Priority { get; set; } = BugPriority.Normal;

        [BsonElement("status")]
        [BsonRepresentation(BsonType.String)]
        public BugStatus Status { get; set; } = BugStatus.Open;

        /// <summary>
        /// E.g. "iOS 17.2 / iPhone 14 / Safari 17"
        /// </summary>
        [BsonElement("environment")]
        [BsonIgnoreIfNull]
        public string? Environment { get; set; }

        /// <summary>
        /// App/build version where the bug was found. E.g. "2.1.4-beta"
        /// </summary>
        [BsonElement("version")]
        [BsonIgnoreIfNull]
        public string? Version { get; set; }

        [BsonElement("reportedBy")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string ReportedBy { get; set; } = string.Empty;

        /// <summary>
        /// One or more testers assigned to investigate or fix this bug.
        /// </summary>
        [BsonElement("assignedTo")]
        public List<string> AssignedTo { get; set; } = new();

        [BsonElement("attachments")]
        public List<BugAttachment> Attachments { get; set; } = new();

        [BsonElement("tags")]
        public List<string> Tags { get; set; } = new();

        /// <summary>
        /// Optional link to the test case this bug was discovered from.
        /// </summary>
        [BsonElement("linkedTestCaseId")]
        [BsonRepresentation(BsonType.ObjectId)]
        [BsonIgnoreIfNull]
        public string? LinkedTestCaseId { get; set; }

        /// <summary>
        /// If this bug is a duplicate, points to the original bug's Id.
        /// </summary>
        [BsonElement("duplicateOf")]
        [BsonRepresentation(BsonType.ObjectId)]
        [BsonIgnoreIfNull]
        public string? DuplicateOf { get; set; }

        /// <summary>
        /// Lightweight audit trail of every status change on this bug.
        /// Append-only — never remove entries.
        /// </summary>
        [BsonElement("statusHistory")]
        public List<BugStatusHistory> StatusHistory { get; set; } = new();

        [BsonElement("resolvedAt")]
        [BsonIgnoreIfNull]
        public DateTime? ResolvedAt { get; set; }
    }

    public class BugAttachment
    {
        /// <summary>
        /// URL or file path (S3, local storage, etc.)
        /// </summary>
        [BsonElement("url")]
        public string Url { get; set; } = string.Empty;

        [BsonElement("fileName")]
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// MIME type. E.g. "image/png", "video/mp4"
        /// </summary>
        [BsonElement("fileType")]
        public string FileType { get; set; } = string.Empty;

        [BsonElement("uploadedBy")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string UploadedBy { get; set; } = string.Empty;

        [BsonElement("uploadedAt")]
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }

    public class BugStatusHistory
    {
        [BsonElement("fromStatus")]
        [BsonRepresentation(BsonType.String)]
        public BugStatus FromStatus { get; set; }

        [BsonElement("toStatus")]
        [BsonRepresentation(BsonType.String)]
        public BugStatus ToStatus { get; set; }

        [BsonElement("changedBy")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string ChangedBy { get; set; } = string.Empty;

        [BsonElement("changedAt")]
        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Optional reason or note for the status change.
        /// </summary>
        [BsonElement("comment")]
        [BsonIgnoreIfNull]
        public string? Comment { get; set; }
    }

    public enum BugSeverity
    {
        /// <summary>System crash, data loss, security breach — blocks all work.</summary>
        Critical,

        /// <summary>Major functionality broken — no workaround.</summary>
        High,

        /// <summary>Feature partially broken — workaround exists.</summary>
        Medium,

        /// <summary>Minor issue, cosmetic, or edge case.</summary>
        Low
    }

    public enum BugPriority
    {
        /// <summary>Fix immediately — must go in next release.</summary>
        Urgent,

        /// <summary>Fix in current sprint/cycle.</summary>
        Normal,

        /// <summary>Fix when time permits.</summary>
        Low
    }

    public enum BugStatus
    {
        Open,
        InProgress,
        Resolved,
        Closed,
        WontFix,
        Duplicate
    }

}
