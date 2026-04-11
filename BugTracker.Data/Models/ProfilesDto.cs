using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BugTracker.Data.Models
{
    // ─────────────────────────────────────────────
    // CREATE / UPDATE PROFILE
    // Same request used for both create and update.
    // All fields optional on update — only provided
    // fields get written.
    // ─────────────────────────────────────────────
    public class UpsertProfileRequest
    {
        [StringLength(150, ErrorMessage = "Headline cannot exceed 150 characters.")]
        public string? Headline { get; set; }

        [StringLength(1000, ErrorMessage = "Bio cannot exceed 1000 characters.")]
        public string? Bio { get; set; }

        [Range(0, 50, ErrorMessage = "Years of experience must be between 0 and 50.")]
        public int? YearsOfExperience { get; set; }

        /// <summary>
        /// Full replacement of skills list.
        /// Send the complete list you want saved — not just additions.
        /// </summary>
        public List<string>? Skills { get; set; }

        /// <summary>open | busy | not_looking</summary>
        public string? AvailabilityStatus { get; set; }

        /// <summary>permanent | contract | both</summary>
        public string? EmploymentTypePreference { get; set; }

        /// <summary>remote | onsite | hybrid</summary>
        public string? WorkTypePreference { get; set; }

        [StringLength(100, ErrorMessage = "Rate/salary expectation cannot exceed 100 characters.")]
        public string? RateOrSalaryExpectation { get; set; }

        public ProfileSocialLinksRequest? SocialLinks { get; set; }

        /// <summary>Whether this profile appears in owner search results.</summary>
        public bool? IsPublic { get; set; }

        public List<AddPortfolioItemRequest>? Experience {  get; set; }
    }

    public class ProfileSocialLinksRequest
    {
        [Url(ErrorMessage = "GitHub must be a valid URL.")]
        [StringLength(200)]
        public string? GitHub { get; set; }

        [Url(ErrorMessage = "LinkedIn must be a valid URL.")]
        [StringLength(200)]
        public string? LinkedIn { get; set; }

        [Url(ErrorMessage = "Portfolio must be a valid URL.")]
        [StringLength(200)]
        public string? Portfolio { get; set; }

        [StringLength(200)]
        public string? Twitter { get; set; }
    }

    // ─────────────────────────────────────────────
    // ADD PORTFOLIO ITEM
    // ─────────────────────────────────────────────
    public class AddPortfolioItemRequest
    {
        [Required(ErrorMessage = "Project name is required.")]
        [StringLength(150, ErrorMessage = "Project name cannot exceed 150 characters.")]
        public string ProjectName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Role is required.")]
        [StringLength(100, ErrorMessage = "Role cannot exceed 100 characters.")]
        public string Role { get; set; } = string.Empty;

        [Required(ErrorMessage = "Description is required.")]
        [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters.")]
        public string Description { get; set; } = string.Empty;

        [Url(ErrorMessage = "Link must be a valid URL.")]
        [StringLength(300)]
        public string? Link { get; set; }

        [Required(ErrorMessage = "Start date is requires")]
        public DateTime StartDate { get; set; }

        public DateTime? EndDate { get; set; }
    }

    // ─────────────────────────────────────────────
    // UPDATE PORTFOLIO ITEM
    // ─────────────────────────────────────────────
    public class UpdatePortfolioItemRequest
    {
        [StringLength(150)]
        public string? ProjectName { get; set; }

        [StringLength(100)]
        public string? Role { get; set; }

        [StringLength(1000)]
        public string? Description { get; set; }

        [Url]
        [StringLength(300)]
        public string? Link { get; set; }

        [StringLength(50)]
        public string? Duration { get; set; }
    }

    // ─────────────────────────────────────────────
    // SEARCH PROFILES QUERY
    // ─────────────────────────────────────────────
    public class SearchProfilesQuery
    {
        /// <summary>Filter by skill tag. e.g. "Selenium"</summary>
        public string? Skill { get; set; }

        /// <summary>open | busy — owners can't search not_looking profiles</summary>
        public string? AvailabilityStatus { get; set; }

        /// <summary>permanent | contract | both</summary>
        public string? EmploymentType { get; set; }

        /// <summary>remote | onsite | hybrid</summary>
        public string? WorkType { get; set; }

        /// <summary>Free text search against name and headline.</summary>
        public string? Search { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}
