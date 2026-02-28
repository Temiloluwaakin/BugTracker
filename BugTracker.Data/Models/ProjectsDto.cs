using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BugTracker.Data.Models
{
    // ─────────────────────────────────────────────
    // Create Project
    // ─────────────────────────────────────────────
    public class CreateProjectRequest
    {
        [Required(ErrorMessage = "Project name is required.")]
        [StringLength(100, MinimumLength = 3, ErrorMessage = "Project name must be between 3 and 100 characters.")]
        public string Name { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
        public string? Description { get; set; }

        /// <summary>Optional tags e.g. ["ios", "regression"]</summary>
        public List<string>? Tags { get; set; }
    }

    // ─────────────────────────────────────────────
    // Update Project
    // ─────────────────────────────────────────────
    public class UpdateProjectRequest
    {
        [StringLength(100, MinimumLength = 3, ErrorMessage = "Project name must be between 3 and 100 characters.")]
        public string? Name { get; set; }

        [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
        public string? Description { get; set; }

        /// <summary>active | archived | completed</summary>
        public string? Status { get; set; }

        public List<string>? Tags { get; set; }
    }

    // ─────────────────────────────────────────────
    // Invite Member
    // ─────────────────────────────────────────────
    public class InviteMemberRequest
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "A valid email address is required.")]
        public string Email { get; set; } = string.Empty;

        /// <summary>tester | viewer — owners cannot be invited, only the creator gets owner role.</summary>
        [Required(ErrorMessage = "Role is required.")]
        public string Role { get; set; } = string.Empty;
    }

    // ─────────────────────────────────────────────
    // Update Member Role
    // ─────────────────────────────────────────────
    public class UpdateMemberRoleRequest
    {
        /// <summary>tester | viewer — cannot promote to owner via this endpoint.</summary>
        [Required(ErrorMessage = "Role is required.")]
        public string Role { get; set; } = string.Empty;
    }


    // ═════════════════════════════════════════════
    // RESPONSE DTOs
    // ═════════════════════════════════════════════

    public class ProjectResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string OwnerId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new();
        public List<MemberResponse> Members { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ProjectSummaryResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Status { get; set; } = string.Empty;
        public string YourRole { get; set; } = string.Empty;   // the caller's role in this project
        public int MemberCount { get; set; }
        public List<string> Tags { get; set; } = new();
        public DateTime CreatedAt { get; set; }
    }

    public class MemberResponse
    {
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime JoinedAt { get; set; }
        public string AddedBy { get; set; } = string.Empty;
    }
}
