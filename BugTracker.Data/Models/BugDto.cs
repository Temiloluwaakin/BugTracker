using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BugTracker.Data.Models
{
    // Create Bug
    public class CreateBugRequest
    {
        [Required(ErrorMessage = "Title is required.")]
        [StringLength(200, MinimumLength = 5, ErrorMessage = "Title must be between 5 and 200 characters.")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Description is required.")]
        [StringLength(5000, ErrorMessage = "Description cannot exceed 5000 characters.")]
        public string Description { get; set; } = string.Empty;

        /// <summary>Free text — tester writes steps however they like.</summary>
        [StringLength(3000, ErrorMessage = "Steps to reproduce cannot exceed 3000 characters.")]
        public string? StepsToReproduce { get; set; }

        [StringLength(1000, ErrorMessage = "Expected behavior cannot exceed 1000 characters.")]
        public string? ExpectedBehavior { get; set; }

        [StringLength(1000, ErrorMessage = "Actual behavior cannot exceed 1000 characters.")]
        public string? ActualBehavior { get; set; }

        /// <summary>critical | high | medium | low</summary>
        [Required(ErrorMessage = "Severity is required.")]
        public string Severity { get; set; } = string.Empty;

        /// <summary>urgent | normal | low</summary>
        [Required(ErrorMessage = "Priority is required.")]
        public string Priority { get; set; } = string.Empty;

        [StringLength(200, ErrorMessage = "Environment cannot exceed 200 characters.")]
        public string? Environment { get; set; }

        [StringLength(50, ErrorMessage = "Version cannot exceed 50 characters.")]
        public string? Version { get; set; }

        /// <summary>Optional: ID of a developer (must have 'developer' role) to assign immediately.</summary>
        public string? AssignedDeveloperId { get; set; }

        public List<string>? Tags { get; set; }
    }


    // Update Bug (metadata only — not status)
    public class UpdateBugRequest
    {
        [StringLength(200, MinimumLength = 5, ErrorMessage = "Title must be between 5 and 200 characters.")]
        public string? Title { get; set; }

        [StringLength(5000, ErrorMessage = "Description cannot exceed 5000 characters.")]
        public string? Description { get; set; }

        [StringLength(3000)]
        public string? StepsToReproduce { get; set; }

        [StringLength(1000)]
        public string? ExpectedBehavior { get; set; }

        [StringLength(1000)]
        public string? ActualBehavior { get; set; }

        /// <summary>critical | high | medium | low</summary>
        public string? Severity { get; set; }

        /// <summary>urgent | normal | low</summary>
        public string? Priority { get; set; }

        [StringLength(200)]
        public string? Environment { get; set; }

        [StringLength(50)]
        public string? Version { get; set; }

        public List<string>? Tags { get; set; }
    }

    // ─────────────────────────────────────────────
    // Update Bug Status (tester only)
    // Only the tester who created the bug OR the
    // current assignee tester can call this.
    // ─────────────────────────────────────────────
    public class UpdateBugStatusRequest
    {
        /// <summary>open | in_progress | resolved | closed | wont_fix | duplicate</summary>
        [Required(ErrorMessage = "Status is required.")]
        public string Status { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "Comment cannot exceed 500 characters.")]
        public string? Comment { get; set; }

        /// <summary>Required if status is 'duplicate'. Must be a valid bug ID in the same project.</summary>
        public string? DuplicateOfBugId { get; set; }
    }

    // ─────────────────────────────────────────────
    // Update Developer Status (developer only)
    // ─────────────────────────────────────────────
    public class UpdateDeveloperStatusRequest
    {
        /// <summary>not_started | working | blocked | fixed | wont_fix</summary>
        [Required(ErrorMessage = "Developer status is required.")]
        public string DeveloperStatus { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Comment { get; set; }
    }

    // ─────────────────────────────────────────────
    // Assign Developer
    // ─────────────────────────────────────────────
    public class AssignDeveloperRequest
    {
        /// <summary>Pass null or omit to unassign the current developer.</summary>
        public string? DeveloperId { get; set; }
    }

    // ─────────────────────────────────────────────
    // Reassign Tester
    // ─────────────────────────────────────────────
    public class ReassignTesterRequest
    {
        [Required(ErrorMessage = "New tester ID is required.")]
        public string NewTesterId { get; set; } = string.Empty;
    }

    // ─────────────────────────────────────────────
    // Tester Comment (overwrite — not threaded)
    // ─────────────────────────────────────────────
    public class UpsertTesterCommentRequest
    {
        [Required(ErrorMessage = "Comment content is required.")]
        [StringLength(2000, MinimumLength = 1, ErrorMessage = "Comment cannot exceed 2000 characters.")]
        public string Content { get; set; } = string.Empty;
    }

    // ─────────────────────────────────────────────
    // Developer Comment (overwrite — not threaded)
    // ─────────────────────────────────────────────
    public class UpsertDeveloperCommentRequest
    {
        [Required(ErrorMessage = "Comment content is required.")]
        [StringLength(2000, MinimumLength = 1, ErrorMessage = "Comment cannot exceed 2000 characters.")]
        public string Content { get; set; } = string.Empty;
    }

    public class AddCommentRequest
    {
        public string Content { get; set; } = string.Empty;
    }

    // ─────────────────────────────────────────────
    // Add Attachment
    // ─────────────────────────────────────────────
    public class AddAttachmentRequest
    {
        [Required(ErrorMessage = "File URL is required.")]
        public string Url { get; set; } = string.Empty;

        [Required(ErrorMessage = "File name is required.")]
        [StringLength(255)]
        public string FileName { get; set; } = string.Empty;

        [Required(ErrorMessage = "File type is required.")]
        [StringLength(100)]
        public string FileType { get; set; } = string.Empty;
    }

    // ─────────────────────────────────────────────
    // Get Bugs Query Filter
    // ─────────────────────────────────────────────
    public class GetBugsQuery
    {
        public string? Status { get; set; }
        public string? Severity { get; set; }
        public string? Priority { get; set; }
        public string? AssignedDeveloperId { get; set; }
        public string? AssignedTesterId { get; set; }
        public string? Tag { get; set; }
        public bool loggedinUser { get; set; } = false;

        /// <summary>Page number, 1-based. Default: 1.</summary>
        public int Page { get; set; } = 1;

        /// <summary>Page size. Default: 20. Max: 100.</summary>
        public int PageSize { get; set; } = 20;
    }


    // ═════════════════════════════════════════════
    // RESPONSE DTOs
    // ═════════════════════════════════════════════
    public class BugResponse
    {
        public string Id { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public int BugNumber { get; set; }
        public string BugLabel => $"BUG-{BugNumber:D3}";   // e.g. BUG-042

        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? StepsToReproduce { get; set; }
        public string? ExpectedBehavior { get; set; }
        public string? ActualBehavior { get; set; }
        public string Severity { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;

        // ── Main bug status (tester-controlled) ──
        public string Status { get; set; } = string.Empty;

        // ── Developer status (developer-controlled) ──
        public string DeveloperStatus { get; set; } = string.Empty;

        public string? Environment { get; set; }
        public string? Version { get; set; }

        // ── People ──
        public BugPersonRef ReportedBy { get; set; } = new();
        public BugPersonRef? AssignedTester { get; set; }
        public BugPersonRef? AssignedDeveloper { get; set; }

        // ── Comments (single per role — overwrite model) ──
        public BugComment? TesterComment { get; set; }
        public BugComment? DeveloperComment { get; set; }

        public List<BugAttachmentResponse> Attachments { get; set; } = new();
        public List<string> Tags { get; set; } = new();
        public string? DuplicateOf { get; set; }

        public List<StatusHistoryResponse> StatusHistory { get; set; } = new();
        public List<BugCommentResponse> Comments { get; set; }

        public DateTime? ResolvedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class BugCommentResponse
    {
        public string Id { get; set; }
        public string AuthorId { get; set; }
        public string AuthorName { get; set; }
        public string Content { get; set; }
        public bool IsEdited { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class BugSummaryResponse
    {
        public string Id { get; set; } = string.Empty;
        public int BugNumber { get; set; }
        public string BugLabel => $"BUG-{BugNumber:D3}";
        public string Description { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string DeveloperStatus { get; set; } = string.Empty;
        public BugPersonRef? AssignedDeveloper { get; set; }
        public BugPersonRef? AssignedTester { get; set; }
        public List<string> Tags { get; set; } = new();
        public DateTime CreatedAt { get; set; }
    }

    public class BugPersonRef
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class BugComment
    {
        public string AuthorId { get; set; } = string.Empty;
        public string AuthorName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public bool IsEdited { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class BugAttachmentResponse
    {
        public string Url { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public string UploadedBy { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
    }

    public class StatusHistoryResponse
    {
        public string FromStatus { get; set; } = string.Empty;
        public string ToStatus { get; set; } = string.Empty;
        public string ChangedBy { get; set; } = string.Empty;
        public string ChangedByName { get; set; } = string.Empty;
        public string? Comment { get; set; }
        public DateTime ChangedAt { get; set; }
    }

    public class PagedBugsResponse
    {
        public List<BugSummaryResponse> Bugs { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    }

    public class BugMetricsResponse
    {
        public int TotalBugs { get; set; }

        public int Open { get; set; }
        public int InProgress { get; set; }
        public int Closed { get; set; }
        public int WontFix { get; set; }
        public int Duplicate { get; set; }

        public double CompletionPercentage { get; set; }
    }
}
