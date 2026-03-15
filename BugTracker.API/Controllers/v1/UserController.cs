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
    public class UserController : Controller
    {
        private readonly ILogger<UserController> _logger;
        private readonly IUserService _userService;

        public UserController(ILogger<UserController> logger, IUserService userService)
        {
            _logger = logger;
            _userService = userService;
        }

        /// <summary>
        /// extract the authenticated user's ID from the JWT claims. Returns null if missing.
        /// </summary>
        /// <returns></returns>
        private string? GetCurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);


        [HttpGet("metrics")]
        public async Task<IActionResult> GetMetrics(
            [FromQuery] DashboardMetricsQuery query,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
                return Unauthorized(new ApiResponse<object>
                {
                    ResponseCode = ResponseCodes.UnAuthorized.ResponseCode,
                    ResponseMessage = "User identity could not be resolved."
                });

            var result = await _userService.GetDashboardMetricsAsync(userId, query, token);
            return Ok(result);
        }

    }
}
