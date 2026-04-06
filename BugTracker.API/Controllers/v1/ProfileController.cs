using Asp.Versioning;
using BugTracker.Data;
using BugTracker.Data.Models;
using BugTracker.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BugTracker.API.Controllers.v1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:ApiVersion}/[controller]")]
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly IProfileService _profileService;

        public ProfileController(IProfileService profileService)
        {
            _profileService = profileService;
        }

        private string? GetCurrentUserId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier);

        private IActionResult UnauthorizedResponse() =>
            Unauthorized(new ApiResponse<object>
            {
                ResponseCode = ResponseCodes.UnAuthorized.ResponseCode,
                ResponseMessage = "User identity could not be resolved."
            });



        /// <summary>
        /// Create or update the caller's profile. AvailabilityStatus: open | busy | notLooking
        /// EmploymentTypePreference: permanent | contract | both
        /// WorkTypePreference: remote | onsite | hybrid
        /// </summary>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpPut("")]
        public async Task<IActionResult> UpsertProfile(
            [FromBody] UpsertProfileRequest request,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _profileService.UpsertProfileAsync(userId, request, token);
            return Ok(result);
        }

        
        /// <summary>
        /// get the profile of the logged in user
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpGet("")]
        public async Task<IActionResult> GetMyProfile(CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _profileService.GetMyProfileAsync(userId, token);
            return Ok(result);
        }


        /// <summary>
        /// Delete the profile of the logged in user
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpDelete("")]
        public async Task<IActionResult> DeleteProfile(CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _profileService.DeleteProfileAsync(userId, token);
            return Ok(result);
        }


        /// <summary>
        /// To search or browse public profiles AvailabilityStatus: open | busy
        /// EmploymentType: permanent | contract | both
        /// workType: remote | onsite | hybrid
        /// </summary>
        /// <param name="query"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpGet("jobs")]
        public async Task<IActionResult> SearchProfiles(
            [FromQuery] SearchProfilesQuery query,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _profileService.SearchProfilesAsync(userId, query, token);
            return Ok(result);
        }


        /// <summary>
        /// View any public profile by userId
        /// </summary>
        /// <param name="targetUserId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpGet("{targetUserId}")]
        public async Task<IActionResult> GetProfileByUserId(
            string targetUserId,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _profileService.GetProfileByUserIdAsync(userId, targetUserId, token);
            return Ok(result);
        }


        /// <summary>
        /// To Add a portfolio item.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpPost("portfolio")]
        public async Task<IActionResult> AddPortfolioItem(
            [FromBody] AddPortfolioItemRequest request,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _profileService.AddPortfolioItemAsync(userId, request, token);
            return Ok(result);
        }


        /// <summary>
        /// To Update a specific portfolio item.
        /// </summary>
        /// <param name="itemId"></param>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpPatch("portfolio/{itemId}")]
        public async Task<IActionResult> UpdatePortfolioItem(
            string itemId,
            [FromBody] UpdatePortfolioItemRequest request,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _profileService.UpdatePortfolioItemAsync(userId, itemId, request, token);
            return Ok(result);
        }

        
        /// <summary>
        /// To Remove a portfolio item
        /// </summary>
        /// <param name="itemId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpDelete("portfolio/{itemId}")]
        public async Task<IActionResult> DeletePortfolioItem(
            string itemId,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _profileService.DeletePortfolioItemAsync(userId, itemId, token);
            return Ok(result);
        }

    }
}
