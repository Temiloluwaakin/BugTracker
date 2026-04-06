using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BugTracker.Data.Entities
{
    // ═══════════════════════════════════════════════════════════
    // TESTER PROFILE
    // One document per user — linked by userId.
    // Optional — users who don't want to be found don't need one.
    // Both testers and developers use this same structure.
    // ═══════════════════════════════════════════════════════════
    public class TesterProfile
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId Id { get; set; }

        /// <summary>
        /// The user this profile belongs to.
        /// Unique index — one profile per user, enforced at DB level.
        /// </summary>
        [BsonElement("userId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId UserId { get; set; }

        /// <summary>Denormalised for search results without joining users.</summary>
        [BsonElement("fullName")]
        public string FullName { get; set; } = string.Empty;

        [BsonElement("email")]
        public string Email { get; set; } = string.Empty;

        /// <summary>One-liner. e.g. "Senior QA Engineer · 5 years exp · Remote"</summary>
        [BsonElement("headline")]
        [BsonIgnoreIfNull]
        public string? Headline { get; set; }

        /// <summary>Longer bio / about section.</summary>
        [BsonElement("bio")]
        [BsonIgnoreIfNull]
        public string? Bio { get; set; }

        /// <summary>Years of professional experience.</summary>
        [BsonElement("yearsOfExperience")]
        [BsonIgnoreIfNull]
        public int? YearsOfExperience { get; set; }

        /// <summary>
        /// Skills / tech tags. e.g. ["Selenium", "Postman", "API Testing", "MongoDB"]
        /// This is the primary search field — indexed.
        /// </summary>
        [BsonElement("skills")]
        public List<string> Skills { get; set; } = new();

        /// <summary>
        /// Availability status.
        /// open        — actively looking, appears in all searches.
        /// busy        — on a project, still visible but flagged.
        /// not_looking — hidden from owner search results.
        /// </summary>
        [BsonElement("availabilityStatus")]
        [BsonRepresentation(BsonType.String)]
        public string AvailabilityStatus { get; set; } = UserAvailabilityStatus.open.ToString();

        /// <summary>permanent | contract | both</summary>
        [BsonElement("employmentTypePreference")]
        [BsonRepresentation(BsonType.String)]
        public string EmploymentTypePreference { get; set; } = employmentType.both.ToString();

        /// <summary>remote | onsite | hybrid</summary>
        [BsonElement("workTypePreference")]
        [BsonRepresentation(BsonType.String)]
        public string WorkTypePreference { get; set; } = workType.remote.ToString();

        /// <summary>
        /// Optional rate. Free text so user can write "₦50k/month" or "$25/hr".
        /// We don't enforce a format — let them express it naturally.
        /// </summary>
        [BsonElement("rateOrSalaryExpectation")]
        [BsonIgnoreIfNull]
        public string? RateOrSalaryExpectation { get; set; }

        /// <summary>Portfolio — self-reported past projects.</summary>
        [BsonElement("portfolioItems")]
        public List<PortfolioItem> PortfolioItems { get; set; } = new();

        /// <summary>Optional social/professional links.</summary>
        [BsonElement("socialLinks")]
        public ProfileSocialLinks SocialLinks { get; set; } = new();

        [BsonElement("profileStatus")]
        public string ProfileStatus { get; set; } = profileStatus.active.ToString();

        /// <summary>
        /// Profile visibility.
        /// true  — appears in owner search (only if availabilityStatus != not_looking).
        /// false — completely hidden from all discovery.
        /// </summary>
        [BsonElement("isPublic")]
        public bool IsPublic { get; set; } = true;

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; }

        [BsonElement("updatedAt")]
        public DateTime UpdatedAt { get; set; }
    }


    // PORTFOLIO ITEM — embedded subdocument
    // Self-reported past work. Not linked to BugTrackPro projects.
    public class PortfolioItem
    {
        /// <summary>Client-provided ID so the frontend can target a specific item for edit/delete.</summary>
        [BsonElement("itemId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId ItemId { get; set; } = ObjectId.GenerateNewId();

        [BsonElement("projectName")]
        public string ProjectName { get; set; } = string.Empty;

        /// <summary>Their role on the project. e.g. "Lead QA Engineer"</summary>
        [BsonElement("role")]
        public string Role { get; set; } = string.Empty;

        [BsonElement("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>Optional link to the project, case study, or write-up.</summary>
        [BsonElement("link")]
        [BsonIgnoreIfNull]
        public string? Link { get; set; }

        /// <summary>e.g. "Jan 2023 – Mar 2023" or "2022" — free text, user decides format.</summary>
        [BsonElement("duration")]
        [BsonIgnoreIfNull]
        public string? Duration { get; set; }
    }


   
    // PROFILE SOCIAL LINKS — embedded subdocument
    public class ProfileSocialLinks
    {
        [BsonElement("github")]
        [BsonIgnoreIfNull]
        public string? GitHub { get; set; }

        [BsonElement("linkedin")]
        [BsonIgnoreIfNull]
        public string? LinkedIn { get; set; }

        [BsonElement("portfolio")]
        [BsonIgnoreIfNull]
        public string? Portfolio { get; set; }

        [BsonElement("twitter")]
        [BsonIgnoreIfNull]
        public string? Twitter { get; set; }
    }


    public enum UserAvailabilityStatus
    {
        open,
        busy,
        notLooking
    }

    public enum employmentType
    {
        permanent,
        contract,
        both
    }

    public enum workType
    {
        hybrid,
        onsite,
        remote
    }

    public enum profileStatus
    {
        active,
        inactive,
    }
}
