using BugTracker.Data;
using BugTracker.Data.Context;
using BugTracker.Data.Entities;
using BugTracker.Data.Models;
using BugTracker.Services.Helpers;
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace BugTracker.Services.Services
{
    public interface IProfileService
    {
        /// <summary>Create or fully replace the caller's profile.</summary>
        Task<ApiResponse<TesterProfileResponse>> UpsertProfileAsync(string actorUserId, UpsertProfileRequest request, CancellationToken token);

        /// <summary>Get the caller's own profile.</summary>
        Task<ApiResponse<TesterProfileResponse>> GetMyProfileAsync(string actorUserId, CancellationToken token);

        /// <summary>Get any public profile by userId. Any authenticated user can view.</summary>
        Task<ApiResponse<TesterProfileResponse>> GetProfileByUserIdAsync(string actorUserId, string targetUserId, CancellationToken token);

        /// <summary>Search/browse public profiles. Available to all authenticated users.</summary>
        Task<ApiResponse<PagedProfilesResponse>> SearchProfilesAsync(string actorUserId, SearchProfilesQuery query, CancellationToken token);

        /// <summary>Add a portfolio item to the caller's profile.</summary>
        Task<ApiResponse<TesterProfileResponse>> AddPortfolioItemAsync(string actorUserId, AddPortfolioItemRequest request, CancellationToken token);

        /// <summary>Update a specific portfolio item by its itemId.</summary>
        Task<ApiResponse<TesterProfileResponse>> UpdatePortfolioItemAsync(string actorUserId, string itemId, UpdatePortfolioItemRequest request, CancellationToken token);

        /// <summary>Remove a portfolio item by its itemId.</summary>
        Task<ApiResponse<TesterProfileResponse>> DeletePortfolioItemAsync(string actorUserId, string itemId, CancellationToken token);

        /// <summary>Delete the caller's entire profile.</summary>
        Task<ApiResponse<object>> DeleteProfileAsync(string actorUserId, CancellationToken token);
    }


    public class ProfileService : IProfileService
    {
        private readonly DatabaseContext _db;
        private readonly IResponseHelper _responseHelper;

        private static readonly HashSet<string> ValidAvailabilities = new() { "open", "busy", "notLooking" };
        private static readonly HashSet<string> ValidEmploymentTypes = new() { "permanent", "contract", "both" };
        private static readonly HashSet<string> ValidWorkTypes = new() { "remote", "onsite", "hybrid" };

        // Owners can only search open/busy — not_looking hides people from discovery
        private static readonly HashSet<string> SearchableAvailabilities = new() { "open", "busy" };

        public ProfileService(DatabaseContext db,
            IResponseHelper responseHelper
            )
        {
            _db = db;
            _responseHelper = responseHelper;
        }

        // ═══════════════════════════════════════════
        // UPSERT PROFILE
        // Creates the profile if it doesn't exist,
        // updates it if it does. One profile per user.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<TesterProfileResponse>> UpsertProfileAsync(
            string actorUserId,
            UpsertProfileRequest request,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                // Validate enums if provided
                if (request.AvailabilityStatus is not null &&
                    !ValidAvailabilities.Contains(request.AvailabilityStatus))
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"Invalid availability status. Allowed: {string.Join(", ", ValidAvailabilities)}.");

                if (request.EmploymentTypePreference is not null &&
                    !ValidEmploymentTypes.Contains(request.EmploymentTypePreference))
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"Invalid employment type. Allowed: {string.Join(", ", ValidEmploymentTypes)}.");

                if (request.WorkTypePreference is not null &&
                    !ValidWorkTypes.Contains(request.WorkTypePreference))
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"Invalid work type. Allowed: {string.Join(", ", ValidWorkTypes)}.");

                // Fetch the user for denormalised fields
                var user = await _db.Users.Find(u => u.Id == actorUserId).FirstOrDefaultAsync(token);
                if (user is null)
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "User account not found.");

                // Check if profile already exists
                var existing = await _db.TesterProfiles
                    .Find(p => p.UserId == actorObjId && p.ProfileStatus == profileStatus.active.ToString())
                    .FirstOrDefaultAsync(token);

                var now = DateTime.UtcNow;

                if (existing is null)
                {
                    // ── CREATE ──
                    var profile = new TesterProfile
                    {
                        UserId = actorObjId,
                        FullName = user.FullName,
                        Email = user.Email,
                        Headline = request.Headline?.Trim(),
                        Bio = request.Bio?.Trim(),
                        YearsOfExperience = request.YearsOfExperience,
                        Skills = SanitiseSkills(request.Skills),
                        AvailabilityStatus = request.AvailabilityStatus ?? "not_looking",
                        EmploymentTypePreference = request.EmploymentTypePreference ?? "both",
                        WorkTypePreference = request.WorkTypePreference ?? "remote",
                        RateOrSalaryExpectation = request.RateOrSalaryExpectation?.Trim(),
                        SocialLinks = MapSocialLinks(request.SocialLinks),
                        IsPublic = request.IsPublic ?? true,
                        PortfolioItems = new List<PortfolioItem>(),
                        CreatedAt = now,
                        UpdatedAt = now
                    };

                    await _db.TesterProfiles.InsertOneAsync(profile, cancellationToken: token);

                    Log.Information("Profile created for user {UserId}.", actorUserId);
                    return Ok(MapToProfileResponse(profile));
                }
                else
                {
                    // ── UPDATE — only set fields that were actually provided ──
                    var updates = new List<UpdateDefinition<TesterProfile>>();

                    if (request.Headline is not null)
                        updates.Add(Builders<TesterProfile>.Update.Set(p => p.Headline, request.Headline.Trim()));

                    if (request.Bio is not null)
                        updates.Add(Builders<TesterProfile>.Update.Set(p => p.Bio, request.Bio.Trim()));

                    if (request.YearsOfExperience.HasValue)
                        updates.Add(Builders<TesterProfile>.Update.Set(p => p.YearsOfExperience, request.YearsOfExperience));

                    if (request.Skills is not null)
                        updates.Add(Builders<TesterProfile>.Update.Set(p => p.Skills, SanitiseSkills(request.Skills)));

                    if (request.AvailabilityStatus is not null)
                        updates.Add(Builders<TesterProfile>.Update.Set(p => p.AvailabilityStatus, request.AvailabilityStatus));

                    if (request.EmploymentTypePreference is not null)
                        updates.Add(Builders<TesterProfile>.Update.Set(p => p.EmploymentTypePreference, request.EmploymentTypePreference));

                    if (request.WorkTypePreference is not null)
                        updates.Add(Builders<TesterProfile>.Update.Set(p => p.WorkTypePreference, request.WorkTypePreference));

                    if (request.RateOrSalaryExpectation is not null)
                        updates.Add(Builders<TesterProfile>.Update.Set(p => p.RateOrSalaryExpectation, request.RateOrSalaryExpectation.Trim()));

                    if (request.SocialLinks is not null)
                        updates.Add(Builders<TesterProfile>.Update.Set(p => p.SocialLinks, MapSocialLinks(request.SocialLinks)));

                    if (request.IsPublic.HasValue)
                        updates.Add(Builders<TesterProfile>.Update.Set(p => p.IsPublic, request.IsPublic.Value));

                    // Always keep name/email in sync with the user record
                    updates.Add(Builders<TesterProfile>.Update.Set(p => p.FullName, user.FullName));
                    updates.Add(Builders<TesterProfile>.Update.Set(p => p.Email, user.Email));
                    updates.Add(Builders<TesterProfile>.Update.Set(p => p.UpdatedAt, now));

                    if (updates.Count == 3) // only the sync fields — nothing meaningful changed
                        _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "No valid fields provided to update.");

                    await _db.TesterProfiles.UpdateOneAsync(
                        p => p.UserId == actorObjId,
                        Builders<TesterProfile>.Update.Combine(updates),
                        cancellationToken: token);

                    Log.Information("Profile updated for user {UserId}.", actorUserId);

                    var updated = await _db.TesterProfiles.Find(p => p.UserId == actorObjId && p.ProfileStatus == profileStatus.active.ToString()).FirstOrDefaultAsync(token);
                    return Ok(MapToProfileResponse(updated!));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error upserting profile for user {UserId}.", actorUserId);
                return SystemError<TesterProfileResponse>();
            }
        }

        // ═══════════════════════════════════════════
        // GET MY PROFILE
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<TesterProfileResponse>> GetMyProfileAsync(
            string actorUserId,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                var profile = await _db.TesterProfiles
                    .Find(p => p.UserId == actorObjId && p.ProfileStatus == profileStatus.active.ToString())
                    .FirstOrDefaultAsync(token);

                if (profile is null)
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.NoRecordReturned.ResponseCode,
                        "You don't have a profile yet. Create one to appear in the Jobs tab.");

                return Ok(MapToProfileResponse(profile));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching profile for user {UserId}.", actorUserId);
                return SystemError<TesterProfileResponse>();
            }
        }

        // ═══════════════════════════════════════════
        // GET PROFILE BY USER ID (public view)
        // Any authenticated user can view a public profile.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<TesterProfileResponse>> GetProfileByUserIdAsync(
            string actorUserId,
            string targetUserId,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out _))
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                if (!ObjectId.TryParse(targetUserId, out var targetObjId))
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid target user ID format.");

                var profile = await _db.TesterProfiles
                    .Find(p => p.UserId == targetObjId && p.ProfileStatus == profileStatus.active.ToString())
                    .FirstOrDefaultAsync(token);

                if (profile is null)
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "This user does not have a profile.");

                // If the viewer is looking at someone else's profile,
                // only show it if it's public
                if (actorUserId != targetUserId && !profile.IsPublic)
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.NoRecordReturned.ResponseCode,
                        "This profile is not publicly visible.");

                return Ok(MapToProfileResponse(profile));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching profile for target user {TargetUserId}.", targetUserId);
                return SystemError<TesterProfileResponse>();
            }
        }

        // ═══════════════════════════════════════════
        // SEARCH PROFILES
        // Only shows public profiles that are not "not_looking".
        // Available to all authenticated users.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<PagedProfilesResponse>> SearchProfilesAsync(
            string actorUserId,
            SearchProfilesQuery query,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out _))
                    _responseHelper.Fail<PagedProfilesResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                var fb = Builders<TesterProfile>.Filter;
                var filters = new List<FilterDefinition<TesterProfile>>
                {
                    // Only show public profiles
                    fb.Eq(p => p.IsPublic, true),
                    // only show active profiles
                    fb.Eq(p => p.ProfileStatus, profileStatus.active.ToString()),
                    // Never show not_looking profiles in search
                    fb.Ne(p => p.AvailabilityStatus, UserAvailabilityStatus.notLooking.ToString())
                };

                // Filter by specific availability if provided
                if (!string.IsNullOrWhiteSpace(query.AvailabilityStatus))
                {
                    if (!SearchableAvailabilities.Contains(query.AvailabilityStatus))
                        _responseHelper.Fail<PagedProfilesResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                            "Availability filter must be 'open' or 'busy'.");

                    filters.Add(fb.Eq(p => p.AvailabilityStatus, query.AvailabilityStatus));
                }

                // Filter by skill — case-insensitive contains
                if (!string.IsNullOrWhiteSpace(query.Skill))
                    filters.Add(fb.AnyEq(p => p.Skills, query.Skill.Trim().ToLowerInvariant()));

                if (!string.IsNullOrWhiteSpace(query.EmploymentType))
                {
                    if (!ValidEmploymentTypes.Contains(query.EmploymentType))
                        _responseHelper.Fail<PagedProfilesResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                            $"Invalid employment type filter. Allowed: {string.Join(", ", ValidEmploymentTypes)}.");

                    // Match "both" preference alongside the specific type requested
                    filters.Add(fb.Or(
                        fb.Eq(p => p.EmploymentTypePreference, query.EmploymentType),
                        fb.Eq(p => p.EmploymentTypePreference, "both")
                    ));
                }

                if (!string.IsNullOrWhiteSpace(query.WorkType))
                {
                    if (!ValidWorkTypes.Contains(query.WorkType))
                        _responseHelper.Fail<PagedProfilesResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                            $"Invalid work type filter. Allowed: {string.Join(", ", ValidWorkTypes)}.");

                    filters.Add(fb.Eq(p => p.WorkTypePreference, query.WorkType));
                }

                // Free text search against name and headline
                if (!string.IsNullOrWhiteSpace(query.Search))
                {
                    var searchRegex = new MongoDB.Bson.BsonRegularExpression(query.Search.Trim(), "i");
                    filters.Add(fb.Or(
                        fb.Regex(p => p.FullName, searchRegex),
                        fb.Regex(p => p.Headline, searchRegex)
                    ));
                }

                var combined = fb.And(filters);
                var pageSize = Math.Clamp(query.PageSize, 1, 100);
                var page = Math.Max(query.Page, 1);
                var skip = (page - 1) * pageSize;
                var totalCount = (int)await _db.TesterProfiles.CountDocumentsAsync(combined, cancellationToken: token);

                // Sort: open profiles first, then busy, then by name
                var profiles = await _db.TesterProfiles
                    .Find(combined)
                    .SortBy(p => p.AvailabilityStatus == "open" ? 0 : 1)
                    .ThenBy(p => p.FullName)
                    .Skip(skip)
                    .Limit(pageSize)
                    .ToListAsync(token);

                return Ok(new PagedProfilesResponse
                {
                    Profiles = profiles.Select(MapToProfileCard).ToList(),
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error searching profiles.");
                return SystemError<PagedProfilesResponse>();
            }
        }

        // ═══════════════════════════════════════════
        // ADD PORTFOLIO ITEM
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<TesterProfileResponse>> AddPortfolioItemAsync(
            string actorUserId,
            AddPortfolioItemRequest request,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                var profile = await _db.TesterProfiles
                    .Find(p => p.UserId == actorObjId && p.ProfileStatus == profileStatus.active.ToString())
                    .FirstOrDefaultAsync(token);

                if (profile is null)
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.NoRecordReturned.ResponseCode,
                        "Create your profile before adding portfolio items.");

                // Max 20 portfolio items — keeps profiles focused
                if (profile.PortfolioItems.Count >= 20)
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        "You can have a maximum of 20 portfolio items.");

                var item = new PortfolioItem
                {
                    ItemId = ObjectId.GenerateNewId(),
                    ProjectName = request.ProjectName.Trim(),
                    Role = request.Role.Trim(),
                    Description = request.Description.Trim(),
                    Link = request.Link?.Trim(),
                    Duration = request.Duration?.Trim()
                };

                var update = Builders<TesterProfile>.Update.Combine(
                    Builders<TesterProfile>.Update.Push(p => p.PortfolioItems, item),
                    Builders<TesterProfile>.Update.Set(p => p.UpdatedAt, DateTime.UtcNow)
                );

                await _db.TesterProfiles.UpdateOneAsync(p => p.UserId == actorObjId, update, cancellationToken: token);

                var updated = await _db.TesterProfiles.Find(p => p.UserId == actorObjId).FirstOrDefaultAsync(token);
                return Ok(MapToProfileResponse(updated!));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error adding portfolio item for user {UserId}.", actorUserId);
                return SystemError<TesterProfileResponse>();
            }
        }

        // ═══════════════════════════════════════════
        // UPDATE PORTFOLIO ITEM
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<TesterProfileResponse>> UpdatePortfolioItemAsync(
            string actorUserId,
            string itemId,
            UpdatePortfolioItemRequest request,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                if (!ObjectId.TryParse(itemId, out var itemObjId))
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid portfolio item ID format.");

                var profile = await _db.TesterProfiles
                    .Find(p => p.UserId == actorObjId && p.ProfileStatus == profileStatus.active.ToString())
                    .FirstOrDefaultAsync(token);

                if (profile is null)
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Profile not found.");

                var item = profile.PortfolioItems.FirstOrDefault(i => i.ItemId == itemObjId);
                if (item is null)
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Portfolio item not found.");

                // Apply partial updates to the in-memory item then replace the whole array.
                // MongoDB's positional operator on nested arrays requires arrayFilters which is
                // verbose — replacing the array is simpler and safe given the 20 item cap.
                if (request.ProjectName is not null) item.ProjectName = request.ProjectName.Trim();
                if (request.Role is not null) item.Role = request.Role.Trim();
                if (request.Description is not null) item.Description = request.Description.Trim();
                if (request.Link is not null) item.Link = request.Link.Trim();
                if (request.Duration is not null) item.Duration = request.Duration.Trim();

                var update = Builders<TesterProfile>.Update.Combine(
                    Builders<TesterProfile>.Update.Set(p => p.PortfolioItems, profile.PortfolioItems),
                    Builders<TesterProfile>.Update.Set(p => p.UpdatedAt, DateTime.UtcNow)
                );

                await _db.TesterProfiles.UpdateOneAsync(p => p.UserId == actorObjId, update, cancellationToken: token);

                var updated = await _db.TesterProfiles.Find(p => p.UserId == actorObjId).FirstOrDefaultAsync(token);
                return Ok(MapToProfileResponse(updated!));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error updating portfolio item for user {UserId}.", actorUserId);
                return SystemError<TesterProfileResponse>();
            }
        }

        // ═══════════════════════════════════════════
        // DELETE PORTFOLIO ITEM
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<TesterProfileResponse>> DeletePortfolioItemAsync(
            string actorUserId,
            string itemId,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                if (!ObjectId.TryParse(itemId, out var itemObjId))
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid portfolio item ID format.");

                var profile = await _db.TesterProfiles
                    .Find(p => p.UserId == actorObjId && p.ProfileStatus == profileStatus.active.ToString())
                    .FirstOrDefaultAsync(token);

                if (profile is null)
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Profile not found.");

                var item = profile.PortfolioItems.FirstOrDefault(i => i.ItemId == itemObjId);
                if (item is null)
                    _responseHelper.Fail<TesterProfileResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Portfolio item not found.");

                var update = Builders<TesterProfile>.Update.Combine(
                    Builders<TesterProfile>.Update.PullFilter(p => p.PortfolioItems, i => i.ItemId == itemObjId),
                    Builders<TesterProfile>.Update.Set(p => p.UpdatedAt, DateTime.UtcNow)
                );

                await _db.TesterProfiles.UpdateOneAsync(p => p.UserId == actorObjId, update, cancellationToken: token);

                var updated = await _db.TesterProfiles.Find(p => p.UserId == actorObjId).FirstOrDefaultAsync(token);
                return Ok(MapToProfileResponse(updated!));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error deleting portfolio item for user {UserId}.", actorUserId);
                return SystemError<TesterProfileResponse>();
            }
        }

        // ═══════════════════════════════════════════
        // DELETE PROFILE
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<object>> DeleteProfileAsync(
            string actorUserId,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    _responseHelper.Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                var result = await _db.TesterProfiles.FindAsync(p => p.UserId == actorObjId && p.ProfileStatus == profileStatus.active.ToString());

                if (result is null)
                    _responseHelper.Fail<object>(ResponseCodes.NoRecordReturned.ResponseCode, "Profile not found.");


                var update = Builders<TesterProfile>.Update.Combine(
                    Builders<TesterProfile>.Update.Set(p => p.ProfileStatus, profileStatus.inactive.ToString()),
                    Builders<TesterProfile>.Update.Set(p => p.UpdatedAt, DateTime.UtcNow)
                );

                await _db.TesterProfiles.UpdateOneAsync(p => p.UserId == actorObjId, update, cancellationToken: token);

                Log.Information("Profile deleted for user {UserId}.", actorUserId);
                return Ok<object>(null, "Your profile has been deleted.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error deleting profile for user {UserId}.", actorUserId);
                return SystemError<object>();
            }
        }

        // ═══════════════════════════════════════════
        // PRIVATE HELPERS
        // ═══════════════════════════════════════════

        private static List<string> SanitiseSkills(List<string>? skills) =>
            skills?.Where(s => !string.IsNullOrWhiteSpace(s))
                   .Select(s => s.Trim().ToLowerInvariant())
                   .Distinct()
                   .ToList() ?? new List<string>();

        private static ProfileSocialLinks MapSocialLinks(ProfileSocialLinksRequest? req) =>
            req is null ? new ProfileSocialLinks() : new ProfileSocialLinks
            {
                GitHub = req.GitHub?.Trim(),
                LinkedIn = req.LinkedIn?.Trim(),
                Portfolio = req.Portfolio?.Trim(),
                Twitter = req.Twitter?.Trim()
            };

        private static TesterProfileResponse MapToProfileResponse(TesterProfile p) => new()
        {
            Id = p.Id.ToString(),
            UserId = p.UserId.ToString(),
            FullName = p.FullName,
            Email = p.Email,
            Headline = p.Headline,
            Bio = p.Bio,
            YearsOfExperience = p.YearsOfExperience,
            Skills = p.Skills,
            AvailabilityStatus = p.AvailabilityStatus,
            EmploymentTypePreference = p.EmploymentTypePreference,
            WorkTypePreference = p.WorkTypePreference,
            RateOrSalaryExpectation = p.RateOrSalaryExpectation,
            PortfolioItems = p.PortfolioItems.Select(i => new PortfolioItemResponse
            {
                ItemId = i.ItemId.ToString(),
                ProjectName = i.ProjectName,
                Role = i.Role,
                Description = i.Description,
                Link = i.Link,
                Duration = i.Duration
            }).ToList(),
            SocialLinks = new ProfileSocialLinksResponse
            {
                GitHub = p.SocialLinks.GitHub,
                LinkedIn = p.SocialLinks.LinkedIn,
                Portfolio = p.SocialLinks.Portfolio,
                Twitter = p.SocialLinks.Twitter
            },
            IsPublic = p.IsPublic,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        };

        private static ProfileCardResponse MapToProfileCard(TesterProfile p) => new()
        {
            UserId = p.UserId.ToString(),
            FullName = p.FullName,
            Headline = p.Headline,
            YearsOfExperience = p.YearsOfExperience,
            Skills = p.Skills,
            AvailabilityStatus = p.AvailabilityStatus,
            EmploymentTypePreference = p.EmploymentTypePreference,
            WorkTypePreference = p.WorkTypePreference,
            RateOrSalaryExpectation = p.RateOrSalaryExpectation
        };

        private static ApiResponse<T> Ok<T>(T? data, string message = "Success") => new()
        {
            ResponseCode = ResponseCodes.Success.ResponseCode,
            ResponseMessage = message,
            Data = data
        };


        private static ApiResponse<T> SystemError<T>() => new()
        {
            ResponseCode = ResponseCodes.SystemMalfunction.ResponseCode,
            ResponseMessage = "An unexpected error occurred. Please try again later."
        };
    }
}
