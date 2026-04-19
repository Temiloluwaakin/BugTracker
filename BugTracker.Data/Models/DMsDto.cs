using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BugTracker.Data.Models
{
    public class SendDmRequest
    {
        [Required(ErrorMessage = "Message content is required.")]
        [StringLength(2000, MinimumLength = 1)]
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Optional: ID of the message being replied to.
        /// If provided, the server will validate the message
        /// </summary>
        public string? ReplyToId { get; set; }
    }

    public class EditDmRequest
    {
        [Required(ErrorMessage = "Updated content is required.")]
        [StringLength(2000, MinimumLength = 1)]
        public string Content { get; set; } = string.Empty;
    }


    // ─────────────────────────────────────────────
    // FULL PROFILE (own profile or owner viewing)
    // ─────────────────────────────────────────────
    public class TesterProfileResponse
    {
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Headline { get; set; }
        public string? Bio { get; set; }
        public int? YearsOfExperience { get; set; }
        public List<string> Skills { get; set; } = new();
        public string AvailabilityStatus { get; set; } = string.Empty;
        public string EmploymentTypePreference { get; set; } = string.Empty;
        public string WorkTypePreference { get; set; } = string.Empty;
        public string? RateOrSalaryExpectation { get; set; }
        public List<PortfolioItemResponse> PortfolioItems { get; set; } = new();
        public ProfileSocialLinksResponse SocialLinks { get; set; } = new();
        public bool IsPublic { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    // ─────────────────────────────────────────────
    // PROFILE CARD (search results / discovery list)
    // Lighter than full profile — just what's needed
    // to render a card in the Jobs tab.
    // ─────────────────────────────────────────────
    public class ProfileCardResponse
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Headline { get; set; }
        public int? YearsOfExperience { get; set; }
        public List<string> Skills { get; set; } = new();
        public string AvailabilityStatus { get; set; } = string.Empty;
        public string EmploymentTypePreference { get; set; } = string.Empty;
        public string WorkTypePreference { get; set; } = string.Empty;
        public string? RateOrSalaryExpectation { get; set; }
    }

    public class PortfolioItemResponse
    {
        public string ItemId { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? Link { get; set; }
        public string? Duration { get; set; }
    }

    public class ProfileSocialLinksResponse
    {
        public string? GitHub { get; set; }
        public string? LinkedIn { get; set; }
        public string? Portfolio { get; set; }
        public string? Twitter { get; set; }
    }

    public class PagedProfilesResponse
    {
        public List<ProfileCardResponse> Profiles { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    }

    // ─────────────────────────────────────────────
    // DM RESPONSES (built now, not yet active)
    // ─────────────────────────────────────────────
    public class DmConversationResponse
    {
        public string Id { get; set; } = string.Empty;
        public DmParticipantResponse OtherParticipant { get; set; } = new();
        public DmParticipantResponse Sender { get; set; } = new();
        public string? LastMessageSnippet { get; set; }
        public DateTime? LastMessageAt { get; set; }
        public bool LastMessageIsMine { get; set; }
    }

    public class DmParticipantResponse
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class DirectMessageResponse
    {
        public string Id { get; set; } = string.Empty;
        public string ConversationId { get; set; } = string.Empty;
        public DmParticipantResponse Sender { get; set; } = new();
        public string Content { get; set; } = string.Empty;
        public bool IsEdited { get; set; }
        public bool IsDeleted { get; set; }
        public ReplyToResponse? ReplyTo { get; set; }
        public bool IsMine { get; set; }
        public DateTime SentAt { get; set; }
        public DateTime? EditedAt { get; set; }
    }

    public class PagedDmResponse
    {
        public List<DirectMessageResponse> Messages { get; set; } = new();
        public int TotalCount { get; set; }
        public string? OlderCursor { get; set; }
    }
}
