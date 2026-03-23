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
    [Route("api/v{version:ApiVersion}/{projectId}/[controller]")]
    [Authorize]
    public class TestCasesController : Controller
    {
        private readonly ITestCaseService _testCaseService;

        public TestCasesController(ITestCaseService testCaseService)
        {
            _testCaseService = testCaseService;
        }

        private string? GetCurrentUserId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier);

        private IActionResult UnauthorizedResponse() =>
            Unauthorized(new ApiResponse<object>
            {
                ResponseCode = ResponseCodes.UnAuthorized.ResponseCode,
                ResponseMessage = "User identity could not be resolved."
            });

        // ─────────────────────────────────────────────
        // POST /api/projects/{projectId}/testcases
        // Create a test case. Testers and owners only.
        // ─────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> CreateTestCase(
            string projectId,
            [FromBody] CreateTestCaseRequest request,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _testCaseService.CreateTestCaseAsync(userId, projectId, request, token);
            return StatusCode(result.ResponseCode == ResponseCodes.Success.ResponseCode ? 201 : 400, result);
        }

        // ─────────────────────────────────────────────
        // GET /api/projects/{projectId}/testcases
        // Get all test cases in a project. Any member can view.
        // ─────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetTestCases(
            string projectId,
            [FromQuery] GetTestCasesQuery query,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _testCaseService.GetTestCasesAsync(userId, projectId, query, token);
            return Ok(result);
        }

        // ─────────────────────────────────────────────
        // GET /api/projects/{projectId}/testcases/{testCaseId}
        // Get a single test case. Any project member can view.
        // ─────────────────────────────────────────────
        [HttpGet("{testCaseId}")]
        public async Task<IActionResult> GetTestCaseById(
            string projectId,
            string testCaseId,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _testCaseService.GetTestCaseByIdAsync(userId, projectId, testCaseId, token);
            return Ok(result);
        }

        // ─────────────────────────────────────────────
        // PUT /api/projects/{projectId}/testcases/{testCaseId}
        // Update test case metadata. Creator or owner only.
        // Cannot edit a deprecated test case.
        // ─────────────────────────────────────────────
        [HttpPut("{testCaseId}")]
        public async Task<IActionResult> UpdateTestCase(
            string projectId,
            string testCaseId,
            [FromBody] UpdateTestCaseRequest request,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _testCaseService.UpdateTestCaseAsync(userId, projectId, testCaseId, request, token);
            return Ok(result);
        }

        // ─────────────────────────────────────────────
        // PATCH /api/projects/{projectId}/testcases/{testCaseId}/status
        // Update test case status. Creator or owner only.
        // draft → active → deprecated
        // ─────────────────────────────────────────────
        [HttpPatch("{testCaseId}/status")]
        public async Task<IActionResult> UpdateTestCaseStatus(
            string projectId,
            string testCaseId,
            [FromBody] UpdateTestCaseStatusRequest request,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _testCaseService.UpdateTestCaseStatusAsync(userId, projectId, testCaseId, request, token);
            return Ok(result);
        }

        // ─────────────────────────────────────────────
        // PATCH /api/projects/{projectId}/testcases/{testCaseId}/assign
        // Assign or unassign a tester. Owner or creator only.
        // ─────────────────────────────────────────────
        [HttpPatch("{testCaseId}/assign")]
        public async Task<IActionResult> AssignTestCase(
            string projectId,
            string testCaseId,
            [FromBody] AssignTestCaseRequest request,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _testCaseService.AssignTestCaseAsync(userId, projectId, testCaseId, request, token);
            return Ok(result);
        }

        // ─────────────────────────────────────────────
        // DELETE /api/projects/{projectId}/testcases/{testCaseId}
        // Delete a test case. Owner or creator only.
        // Cannot delete if it has existing test runs.
        // ─────────────────────────────────────────────
        [HttpDelete("{testCaseId}")]
        public async Task<IActionResult> DeleteTestCase(
            string projectId,
            string testCaseId,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _testCaseService.DeleteTestCaseAsync(userId, projectId, testCaseId, token);
            return Ok(result);
        }

        // ═════════════════════════════════════════════
        // TEST RUN ENDPOINTS
        // Nested under testcases since a run always
        // belongs to a specific test case.
        // ═════════════════════════════════════════════

        // ─────────────────────────────────────────────
        // POST /api/projects/{projectId}/testcases/{testCaseId}/runs
        // Log a new test run. Any tester or owner can execute.
        // Test case must be 'active' to be executed.
        // ─────────────────────────────────────────────
        [HttpPost("{testCaseId}/runs")]
        public async Task<IActionResult> LogTestRun(
            string projectId,
            string testCaseId,
            [FromBody] LogTestRunRequest request,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _testCaseService.LogTestRunAsync(userId, projectId, testCaseId, request, token);
            return StatusCode(result.ResponseCode == ResponseCodes.Success.ResponseCode ? 201 : 400, result);
        }

        // ─────────────────────────────────────────────
        // GET /api/projects/{projectId}/testcases/{testCaseId}/runs
        // Get all runs for a test case. Any member can view.
        // ─────────────────────────────────────────────
        [HttpGet("{testCaseId}/runs")]
        public async Task<IActionResult> GetTestRuns(
            string projectId,
            string testCaseId,
            [FromQuery] GetTestRunsQuery query,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _testCaseService.GetTestRunsAsync(userId, projectId, testCaseId, query, token);
            return Ok(result);
        }

        // ─────────────────────────────────────────────
        // GET /api/projects/{projectId}/testcases/{testCaseId}/runs/{testRunId}
        // Get a single test run. Any project member can view.
        // ─────────────────────────────────────────────
        [HttpGet("{testCaseId}/runs/{testRunId}")]
        public async Task<IActionResult> GetTestRunById(
            string projectId,
            string testCaseId,
            string testRunId,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _testCaseService.GetTestRunByIdAsync(userId, projectId, testCaseId, testRunId, token);
            return Ok(result);
        }

        // ─────────────────────────────────────────────
        // PATCH /api/projects/{projectId}/testcases/{testCaseId}/runs/{testRunId}/link-bug
        // Link an existing bug to a failed test run after the fact.
        // The person who logged the run or the owner can do this.
        // ─────────────────────────────────────────────
        [HttpPatch("{testCaseId}/runs/{testRunId}/link-bug")]
        public async Task<IActionResult> LinkBugToRun(
            string projectId,
            string testCaseId,
            string testRunId,
            [FromBody] LinkBugToRunRequest request,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _testCaseService.LinkBugToRunAsync(userId, projectId, testCaseId, testRunId, request, token);
            return Ok(result);
        }

        // ─────────────────────────────────────────────
        // DELETE /api/projects/{projectId}/testcases/{testCaseId}/runs/{testRunId}
        // Delete a test run. The person who logged it or the owner can delete.
        // ─────────────────────────────────────────────
        [HttpDelete("{testCaseId}/runs/{testRunId}")]
        public async Task<IActionResult> DeleteTestRun(
            string projectId,
            string testCaseId,
            string testRunId,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _testCaseService.DeleteTestRunAsync(userId, projectId, testCaseId, testRunId, token);
            return Ok(result);
        }
    }
}
