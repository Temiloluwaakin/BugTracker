using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BugTracker.Data.Models
{
    public class GenerateTestCaseReq
    {
        public string ProjectOverview { get; set; }
        public bool IsDocUpload { get; set; }
        public FileUploadRequest? FileUpload { get; set; }
    }

    public class FileUploadRequest
    {
        public string Base64File { get; set; }

        public string FileType { get; set; }
    }

    public class GeneratedTestCase
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string Preconditions { get; set; }
        public List<AIStep> Steps { get; set; }
        public string ExpectedResult { get; set; }
        public string Priority { get; set; }
        public List<string> Tags { get; set; }
    }

    public class AIStep
    {
        public string Action { get; set; }
        public string ExpectedOutcome { get; set; }
    }

    // CREATE TEST CASE
    public class CreateTestCaseRequest
    {
        [Required(ErrorMessage = "Title is required.")]
        [StringLength(200, MinimumLength = 5, ErrorMessage = "Title must be between 5 and 200 characters.")]
        public string Title { get; set; } = string.Empty;

        [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters.")]
        public string? Description { get; set; }

        [StringLength(500, ErrorMessage = "Preconditions cannot exceed 500 characters.")]
        public string? Preconditions { get; set; }

        /// <summary>Ordered list of steps. At least one step is required.</summary>
        [Required(ErrorMessage = "At least one step is required.")]
        [MinLength(1, ErrorMessage = "At least one step is required.")]
        public List<TestCaseStepRequest> Steps { get; set; } = new();

        [Required(ErrorMessage = "Expected result is required.")]
        [StringLength(1000, ErrorMessage = "Expected result cannot exceed 1000 characters.")]
        public string ExpectedResult { get; set; } = string.Empty;

        /// <summary>high | medium | low</summary>
        [Required(ErrorMessage = "Priority is required.")]
        public string Priority { get; set; } = string.Empty;

        /// <summary>Optional: tester to assign this case to for execution ownership.</summary>
        public string? AssignedToId { get; set; }

        public List<string>? Tags { get; set; }
    }

    // ─────────────────────────────────────────────
    // TEST CASE STEP (used in create + update)
    // ─────────────────────────────────────────────
    public class TestCaseStepRequest
    {
        [Required(ErrorMessage = "Step action is required.")]
        [StringLength(500, ErrorMessage = "Step action cannot exceed 500 characters.")]
        public string Action { get; set; } = string.Empty;

        [Required(ErrorMessage = "Expected outcome is required.")]
        [StringLength(500, ErrorMessage = "Expected outcome cannot exceed 500 characters.")]
        public string ExpectedOutcome { get; set; } = string.Empty;
    }

    // ─────────────────────────────────────────────
    // UPDATE TEST CASE (metadata only)
    // ─────────────────────────────────────────────
    public class UpdateTestCaseRequest
    {
        [StringLength(200, MinimumLength = 5)]
        public string? Title { get; set; }

        [StringLength(1000)]
        public string? Description { get; set; }

        [StringLength(500)]
        public string? Preconditions { get; set; }

        /// <summary>
        /// If provided, REPLACES the entire steps array.
        /// Step numbers are auto-assigned in order — do not send stepNumber in the request.
        /// </summary>
        public List<TestCaseStepRequest>? Steps { get; set; }

        [StringLength(1000)]
        public string? ExpectedResult { get; set; }

        /// <summary>high | medium | low</summary>
        public string? Priority { get; set; }

        public List<string>? Tags { get; set; }
    }

    // ─────────────────────────────────────────────
    // UPDATE TEST CASE STATUS
    // Only the creator or the owner can do this.
    // ─────────────────────────────────────────────
    public class UpdateTestCaseStatusRequest
    {
        /// <summary>draft | active | deprecated</summary>
        [Required(ErrorMessage = "Status is required.")]
        public string Status { get; set; } = string.Empty;
    }

    // ─────────────────────────────────────────────
    // ASSIGN TEST CASE
    // Assign or unassign a tester to a test case.
    // Owner or the creator can do this.
    // ─────────────────────────────────────────────
    public class AssignTestCaseRequest
    {
        /// <summary>Pass null to unassign.</summary>
        public string? AssignedToId { get; set; }
    }

    // ─────────────────────────────────────────────
    // LOG TEST RUN
    // ─────────────────────────────────────────────
    public class LogTestRunRequest
    {
        /// <summary>passed | failed | blocked | skipped</summary>
        [Required(ErrorMessage = "Result is required.")]
        public string Result { get; set; } = string.Empty;

        [StringLength(200)]
        public string? Environment { get; set; }

        [StringLength(50)]
        public string? AppVersion { get; set; }

        [StringLength(2000)]
        public string? Notes { get; set; }

        /// <summary>
        /// Optional per-step results. If provided, every step number must match
        /// a step in the test case. Partial results are allowed — you don't have
        /// to submit results for every step.
        /// </summary>
        public List<TestRunStepResultRequest>? StepResults { get; set; }

        /// <summary>
        /// Only used when result is 'failed'. Two mutually exclusive options:
        ///
        /// Option A — Link an existing bug:
        ///   Set LinkedBugId to an existing bug ID in this project.
        ///   Leave AutoCreateBug as false (or omit it).
        ///
        /// Option B — Auto-create a new bug from this failed run:
        ///   Set AutoCreateBug = true.
        ///   Fill in AutoCreateBugTitle (required) and AutoCreateBugSeverity/Priority (optional, default to 'medium'/'normal').
        ///   Leave LinkedBugId null.
        ///
        /// You cannot set both LinkedBugId and AutoCreateBug = true at the same time.
        /// </summary>
        public string? LinkedBugId { get; set; }

        public bool AutoCreateBug { get; set; } = false;

        [StringLength(200, MinimumLength = 5, ErrorMessage = "Bug title must be between 5 and 200 characters.")]
        public string? AutoCreateBugTitle { get; set; }

        /// <summary>critical | high | medium | low — defaults to 'medium' if omitted.</summary>
        public string? AutoCreateBugSeverity { get; set; }

        /// <summary>urgent | normal | low — defaults to 'normal' if omitted.</summary>
        public string? AutoCreateBugPriority { get; set; }

        /// <summary>Execution time in seconds. Optional.</summary>
        public int? Duration { get; set; }
    }

    // ─────────────────────────────────────────────
    // TEST RUN STEP RESULT (used inside LogTestRunRequest)
    // ─────────────────────────────────────────────
    public class TestRunStepResultRequest
    {
        [Required]
        public int StepNumber { get; set; }

        /// <summary>passed | failed | blocked | skipped</summary>
        [Required(ErrorMessage = "Step result is required.")]
        public string Result { get; set; } = string.Empty;

        [StringLength(500)]
        public string? ActualOutcome { get; set; }
    }

    // ─────────────────────────────────────────────
    // LINK BUG TO TEST RUN (after the fact)
    // ─────────────────────────────────────────────
    public class LinkBugToRunRequest
    {
        [Required(ErrorMessage = "Bug ID is required.")]
        public string BugId { get; set; } = string.Empty;
    }

    // ─────────────────────────────────────────────
    // GET TEST CASES QUERY
    // ─────────────────────────────────────────────
    public class GetTestCasesQuery
    {
        public string? Status { get; set; }
        public string? Priority { get; set; }
        public string? AssignedToId { get; set; }
        public string? Tag { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    // ─────────────────────────────────────────────
    // GET TEST RUNS QUERY
    // ─────────────────────────────────────────────
    public class GetTestRunsQuery
    {
        public string? Result { get; set; }
        public string? ExecutedById { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }


    // ═════════════════════════════════════════════
    // RESPONSE DTOs
    // ═════════════════════════════════════════════
    public class TestCaseResponse
    {
        public string Id { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public int CaseNumber { get; set; }
        public string CaseLabel => $"TC-{CaseNumber:D3}";
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Preconditions { get; set; }
        public List<TestCaseStepResponse> Steps { get; set; } = new();
        public string ExpectedResult { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public TestCasePersonRef CreatedBy { get; set; } = new();
        public TestCasePersonRef? AssignedTo { get; set; }
        public List<string> Tags { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class TestCaseSummaryResponse
    {
        public string Id { get; set; } = string.Empty;
        public int CaseNumber { get; set; }
        public string CaseLabel => $"TC-{CaseNumber:D3}";
        public string Title { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int StepCount { get; set; }
        public TestCasePersonRef? AssignedTo { get; set; }
        public List<string> Tags { get; set; } = new();
        public DateTime CreatedAt { get; set; }
    }

    public class TestCaseStepResponse
    {
        public int StepNumber { get; set; }
        public string Action { get; set; } = string.Empty;
        public string ExpectedOutcome { get; set; } = string.Empty;
    }

    public class TestCasePersonRef
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class PagedTestCasesResponse
    {
        public List<TestCaseSummaryResponse> TestCases { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    }

    public class TestRunResponse
    {
        public string Id { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string TestCaseId { get; set; } = string.Empty;
        public string TestCaseLabel => $"TC-{TestCaseCaseNumber:D3}";
        public int TestCaseCaseNumber { get; set; }
        public string TestCaseTitle { get; set; } = string.Empty;
        public TestCasePersonRef ExecutedBy { get; set; } = new();
        public string Result { get; set; } = string.Empty;
        public string? Environment { get; set; }
        public string? AppVersion { get; set; }
        public string? Notes { get; set; }
        public List<TestRunStepResultResponse> StepResults { get; set; } = new();
        public string? LinkedBugId { get; set; }
        public string? LinkedBugLabel { get; set; }
        public int? Duration { get; set; }
        public DateTime ExecutedAt { get; set; }
    }

    public class TestRunStepResultResponse
    {
        public int StepNumber { get; set; }
        public string Result { get; set; } = string.Empty;
        public string? ActualOutcome { get; set; }
    }

    public class PagedTestRunsResponse
    {
        public List<TestRunResponse> Runs { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    }
}
