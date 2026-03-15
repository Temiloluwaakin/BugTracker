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
    public class ProjectController : Controller
    {
        private readonly ILogger<ProjectController> _logger;
        private readonly IProjectService _projectService;

        public ProjectController (ILogger<ProjectController> logger, IProjectService projectService)
        {
            _logger = logger;
            _projectService = projectService;
        }

        /// <summary>
        /// extract the authenticated user's ID from the JWT claims. Returns null if missing.
        /// </summary>
        /// <returns></returns>
        private string? GetCurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);



        /// <summary>
        /// To create a project. the person who creates is automatically given the owner role
        /// </summary>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> CreateProject([FromBody] CreateProjectRequest request, CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
                return Unauthorized(new ApiResponse<object>
                {
                    ResponseCode = ResponseCodes.UnAuthorized.ResponseCode,
                    ResponseMessage = "User identity could not be resolved."
                });

            var result = await _projectService.CreateProjectAsync(userId, request, token);
            return StatusCode(result.ResponseCode == ResponseCodes.Success.ResponseCode ? 200 : 400, result);
        }


        /// <summary>
        /// To get all the projecs the logged in user is a member of
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpGet]
        public async Task<IActionResult> GetMyProjects(CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
                return Unauthorized(new ApiResponse<object>
                {
                    ResponseCode = ResponseCodes.UnAuthorized.ResponseCode,
                    ResponseMessage = "User identity could not be resolved."
                });

            var result = await _projectService.GetMyProjectsAsync(userId, token);
            return Ok(result);
        }


        /// <summary>
        /// Get a single project. the logged in user must be a member of the project
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpGet("{projectId}")]
        public async Task<IActionResult> GetProjectById(
            string projectId,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
                return Unauthorized(new ApiResponse<object>
                {
                    ResponseCode = ResponseCodes.UnAuthorized.ResponseCode,
                    ResponseMessage = "User identity could not be resolved."
                });

            var result = await _projectService.GetProjectByIdAsync(userId, projectId, token);
            return Ok(result);
        }


        /// <summary>
        /// Update project metadata (name,description,status) of a project by the owner
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpPut("{projectId}")]
        public async Task<IActionResult> UpdateProject(
            string projectId,
            [FromBody] UpdateProjectRequest request,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
                return Unauthorized(new ApiResponse<object>
                {
                    ResponseCode = ResponseCodes.UnAuthorized.ResponseCode,
                    ResponseMessage = "User identity could not be resolved."
                });

            var result = await _projectService.UpdateProjectAsync(userId, projectId, request, token);
            return Ok(result);
        }


        /// <summary>
        /// Soft-delete (archive) a project by the owner of the project
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpDelete("{projectId}")]
        public async Task<IActionResult> DeleteProject(
            string projectId,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
                return Unauthorized(new ApiResponse<object>
                {
                    ResponseCode = ResponseCodes.UnAuthorized.ResponseCode,
                    ResponseMessage = "User identity could not be resolved."
                });

            var result = await _projectService.DeleteProjectAsync(userId, projectId, token);
            return Ok(result);
        }


        /// <summary>
        /// Invite a user by email by the Owner only. if the person is not a user it stores the invitation for 7 days so as
        /// when the user creates account he automatically have access to the project
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpPost("{projectId}/invite")]
        public async Task<IActionResult> InviteMember(
            string projectId,
            [FromBody] InviteMemberRequest request,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
                return Unauthorized(new ApiResponse<object>
                {
                    ResponseCode = ResponseCodes.UnAuthorized.ResponseCode,
                    ResponseMessage = "User identity could not be resolved."
                });

            var result = await _projectService.InviteMemberAsync(userId, projectId, request, token);
            return Ok(result);
        }


        /// <summary>
        /// Change a member's role by the Owner only.
        /// Owner cannot downgrade themselves.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="memberId"></param>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpPut("{projectId}/members/{memberId}/role")]
        public async Task<IActionResult> UpdateMemberRole(
            string projectId,
            string memberId,
            [FromBody] UpdateMemberRoleRequest request,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
                return Unauthorized(new ApiResponse<object>
                {
                    ResponseCode = ResponseCodes.UnAuthorized.ResponseCode,
                    ResponseMessage = "User identity could not be resolved."
                });

            var result = await _projectService.UpdateMemberRoleAsync(userId, projectId, memberId, request, token);
            return Ok(result);
        }


        /// <summary>
        /// Remove a member by the Owner only.
        /// Owner cannot remove themselves.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="memberId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpDelete("{projectId}/members/{memberId}")]
        public async Task<IActionResult> RemoveMember(
            string projectId,
            string memberId,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
                return Unauthorized(new ApiResponse<object>
                {
                    ResponseCode = ResponseCodes.UnAuthorized.ResponseCode,
                    ResponseMessage = "User identity could not be resolved."
                });

            var result = await _projectService.RemoveMemberAsync(userId, projectId, memberId, token);
            return Ok(result);
        }


        /// <summary>
        /// List all members of a project.
        /// Any project member can call this.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpGet("{projectId}/members")]
        public async Task<IActionResult> GetMembers(
            string projectId,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
                return Unauthorized(new ApiResponse<object>
                {
                    ResponseCode = ResponseCodes.UnAuthorized.ResponseCode,
                    ResponseMessage = "User identity could not be resolved."
                });

            var result = await _projectService.GetMembersAsync(userId, projectId, token);
            return Ok(result);
        }


        /// <summary>
        /// List all members of a project.
        /// Any project member can call this.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpGet("{projectId}/activities")]
        public async Task<IActionResult> GetActivities(
            string projectId,
            int page,
            int pageSize,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
                return Unauthorized(new ApiResponse<object>
                {
                    ResponseCode = ResponseCodes.UnAuthorized.ResponseCode,
                    ResponseMessage = "User identity could not be resolved."
                });

            var result = await _projectService.GetActivitiesAsync(userId, projectId, page, pageSize, token);
            return Ok(result);
        }



        [HttpGet("{projectId}/metrics")]
        public async Task<IActionResult> GetMetrics(
            string projectId,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
                return Unauthorized(new ApiResponse<object>
                {
                    ResponseCode = ResponseCodes.UnAuthorized.ResponseCode,
                    ResponseMessage = "User identity could not be resolved."
                });

            var result = await _projectService.GetProjectMetricsAsync(userId, projectId, token);
            return Ok(result);
        }

    }
}
