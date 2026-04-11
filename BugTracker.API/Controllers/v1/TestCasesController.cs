using Asp.Versioning;
using BugTracker.Data;
using BugTracker.Data.Entities;
using BugTracker.Data.Models;
using BugTracker.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
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


        /// <summary>
        /// To generate test case by uploading a base64 document and carrying it out
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        [HttpPost("generate")]
        public async Task<IActionResult> Generate(
            string projectId,
            [FromBody] GenerateTestCaseReq request, 
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _testCaseService.GenerateTestCase(userId, projectId, request, token);
            return StatusCode(result.ResponseCode == ResponseCodes.Success.ResponseCode ? 201 : 400, result);
        }


        
        /// <summary>
        /// To create a Test case. only a tester or owner can do that
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
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

        

        /// <summary>
        /// to get all test casese in a project, any member can view it
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="query"></param>
        /// <param name="token"></param>
        /// <returns></returns>
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

        
        
        /// <summary>
        /// to get a single test case. any member can view
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="testCaseId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
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

        
        
        /// <summary>
        /// to update a test case metadata. only the creator or owner.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="testCaseId"></param>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
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

        
        
        /// <summary>
        /// to update a test case status. creater or owner only
        /// status: draft, active, deprecated
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="testCaseId"></param>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
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

        
        
        /// <summary>
        /// to assign or unassign a tester to a test case. owner or creator
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="testCaseId"></param>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
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

        
        /// <summary>
        /// delete a test case. cant delete if it has existing test runs
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="testCaseId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
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



        /// <summary>
        /// Log a new test run. Any tester or owner can execute. Test case must be 'active' to be executed.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="testCaseId"></param>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
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


        /// <summary>
        /// Get all runs for a test case. Any member can view.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="testCaseId"></param>
        /// <param name="query"></param>
        /// <param name="token"></param>
        /// <returns></returns>
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



        /// <summary>
        ///  Get a single test run. Any project member can view.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="testCaseId"></param>
        /// <param name="testRunId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
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


        /// <summary>
        /// Link an existing bug to a failed test run after the fact.
        /// The person who logged the run or the owner can do this
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="testCaseId"></param>
        /// <param name="testRunId"></param>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
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


        /// <summary>
        /// Delete a test run. The person who logged it or the owner can delete.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="testCaseId"></param>
        /// <param name="testRunId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
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
