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
    public class BugController : Controller
    {
        private readonly IBugService _bugService;

        public BugController(IBugService bugService)
        {
            _bugService = bugService;
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
        /// Create a bug. Any tester or the project owner in the project can do this.
        /// severities status: "critical", "high", "medium", "low"
        /// priority status: "urgent", "normal", "low"
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> CreateBug(
            [FromQuery] string projectId,
            [FromBody] CreateBugRequest request,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _bugService.CreateBugAsync(userId, projectId, request, token);
            return StatusCode(result.ResponseCode == ResponseCodes.Success.ResponseCode ? 201 : 400, result);
        }


        /// <summary>
        /// Get all bugs in a project. Any member can view this
        /// Supports filtering via query by loggedinuser, status, severity, priority, assigned-developer, assigned-tester.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="query"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpGet]
        public async Task<IActionResult> GetBugs(
            string projectId,
            [FromQuery] GetBugsQuery query,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _bugService.GetBugsAsync(userId, projectId, query, token);
            return Ok(result);
        }

        

        /// <summary>
        /// Get a single bug. Any project member can view.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="bugId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpGet("{bugId}")]
        public async Task<IActionResult> GetBugById(
            string projectId,
            string bugId,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _bugService.GetBugByIdAsync(userId, projectId, bugId, token);
            return Ok(result);
        }


        /// <summary>
        /// Update bug metadata (title, description, severity etc.) - Owner, assigned tester, or the original reporter can do this.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="bugId"></param>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpPut("{bugId}")]
        public async Task<IActionResult> UpdateBug(
            string projectId,
            string bugId,
            [FromBody] UpdateBugRequest request,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _bugService.UpdateBugAsync(userId, projectId, bugId, request, token);
            return Ok(result);
        }


        /// <summary>
        /// Update main bug status. Only the assigned tester, the original reporter, or the project owner
        /// bug status are: "none", "open", "inprogress", "resolved", "closed", "wontfix", "duplicate"
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="bugId"></param>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpPatch("{bugId}/status")]
        public async Task<IActionResult> UpdateBugStatus(
            string projectId,
            string bugId,
            [FromBody] UpdateBugStatusRequest request,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _bugService.UpdateBugStatusAsync(userId, projectId, bugId, request, token);
            return Ok(result);
        }


        /// <summary>
        /// Update developer status. only Assigned developer can do this.
        /// developers status are: "none", "notassigned", "notstarted", "ongoing", "blocked", "fixed", "notabug"
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="bugId"></param>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpPatch("{bugId}/developer-status")]
        public async Task<IActionResult> UpdateDeveloperStatus(
            string projectId,
            string bugId,
            [FromBody] UpdateDeveloperStatusRequest request,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _bugService.UpdateDeveloperStatusAsync(userId, projectId, bugId, request, token);
            return Ok(result);
        }


        /// <summary>
        /// Assign or unassign a developer. Owner or the assigned tester can do this.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="bugId"></param>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpPatch("{bugId}/assign-developer")]
        public async Task<IActionResult> AssignDeveloper(
            string projectId,
            string bugId,
            [FromBody] AssignDeveloperRequest request,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _bugService.AssignDeveloperAsync(userId, projectId, bugId, request, token);
            return Ok(result);
        }

        
        /// <summary>
        /// Reassign a bug to another tester. Owner or the current assigned tester can do this.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="bugId"></param>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpPatch("{bugId}/reassign-tester")]
        public async Task<IActionResult> ReassignTester(
            string projectId,
            string bugId,
            [FromBody] ReassignTesterRequest request,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _bugService.ReassignTesterAsync(userId, projectId, bugId, request, token);
            return Ok(result);
        }

        
        /// <summary>
        /// Create or overwrite the tester comment on a bug. Only the assigned tester (or original reporter) can do this.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="bugId"></param>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpPut("{bugId}/tester-comment")]
        public async Task<IActionResult> UpsertTesterComment(
            string projectId,
            string bugId,
            [FromBody] UpsertTesterCommentRequest request,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _bugService.UpsertTesterCommentAsync(userId, projectId, bugId, request, token);
            return Ok(result);
        }

        
        /// <summary>
        /// Create or overwrite the developer comment on a bug. Only the assigned developer can do this.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="bugId"></param>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpPut("{bugId}/developer-comment")]
        public async Task<IActionResult> UpsertDeveloperComment(
            string projectId,
            string bugId,
            [FromBody] UpsertDeveloperCommentRequest request,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _bugService.UpsertDeveloperCommentAsync(userId, projectId, bugId, request, token);
            return Ok(result);
        }

        
        /// <summary>
        /// Add an attachment to a bug. Any project member can add attachments.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="bugId"></param>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpPost("{bugId}/attachments")]
        public async Task<IActionResult> AddAttachment(
            string projectId,
            string bugId,
            [FromBody] AddAttachmentRequest request,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _bugService.AddAttachmentAsync(userId, projectId, bugId, request, token);
            return Ok(result);
        }

        
        /// <summary>
        /// Delete a bug. Owner or the original reporter only.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="bugId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpDelete("{bugId}")]
        public async Task<IActionResult> DeleteBug(
            string projectId,
            string bugId,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _bugService.DeleteBugAsync(userId, projectId, bugId, token);
            return Ok(result);
        }
    }
}
