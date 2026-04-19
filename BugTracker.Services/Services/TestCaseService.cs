using BugTracker.Data;
using BugTracker.Data.Context;
using BugTracker.Data.Entities;
using BugTracker.Data.Models;
using BugTracker.Services.Helpers;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using Serilog;
using System.Text;
using Newtonsoft.Json;

namespace BugTracker.Services.Services
{
    public interface ITestCaseService
    {
        // ── Test Cases ──
        Task<ApiResponse<List<TestCaseResponse>>> CreateTestCasesAsync(string actorUserId, string projectId, List<CreateTestCaseRequest> request, CancellationToken token);
        Task<ApiResponse<object>> GenerateTestCase(string actorUserId, string projectId, GenerateTestCaseReq request, CancellationToken token);
        Task<ApiResponse<PagedTestCasesResponse>> GetTestCasesAsync(string actorUserId, string projectId, GetTestCasesQuery query, CancellationToken token);
        Task<ApiResponse<TestCaseResponse>> GetTestCaseByIdAsync(string actorUserId, string projectId, string testCaseId, CancellationToken token);
        Task<ApiResponse<TestCaseResponse>> UpdateTestCaseAsync(string actorUserId, string projectId, string testCaseId, UpdateTestCaseRequest request, CancellationToken token);
        Task<ApiResponse<TestCaseResponse>> UpdateTestCaseStatusAsync(string actorUserId, string projectId, string testCaseId, UpdateTestCaseStatusRequest request, CancellationToken token);
        Task<ApiResponse<TestCaseResponse>> AssignTestCaseAsync(string actorUserId, string projectId, string testCaseId, AssignTestCaseRequest request, CancellationToken token);
        Task<ApiResponse<object>> DeleteTestCaseAsync(string actorUserId, string projectId, string testCaseId, CancellationToken token);

        // ── Test Runs ──
        Task<ApiResponse<TestRunResponse>> LogTestRunAsync(string actorUserId, string projectId, string testCaseId, LogTestRunRequest request, CancellationToken token);
        Task<ApiResponse<PagedTestRunsResponse>> GetTestRunsAsync(string actorUserId, string projectId, string testCaseId, GetTestRunsQuery query, CancellationToken token);
        Task<ApiResponse<TestRunResponse>> GetTestRunByIdAsync(string actorUserId, string projectId, string testCaseId, string testRunId, CancellationToken token);
        Task<ApiResponse<TestRunResponse>> LinkBugToRunAsync(string actorUserId, string projectId, string testCaseId, string testRunId, LinkBugToRunRequest request, CancellationToken token);
        Task<ApiResponse<object>> DeleteTestRunAsync(string actorUserId, string projectId, string testCaseId, string testRunId, CancellationToken token);
    }

    public class TestCaseService : ITestCaseService
    {
        private readonly DatabaseContext _db;
        private readonly IResponseHelper _responseHelper;
        private readonly IAIHelper _aiService;

        private static readonly HashSet<string> ValidPriorities = new() { "high", "medium", "low" };
        private static readonly HashSet<string> ValidStatuses = new() { "draft", "active", "deprecated" };
        private static readonly HashSet<string> ValidRunResults = new() { "passed", "failed", "blocked", "skipped" };
        private static readonly HashSet<string> ValidStepResults = new() { "passed", "failed", "blocked", "skipped" };

        public TestCaseService(
            DatabaseContext db,
            IResponseHelper responseHelper,
            IAIHelper aiService
            )
        {
            _db = db;
            _responseHelper = responseHelper;
            _aiService = aiService;
        }



        public async Task<ApiResponse<object>> GenerateTestCase(
            string actorUserId,
            string projectId,
            GenerateTestCaseReq request,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return _responseHelper.Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                if (!ObjectId.TryParse(projectId, out var projectObjId))
                    return _responseHelper.Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid project ID format.");

                if (request == null)
                {
                    return _responseHelper.Fail<object>(ResponseCodes.EmptyEntryDetected.ResponseCode, "Request is Empty.");
                }

                string text = request.ProjectOverview;
                if (request.IsDocUpload)
                {
                    var bytes = Convert.FromBase64String(request.FileUpload.Base64File);

                    text = request.FileUpload.FileType switch
                    {
                        "pdf" => ExtractPdfText(bytes),
                        "docx" => ExtractDocxText(bytes),
                        "xlsx" => ExtractExcelText(bytes),
                        _ => throw new Exception("Unsupported file type")
                    };
                }

                var aiResult = await _aiService.GenerateGeminiTestCases(text);
                if (aiResult == null)
                {
                    Log.Warning("Failed to generate testcases");
                    return _responseHelper.Fail<object>(ResponseCodes.Failed.ResponseCode, "Failed to generate TestCase");
                }

                return _responseHelper.Ok(aiResult);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error generating test case fpr project {ProjectId} by user {UserId}.", projectId, actorUserId);
                return _responseHelper.SystemError<object>();
            }
        }




        // ═══════════════════════════════════════════
        // CREATE TEST CASE
        // Testers and owners only.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<List<TestCaseResponse>>> CreateTestCasesAsync(
            string actorUserId,
            string projectId,
            List<CreateTestCaseRequest> requests,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return _responseHelper.Fail<List<TestCaseResponse>>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                if (!ObjectId.TryParse(projectId, out var projectObjId))
                    return _responseHelper.Fail<List<TestCaseResponse>>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid project ID format.");

                if (requests is null || requests.Count == 0)
                    return _responseHelper.Fail<List<TestCaseResponse>>(ResponseCodes.InvalidEntryDetected.ResponseCode, "At least one test case is required.");

                // 1. Fetch project and do common validations
                var project = await _db.Projects.Find(p => p.Id == projectId).FirstOrDefaultAsync(token);
                if (project is null)
                    return _responseHelper.Fail<List<TestCaseResponse>>(ResponseCodes.NoRecordReturned.ResponseCode, "Project not found.");

                if (project.Status == ProjectStatus.Archived)
                    return _responseHelper.Fail<List<TestCaseResponse>>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        "Cannot create test cases in an archived project.");

                // 2. Verify caller is tester or owner
                var callerMember = project.Members.FirstOrDefault(m => m.UserId == actorUserId);
                if (callerMember is null)
                    return _responseHelper.Fail<List<TestCaseResponse>>(ResponseCodes.UnAuthorized.ResponseCode, "You are not a member of this project.");

                if (callerMember.Role is not (ProjectRole.Tester or ProjectRole.Owner))
                    return _responseHelper.Fail<List<TestCaseResponse>>(ResponseCodes.UnAuthorized.ResponseCode,
                        "Only testers and owners can create test cases.");

                // 3. Prepare result list and validate all requests first (fail fast on any invalid request)
                var testCasesToInsert = new List<TestCase>();
                var responses = new List<TestCaseResponse>();
                var now = DateTime.UtcNow;

                for (int reqIndex = 0; reqIndex < requests.Count; reqIndex++)
                {
                    var request = requests[reqIndex];

                    // Validate each request individually
                    var validationError = ValidateCreateTestCaseRequest(request, reqIndex + 1);
                    if (validationError != null)
                        return _responseHelper.Fail<List<TestCaseResponse>>(ResponseCodes.InvalidEntryDetected.ResponseCode, validationError);

                    // Resolve optional assignee
                    ProjectMember? assignedMember = null;
                    ObjectId? assignedObjId = null;

                    if (!string.IsNullOrWhiteSpace(request.AssignedToId))
                    {
                        if (!ObjectId.TryParse(request.AssignedToId, out var assignedId))
                            return _responseHelper.Fail<List<TestCaseResponse>>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                                $"Test case {reqIndex + 1}: Invalid AssignedToId format.");

                        assignedMember = project.Members.FirstOrDefault(m => m.UserId == request.AssignedToId);
                        if (assignedMember is null)
                            return _responseHelper.Fail<List<TestCaseResponse>>(ResponseCodes.NoRecordReturned.ResponseCode,
                                $"Test case {reqIndex + 1}: The specified assignee is not a member of this project.");

                        if (assignedMember.Role is not (ProjectRole.Tester or ProjectRole.Owner))
                            return _responseHelper.Fail<List<TestCaseResponse>>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                                $"Test case {reqIndex + 1}: Test cases can only be assigned to testers or owners.");

                        assignedObjId = assignedId;
                    }

                    // Get next atomic case number
                    var caseNumber = await GetNextCaseNumberAsync(projectObjId, token);

                    // Build steps (auto-assign step numbers)
                    var steps = request.Steps.Select((s, index) => new TestCaseStep
                    {
                        StepNumber = index + 1,
                        Action = s.Action.Trim(),
                        ExpectedOutcome = s.ExpectedOutcome.Trim()
                    }).ToList();

                    var testCase = new TestCase
                    {
                        ProjectId = projectObjId,
                        CaseNumber = caseNumber,
                        Title = request.Title.Trim(),
                        Description = request.Description?.Trim(),
                        Preconditions = request.Preconditions?.Trim(),
                        Steps = steps,
                        ExpectedResult = request.ExpectedResult.Trim(),
                        Priority = request.Priority.Trim().ToLowerInvariant(),
                        Status = "draft",
                        CreatedById = actorObjId,
                        CreatedByName = callerMember.FullName,
                        CreatedByEmail = callerMember.Email,
                        AssignedToId = assignedObjId,
                        AssignedToName = assignedMember?.FullName,
                        AssignedToEmail = assignedMember?.Email,
                        Tags = SanitiseTags(request.Tags),
                        CreatedAt = now,
                        UpdatedAt = now
                    };

                    testCasesToInsert.Add(testCase);
                }

                // 4. Bulk insert all test cases
                if (testCasesToInsert.Count > 0)
                {
                    await _db.TestCases.InsertManyAsync(testCasesToInsert, cancellationToken: token);
                }

                // 5. Map to responses and log activities
                foreach (var testCase in testCasesToInsert)
                {
                    var response = MapToTestCaseResponse(testCase);
                    responses.Add(response);

                    await LogActivityAsync(
                        projectId: projectObjId,
                        actorId: actorObjId,
                        actorName: callerMember.FullName,
                        action: ActivityAction.TestCaseCreated,
                        entityType: ActivityEntityType.TestCase,
                        entityId: ObjectId.Parse(testCase.Id),
                        entityTitle: $"TC-{testCase.CaseNumber:D3}: {testCase.Title}",
                        token: token);

                    Log.Information("Test case {CaseLabel} created in project {ProjectId} by user {UserId}.",
                        $"TC-{testCase.CaseNumber:D3}", projectId, actorUserId);
                }

                return _responseHelper.Ok(responses);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error creating {Count} test cases in project {ProjectId} by user {UserId}.",
                    requests?.Count ?? 0, projectId, actorUserId);
                return _responseHelper.SystemError<List<TestCaseResponse>>();
            }
        }

        // ═══════════════════════════════════════════
        // GET TEST CASES (paged + filtered)
        // Any project member can view.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<PagedTestCasesResponse>> GetTestCasesAsync(
            string actorUserId,
            string projectId,
            GetTestCasesQuery query,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return _responseHelper.Fail<PagedTestCasesResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                if (!ObjectId.TryParse(projectId, out var projectObjId))
                    return _responseHelper.Fail<PagedTestCasesResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid project ID format.");

                var project = await _db.Projects.Find(p => p.Id == projectId).FirstOrDefaultAsync(token);
                if (project is null)
                    return _responseHelper.Fail<PagedTestCasesResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Project not found.");

                if (!project.Members.Any(m => m.UserId == actorUserId))
                    return _responseHelper.Fail<PagedTestCasesResponse>(ResponseCodes.UnAuthorized.ResponseCode, "You are not a member of this project.");

                // Build filter
                var fb = Builders<TestCase>.Filter;
                var filters = new List<FilterDefinition<TestCase>>
            {
                fb.Eq(tc => tc.ProjectId, projectObjId)
            };

                if (!string.IsNullOrWhiteSpace(query.Status))
                    filters.Add(fb.Eq(tc => tc.Status, query.Status.ToLowerInvariant()));

                if (!string.IsNullOrWhiteSpace(query.Priority))
                    filters.Add(fb.Eq(tc => tc.Priority, query.Priority.ToLowerInvariant()));

                if (!string.IsNullOrWhiteSpace(query.AssignedToId) &&
                    ObjectId.TryParse(query.AssignedToId, out var assigneeFilter))
                    filters.Add(fb.Eq(tc => tc.AssignedToId, assigneeFilter));

                if (!string.IsNullOrWhiteSpace(query.Tag))
                    filters.Add(fb.AnyEq(tc => tc.Tags, query.Tag.ToLowerInvariant()));

                var combined = fb.And(filters);

                var pageSize = Math.Clamp(query.PageSize, 1, 100);
                var page = Math.Max(query.Page, 1);
                var skip = (page - 1) * pageSize;
                var totalCount = (int)await _db.TestCases.CountDocumentsAsync(combined, cancellationToken: token);

                var testCases = await _db.TestCases
                    .Find(combined)
                    .SortByDescending(tc => tc.CreatedAt)
                    .Skip(skip)
                    .Limit(pageSize)
                    .ToListAsync(token);

                return _responseHelper.Ok(new PagedTestCasesResponse
                {
                    TestCases = testCases.Select(MapToTestCaseSummary).ToList(),
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching test cases for project {ProjectId}.", projectId);
                return _responseHelper.SystemError<PagedTestCasesResponse>();
            }
        }

        // ═══════════════════════════════════════════
        // GET TEST CASE BY ID
        // Any project member can view.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<TestCaseResponse>> GetTestCaseByIdAsync(
            string actorUserId,
            string projectId,
            string testCaseId,
            CancellationToken token)
        {
            try
            {
                var (valid, actorObjId, projectObjId, tcObjId, error) = ParseIds(actorUserId, projectId, testCaseId);
                if (!valid) return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, error!);

                var (project, testCase, memberError) = await FetchProjectAndTestCaseAsync(projectObjId, tcObjId, actorObjId, token);
                if (memberError is not null) return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.UnAuthorized.ResponseCode, memberError);
                if (project is null || testCase is null) return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Test case or project not found.");

                return _responseHelper.Ok(MapToTestCaseResponse(testCase));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching test case {TestCaseId}.", testCaseId);
                return _responseHelper.SystemError<TestCaseResponse>();
            }
        }

        // ═══════════════════════════════════════════
        // UPDATE TEST CASE (metadata)
        // Creator or owner only.
        // Cannot edit a deprecated test case.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<TestCaseResponse>> UpdateTestCaseAsync(
            string actorUserId,
            string projectId,
            string testCaseId,
            UpdateTestCaseRequest request,
            CancellationToken token)
        {
            try
            {
                var (valid, actorObjId, projectObjId, tcObjId, error) = ParseIds(actorUserId, projectId, testCaseId);
                if (!valid) return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, error!);

                var (project, testCase, memberError) = await FetchProjectAndTestCaseAsync(projectObjId, tcObjId, actorObjId, token);
                if (memberError is not null) return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.UnAuthorized.ResponseCode, memberError);
                if (project is null || testCase is null) return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Test case or project not found.");

                var callerMember = project.Members.First(m => m.UserId == actorUserId);

                // Only creator or owner can edit
                if (testCase.CreatedById != actorObjId && callerMember.Role != ProjectRole.Owner)
                    return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.UnAuthorized.ResponseCode,
                        "Only the test case creator or project owner can edit this test case.");

                // Cannot edit a deprecated test case
                if (testCase.Status == "deprecated")
                    return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        "This test case is deprecated and can no longer be edited. Update its status first.");

                // Validate priority if provided
                if (request.Priority is not null && !ValidPriorities.Contains(request.Priority.ToLowerInvariant()))
                    return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"Invalid priority. Allowed: {string.Join(", ", ValidPriorities)}.");

                // Validate steps if provided
                if (request.Steps is not null)
                {
                    if (request.Steps.Count == 0)
                        return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                            "Steps cannot be empty. Provide at least one step or omit the field.");

                    for (int i = 0; i < request.Steps.Count; i++)
                    {
                        var s = request.Steps[i];
                        if (string.IsNullOrWhiteSpace(s.Action))
                            return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, $"Step {i + 1}: action is required.");
                        if (string.IsNullOrWhiteSpace(s.ExpectedOutcome))
                            return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, $"Step {i + 1}: expected outcome is required.");
                    }
                }

                // Build partial update
                var updates = new List<UpdateDefinition<TestCase>>();

                if (!string.IsNullOrWhiteSpace(request.Title))
                    updates.Add(Builders<TestCase>.Update.Set(tc => tc.Title, request.Title.Trim()));

                if (request.Description is not null)
                    updates.Add(Builders<TestCase>.Update.Set(tc => tc.Description, request.Description.Trim()));

                if (request.Preconditions is not null)
                    updates.Add(Builders<TestCase>.Update.Set(tc => tc.Preconditions, request.Preconditions.Trim()));

                if (request.ExpectedResult is not null)
                    updates.Add(Builders<TestCase>.Update.Set(tc => tc.ExpectedResult, request.ExpectedResult.Trim()));

                if (request.Priority is not null)
                    updates.Add(Builders<TestCase>.Update.Set(tc => tc.Priority, request.Priority.ToLowerInvariant()));

                if (request.Steps is not null)
                {
                    var rebuilt = request.Steps.Select((s, i) => new TestCaseStep
                    {
                        StepNumber = i + 1,
                        Action = s.Action.Trim(),
                        ExpectedOutcome = s.ExpectedOutcome.Trim()
                    }).ToList();
                    updates.Add(Builders<TestCase>.Update.Set(tc => tc.Steps, rebuilt));
                }

                if (request.Tags is not null)
                    updates.Add(Builders<TestCase>.Update.Set(tc => tc.Tags, SanitiseTags(request.Tags)));

                updates.Add(Builders<TestCase>.Update.Set(tc => tc.UpdatedAt, DateTime.UtcNow));

                if (updates.Count == 1)
                    return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "No valid fields provided to update.");

                await _db.TestCases.UpdateOneAsync(tc => tc.Id == testCaseId, Builders<TestCase>.Update.Combine(updates), cancellationToken: token);

                await LogActivityAsync(projectObjId, actorObjId, callerMember.FullName,
                    ActivityAction.TestCaseUpdated, ActivityEntityType.TestCase, tcObjId, $"TC-{testCase.CaseNumber:D3}: {testCase.Title}", token: token);

                var updated = await _db.TestCases.Find(tc => tc.Id == testCaseId).FirstOrDefaultAsync(token);
                return _responseHelper.Ok(MapToTestCaseResponse(updated!));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error updating test case {TestCaseId}.", testCaseId);
                return _responseHelper.SystemError<TestCaseResponse>();
            }
        }

        // ═══════════════════════════════════════════
        // UPDATE TEST CASE STATUS
        // Creator or owner only.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<TestCaseResponse>> UpdateTestCaseStatusAsync(
            string actorUserId,
            string projectId,
            string testCaseId,
            UpdateTestCaseStatusRequest request,
            CancellationToken token)
        {
            try
            {
                var (valid, actorObjId, projectObjId, tcObjId, error) = ParseIds(actorUserId, projectId, testCaseId);
                if (!valid) return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, error!);

                var newStatus = request.Status.Trim().ToLowerInvariant();
                if (!ValidStatuses.Contains(newStatus))
                    return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"Invalid status. Allowed: {string.Join(", ", ValidStatuses)}.");

                var (project, testCase, memberError) = await FetchProjectAndTestCaseAsync(projectObjId, tcObjId, actorObjId, token);
                if (memberError is not null) return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.UnAuthorized.ResponseCode, memberError);
                if (project is null || testCase is null) return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Test case or project not found.");

                var callerMember = project.Members.First(m => m.UserId == actorUserId);

                if (testCase.CreatedById != actorObjId && callerMember.Role != ProjectRole.Owner)
                    return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.UnAuthorized.ResponseCode,
                        "Only the test case creator or project owner can change the status.");

                // No-op guard
                if (testCase.Status == newStatus)
                    return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"Test case is already '{newStatus}'.");

                // Cannot activate a test case with no steps
                if (newStatus == "active" && testCase.Steps.Count == 0)
                    return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        "A test case must have at least one step before it can be set to 'active'.");

                var update = Builders<TestCase>.Update.Combine(
                    Builders<TestCase>.Update.Set(tc => tc.Status, newStatus),
                    Builders<TestCase>.Update.Set(tc => tc.UpdatedAt, DateTime.UtcNow)
                );

                await _db.TestCases.UpdateOneAsync(tc => tc.Id == testCaseId, update, cancellationToken: token);

                await LogActivityAsync(
                    projectId: projectObjId,
                    actorId: actorObjId,
                    actorName: callerMember.FullName,
                    action: ActivityAction.TestCaseStatusChanged,
                    entityType: ActivityEntityType.TestCase,
                    entityId: tcObjId,
                    entityTitle: $"TC-{testCase.CaseNumber:D3}: {testCase.Title}",
                    metadata: new Dictionary<string, string> { { "fromStatus", testCase.Status }, { "toStatus", newStatus } },
                    token: token);

                Log.Information("Test case {CaseLabel} status changed to '{Status}' by user {UserId}.",
                    $"TC-{testCase.CaseNumber:D3}", newStatus, actorUserId);

                var updated = await _db.TestCases.Find(tc => tc.Id == testCaseId).FirstOrDefaultAsync(token);
                return _responseHelper.Ok(MapToTestCaseResponse(updated!));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error updating status on test case {TestCaseId}.", testCaseId);
                return _responseHelper.SystemError<TestCaseResponse>();
            }
        }

        // ═══════════════════════════════════════════
        // ASSIGN TEST CASE
        // Owner or creator can assign/unassign a tester.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<TestCaseResponse>> AssignTestCaseAsync(
            string actorUserId,
            string projectId,
            string testCaseId,
            AssignTestCaseRequest request,
            CancellationToken token)
        {
            try
            {
                var (valid, actorObjId, projectObjId, tcObjId, error) = ParseIds(actorUserId, projectId, testCaseId);
                if (!valid) return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, error!);

                var (project, testCase, memberError) = await FetchProjectAndTestCaseAsync(projectObjId, tcObjId, actorObjId, token);
                if (memberError is not null) return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.UnAuthorized.ResponseCode, memberError);
                if (project is null || testCase is null) return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Test case or project not found.");

                var callerMember = project.Members.First(m => m.UserId == actorUserId);

                if (testCase.CreatedById != actorObjId && callerMember.Role != ProjectRole.Owner)
                    return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.UnAuthorized.ResponseCode,
                        "Only the test case creator or project owner can assign this test case.");

                var updates = new List<UpdateDefinition<TestCase>>();

                if (string.IsNullOrWhiteSpace(request.AssignedToId))
                {
                    // Unassign
                    if (!testCase.AssignedToId.HasValue)
                        return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "This test case has no one assigned.");

                    updates.Add(Builders<TestCase>.Update.Unset(tc => tc.AssignedToId));
                    updates.Add(Builders<TestCase>.Update.Unset(tc => tc.AssignedToName));
                    updates.Add(Builders<TestCase>.Update.Unset(tc => tc.AssignedToEmail));
                }
                else
                {
                    if (!ObjectId.TryParse(request.AssignedToId, out var assigneeObjId))
                        return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid assignedToId format.");

                    var assigneeMember = project.Members.FirstOrDefault(m => m.UserId == request.AssignedToId);
                    if (assigneeMember is null)
                        return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.NoRecordReturned.ResponseCode,
                            "The specified assignee is not a member of this project.");

                    if (assigneeMember.Role is not (ProjectRole.Tester or ProjectRole.Owner))
                        return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                            "Test cases can only be assigned to testers or owners.");

                    if (testCase.AssignedToId == assigneeObjId)
                        return _responseHelper.Fail<TestCaseResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "This person is already assigned to the test case.");

                    updates.Add(Builders<TestCase>.Update.Set(tc => tc.AssignedToId, assigneeObjId));
                    updates.Add(Builders<TestCase>.Update.Set(tc => tc.AssignedToName, assigneeMember.FullName));
                    updates.Add(Builders<TestCase>.Update.Set(tc => tc.AssignedToEmail, assigneeMember.Email));
                }

                updates.Add(Builders<TestCase>.Update.Set(tc => tc.UpdatedAt, DateTime.UtcNow));
                await _db.TestCases.UpdateOneAsync(tc => tc.Id == testCaseId, Builders<TestCase>.Update.Combine(updates), cancellationToken: token);

                await LogActivityAsync(projectObjId, actorObjId, callerMember.FullName,
                    ActivityAction.TestCaseAssigned, ActivityEntityType.TestCase, tcObjId, $"TC-{testCase.CaseNumber:D3}: {testCase.Title}", token: token);

                var updated = await _db.TestCases.Find(tc => tc.Id == testCaseId).FirstOrDefaultAsync(token);
                return _responseHelper.Ok(MapToTestCaseResponse(updated!));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error assigning test case {TestCaseId}.", testCaseId);
                return _responseHelper.SystemError<TestCaseResponse>();
            }
        }

        // ═══════════════════════════════════════════
        // DELETE TEST CASE
        // Owner or creator only.
        // Blocked if existing test runs reference this case.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<object>> DeleteTestCaseAsync(
            string actorUserId,
            string projectId,
            string testCaseId,
            CancellationToken token)
        {
            try
            {
                var (valid, actorObjId, projectObjId, tcObjId, error) = ParseIds(actorUserId, projectId, testCaseId);
                if (!valid) return _responseHelper.Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode, error!);

                var (project, testCase, memberError) = await FetchProjectAndTestCaseAsync(projectObjId, tcObjId, actorObjId, token);
                if (memberError is not null) return _responseHelper.Fail<object>(ResponseCodes.UnAuthorized.ResponseCode, memberError);
                if (project is null || testCase is null) return _responseHelper.Fail<object>(ResponseCodes.NoRecordReturned.ResponseCode, "Test case or project not found.");

                var callerMember = project.Members.First(m => m.UserId == actorUserId);

                if (testCase.CreatedById != actorObjId && callerMember.Role != ProjectRole.Owner)
                    return _responseHelper.Fail<object>(ResponseCodes.UnAuthorized.ResponseCode,
                        "Only the test case creator or project owner can delete this test case.");

                // Block deletion if test runs exist — deleting the definition orphans the history
                var runCount = await _db.TestRuns.CountDocumentsAsync(
                    r => r.TestCaseId == tcObjId, cancellationToken: token);

                if (runCount > 0)
                    return _responseHelper.Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"This test case has {runCount} test run(s) and cannot be deleted. " +
                        "Set its status to 'deprecated' instead to retire it.");

                await _db.TestCases.DeleteOneAsync(tc => tc.Id == testCaseId, token);

                await LogActivityAsync(projectObjId, actorObjId, callerMember.FullName,
                    ActivityAction.TestCaseDeleted, ActivityEntityType.TestCase, tcObjId, $"TC-{testCase.CaseNumber:D3}: {testCase.Title}", token: token);

                Log.Information("Test case {CaseLabel} deleted by user {UserId}.",
                    $"TC-{testCase.CaseNumber:D3}", actorUserId);

                return _responseHelper.Ok<object>(null, $"TC-{testCase.CaseNumber:D3} has been deleted.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error deleting test case {TestCaseId}.", testCaseId);
                return _responseHelper.SystemError<object>();
            }
        }

        // ═══════════════════════════════════════════
        // LOG TEST RUN
        // Any tester or owner in the project can execute.
        // Test case must be 'active'.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<TestRunResponse>> LogTestRunAsync(
            string actorUserId,
            string projectId,
            string testCaseId,
            LogTestRunRequest request,
            CancellationToken token)
        {
            try
            {
                var (valid, actorObjId, projectObjId, tcObjId, error) = ParseIds(actorUserId, projectId, testCaseId);
                if (!valid) return _responseHelper.Fail<TestRunResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, error!);

                // 1. Validate result
                var result = request.Result.Trim().ToLowerInvariant();
                if (!ValidRunResults.Contains(result))
                    return _responseHelper.Fail<TestRunResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"Invalid result. Allowed: {string.Join(", ", ValidRunResults)}.");

                // 2. Validate duration if provided
                if (request.Duration.HasValue && request.Duration.Value < 0)
                    return _responseHelper.Fail<TestRunResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Duration cannot be negative.");

                var (project, testCase, memberError) = await FetchProjectAndTestCaseAsync(projectObjId, tcObjId, actorObjId, token);
                if (memberError is not null) return _responseHelper.Fail<TestRunResponse>(ResponseCodes.UnAuthorized.ResponseCode, memberError);
                if (project is null || testCase is null) return _responseHelper.Fail<TestRunResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Test case or project not found.");

                // 3. Only testers and owners can log runs
                var callerMember = project.Members.First(m => m.UserId == actorUserId);
                if (callerMember.Role is not (ProjectRole.Tester or ProjectRole.Owner))
                    return _responseHelper.Fail<TestRunResponse>(ResponseCodes.UnAuthorized.ResponseCode,
                        "Only testers and owners can log test runs.");

                // 4. Test case must be active to be executed
                if (testCase.Status != "active")
                    return _responseHelper.Fail<TestRunResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"This test case is '{testCase.Status}'. Only 'active' test cases can be executed. " +
                        "Update its status before logging a run.");

                // 5. Validate step results if provided
                if (request.StepResults is not null && request.StepResults.Count > 0)
                {
                    var validStepNumbers = testCase.Steps.Select(s => s.StepNumber).ToHashSet();

                    foreach (var sr in request.StepResults)
                    {
                        if (!validStepNumbers.Contains(sr.StepNumber))
                            return _responseHelper.Fail<TestRunResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                                $"Step number {sr.StepNumber} does not exist in this test case.");

                        if (!ValidStepResults.Contains(sr.Result.Trim().ToLowerInvariant()))
                            return _responseHelper.Fail<TestRunResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                                $"Invalid step result '{sr.Result}' on step {sr.StepNumber}. " +
                                $"Allowed: {string.Join(", ", ValidStepResults)}.");
                    }

                    // Prevent duplicate step numbers in the request
                    var stepNums = request.StepResults.Select(sr => sr.StepNumber).ToList();
                    if (stepNums.Count != stepNums.Distinct().Count())
                        return _responseHelper.Fail<TestRunResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                            "Duplicate step numbers found in step results. Each step can only have one result.");
                }

                // 6. If result is 'failed', validate the linked bug if provided
                ObjectId? linkedBugObjId = null;
                string? linkedBugLabel = null;

                if (!string.IsNullOrWhiteSpace(request.LinkedBugId))
                {
                    if (result != "failed")
                        return _responseHelper.Fail<TestRunResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                            "A bug can only be linked to a failed test run.");

                    if (!ObjectId.TryParse(request.LinkedBugId, out var bugObjId))
                        return _responseHelper.Fail<TestRunResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid linked bug ID format.");

                    var linkedBug = await _db.Bugs
                        .Find(b => b.Id == request.LinkedBugId && b.ProjectId == projectId)
                        .FirstOrDefaultAsync(token);

                    if (linkedBug is null)
                        return _responseHelper.Fail<TestRunResponse>(ResponseCodes.NoRecordReturned.ResponseCode,
                            "The linked bug was not found in this project.");

                    linkedBugObjId = bugObjId;
                    linkedBugLabel = $"BUG-{linkedBug.BugNumber:D3}";
                }

                // 7. Build and insert the test run
                var stepResults = request.StepResults?.Select(sr => new TestRunStepResult
                {
                    StepNumber = sr.StepNumber,
                    Result = sr.Result.Trim().ToLowerInvariant(),
                    ActualOutcome = sr.ActualOutcome?.Trim()
                }).ToList() ?? new List<TestRunStepResult>();

                var now = DateTime.UtcNow;
                var testRun = new TestRun
                {
                    ProjectId = projectObjId,
                    TestCaseId = tcObjId,
                    TestCaseTitle = testCase.Title,
                    TestCaseCaseNumber = testCase.CaseNumber,
                    ExecutedById = actorObjId,
                    ExecutedByName = callerMember.FullName,
                    ExecutedByEmail = callerMember.Email,
                    Result = result,
                    Environment = request.Environment?.Trim(),
                    AppVersion = request.AppVersion?.Trim(),
                    Notes = request.Notes?.Trim(),
                    StepResults = stepResults,
                    LinkedBugId = linkedBugObjId,
                    LinkedBugLabel = linkedBugLabel,
                    Duration = request.Duration,
                    ExecutedAt = now
                };

                await _db.TestRuns.InsertOneAsync(testRun, cancellationToken: token);

                await LogActivityAsync(
                    projectId: projectObjId,
                    actorId: actorObjId,
                    actorName: callerMember.FullName,
                    action: ActivityAction.TestRunLogged,
                    entityType: ActivityEntityType.TestRun,
                    entityId: ObjectId.Parse(testRun.Id),
                    entityTitle: $"TC-{testCase.CaseNumber:D3}: {testCase.Title}",
                    metadata: new Dictionary<string, string> { { "result", result } },
                    token: token);

                Log.Information("Test run logged for {CaseLabel} with result '{Result}' by user {UserId}.",
                    $"TC-{testCase.CaseNumber:D3}", result, actorUserId);

                return _responseHelper.Ok(MapToTestRunResponse(testRun));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error logging test run for test case {TestCaseId}.", testCaseId);
                return _responseHelper.SystemError<TestRunResponse>();
            }
        }

        // ═══════════════════════════════════════════
        // GET TEST RUNS (paged + filtered)
        // Any project member can view.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<PagedTestRunsResponse>> GetTestRunsAsync(
            string actorUserId,
            string projectId,
            string testCaseId,
            GetTestRunsQuery query,
            CancellationToken token)
        {
            try
            {
                var (valid, actorObjId, projectObjId, tcObjId, error) = ParseIds(actorUserId, projectId, testCaseId);
                if (!valid) return _responseHelper.Fail<PagedTestRunsResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, error!);

                var project = await _db.Projects.Find(p => p.Id == projectId).FirstOrDefaultAsync(token);
                if (project is null)
                    return _responseHelper.Fail<PagedTestRunsResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Project not found.");

                if (!project.Members.Any(m => m.UserId == actorUserId))
                    return _responseHelper.Fail<PagedTestRunsResponse>(ResponseCodes.UnAuthorized.ResponseCode, "You are not a member of this project.");

                // Verify test case exists in project
                var testCaseExists = await _db.TestCases
                    .Find(tc => tc.Id == testCaseId && tc.ProjectId == projectObjId)
                    .AnyAsync(token);

                if (!testCaseExists)
                    return _responseHelper.Fail<PagedTestRunsResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Test case not found in this project.");

                var fb = Builders<TestRun>.Filter;
                var filters = new List<FilterDefinition<TestRun>>
            {
                fb.Eq(r => r.TestCaseId, tcObjId),
                fb.Eq(r => r.ProjectId, projectObjId)
            };

                if (!string.IsNullOrWhiteSpace(query.Result))
                    filters.Add(fb.Eq(r => r.Result, query.Result.ToLowerInvariant()));

                if (!string.IsNullOrWhiteSpace(query.ExecutedById) &&
                    ObjectId.TryParse(query.ExecutedById, out var executorFilter))
                    filters.Add(fb.Eq(r => r.ExecutedById, executorFilter));

                var combined = fb.And(filters);
                var pageSize = Math.Clamp(query.PageSize, 1, 100);
                var page = Math.Max(query.Page, 1);
                var skip = (page - 1) * pageSize;
                var totalCount = (int)await _db.TestRuns.CountDocumentsAsync(combined, cancellationToken: token);

                var runs = await _db.TestRuns
                    .Find(combined)
                    .SortByDescending(r => r.ExecutedAt)
                    .Skip(skip)
                    .Limit(pageSize)
                    .ToListAsync(token);

                return _responseHelper.Ok(new PagedTestRunsResponse
                {
                    Runs = runs.Select(MapToTestRunResponse).ToList(),
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching test runs for test case {TestCaseId}.", testCaseId);
                return _responseHelper.SystemError<PagedTestRunsResponse>();
            }
        }

        // ═══════════════════════════════════════════
        // GET TEST RUN BY ID
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<TestRunResponse>> GetTestRunByIdAsync(
            string actorUserId,
            string projectId,
            string testCaseId,
            string testRunId,
            CancellationToken token)
        {
            try
            {
                var (valid, actorObjId, projectObjId, tcObjId, error) = ParseIds(actorUserId, projectId, testCaseId);
                if (!valid) return _responseHelper.Fail<TestRunResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, error!);

                if (!ObjectId.TryParse(testRunId, out var runObjId))
                    return _responseHelper.Fail<TestRunResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid test run ID format.");

                var project = await _db.Projects.Find(p => p.Id == projectId).FirstOrDefaultAsync(token);
                if (project is null)
                    return _responseHelper.Fail<TestRunResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Project not found.");

                if (!project.Members.Any(m => m.UserId == actorUserId))
                    return _responseHelper.Fail<TestRunResponse>(ResponseCodes.UnAuthorized.ResponseCode, "You are not a member of this project.");

                var run = await _db.TestRuns
                    .Find(r => r.Id == testRunId && r.TestCaseId == tcObjId && r.ProjectId == projectObjId)
                    .FirstOrDefaultAsync(token);

                if (run is null)
                    return _responseHelper.Fail<TestRunResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Test run not found.");

                return _responseHelper.Ok(MapToTestRunResponse(run));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching test run {TestRunId}.", testRunId);
                return _responseHelper.SystemError<TestRunResponse>();
            }
        }

        // ═══════════════════════════════════════════
        // LINK BUG TO RUN (after the fact)
        // The person who logged the run or the owner can link a bug.
        // Run must be 'failed'. Bug must be in the same project.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<TestRunResponse>> LinkBugToRunAsync(
            string actorUserId,
            string projectId,
            string testCaseId,
            string testRunId,
            LinkBugToRunRequest request,
            CancellationToken token)
        {
            try
            {
                var (valid, actorObjId, projectObjId, tcObjId, error) = ParseIds(actorUserId, projectId, testCaseId);
                if (!valid) return _responseHelper.Fail<TestRunResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, error!);

                if (!ObjectId.TryParse(testRunId, out var runObjId))
                    return _responseHelper.Fail<TestRunResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid test run ID format.");

                if (!ObjectId.TryParse(request.BugId, out var bugObjId))
                    return _responseHelper.Fail<TestRunResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid bug ID format.");

                var project = await _db.Projects.Find(p => p.Id == projectId).FirstOrDefaultAsync(token);
                if (project is null)
                    return _responseHelper.Fail<TestRunResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Project not found.");

                var callerMember = project.Members.FirstOrDefault(m => m.UserId == actorUserId);
                if (callerMember is null)
                    return _responseHelper.Fail<TestRunResponse>(ResponseCodes.UnAuthorized.ResponseCode, "You are not a member of this project.");

                var run = await _db.TestRuns
                    .Find(r => r.Id == testRunId && r.TestCaseId == tcObjId && r.ProjectId == projectObjId)
                    .FirstOrDefaultAsync(token);

                if (run is null)
                    return _responseHelper.Fail<TestRunResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Test run not found.");

                // Only the executor or owner can link a bug
                if (run.ExecutedById != actorObjId && callerMember.Role != ProjectRole.Owner)
                    return _responseHelper.Fail<TestRunResponse>(ResponseCodes.UnAuthorized.ResponseCode,
                        "Only the person who logged this run or the project owner can link a bug.");

                // Can only link bugs to failed runs
                if (run.Result != "failed")
                    return _responseHelper.Fail<TestRunResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"Bugs can only be linked to failed test runs. This run result is '{run.Result}'.");

                // Prevent re-linking the same bug
                if (run.LinkedBugId == bugObjId)
                    return _responseHelper.Fail<TestRunResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "This bug is already linked to this test run.");

                // Verify the bug exists in the same project
                var bug = await _db.Bugs
                    .Find(b => b.Id == request.BugId && b.ProjectId == projectId)
                    .FirstOrDefaultAsync(token);

                if (bug is null)
                    return _responseHelper.Fail<TestRunResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "The specified bug was not found in this project.");

                var update = Builders<TestRun>.Update.Combine(
                    Builders<TestRun>.Update.Set(r => r.LinkedBugId, bugObjId),
                    Builders<TestRun>.Update.Set(r => r.LinkedBugLabel, $"BUG-{bug.BugNumber:D3}")
                );

                await _db.TestRuns.UpdateOneAsync(r => r.Id == testRunId, update, cancellationToken: token);

                await LogActivityAsync(projectObjId, actorObjId, callerMember.FullName,
                    ActivityAction.TestRunBugLinked, ActivityEntityType.TestRun, runObjId,
                    $"TC-{run.TestCaseCaseNumber:D3}: {run.TestCaseTitle}",
                    metadata: new Dictionary<string, string> { { "bugLabel", $"BUG-{bug.BugNumber:D3}" } },
                    token: token);

                var updated = await _db.TestRuns.Find(r => r.Id == testRunId).FirstOrDefaultAsync(token);
                return _responseHelper.Ok(MapToTestRunResponse(updated!));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error linking bug to test run {TestRunId}.", testRunId);
                return _responseHelper.SystemError<TestRunResponse>();
            }
        }

        // ═══════════════════════════════════════════
        // DELETE TEST RUN
        // The person who logged it or the owner can delete.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<object>> DeleteTestRunAsync(
            string actorUserId,
            string projectId,
            string testCaseId,
            string testRunId,
            CancellationToken token)
        {
            try
            {
                var (valid, actorObjId, projectObjId, tcObjId, error) = ParseIds(actorUserId, projectId, testCaseId);
                if (!valid) return _responseHelper.Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode, error!);

                if (!ObjectId.TryParse(testRunId, out var runObjId))
                    return _responseHelper.Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid test run ID format.");

                var project = await _db.Projects.Find(p => p.Id == projectId).FirstOrDefaultAsync(token);
                if (project is null)
                    return _responseHelper.Fail<object>(ResponseCodes.NoRecordReturned.ResponseCode, "Project not found.");

                var callerMember = project.Members.FirstOrDefault(m => m.UserId == actorUserId);
                if (callerMember is null)
                    return _responseHelper.Fail<object>(ResponseCodes.UnAuthorized.ResponseCode, "You are not a member of this project.");

                var run = await _db.TestRuns
                    .Find(r => r.Id == testRunId && r.TestCaseId == tcObjId && r.ProjectId == projectObjId)
                    .FirstOrDefaultAsync(token);

                if (run is null)
                    return _responseHelper.Fail<object>(ResponseCodes.NoRecordReturned.ResponseCode, "Test run not found.");

                if (run.ExecutedById != actorObjId && callerMember.Role != ProjectRole.Owner)
                    return _responseHelper.Fail<object>(ResponseCodes.UnAuthorized.ResponseCode,
                        "Only the person who logged this run or the project owner can delete it.");

                await _db.TestRuns.DeleteOneAsync(r => r.Id == testRunId, token);

                await LogActivityAsync(projectObjId, actorObjId, callerMember.FullName,
                    ActivityAction.TestRunDeleted, ActivityEntityType.TestRun, runObjId,
                    $"TC-{run.TestCaseCaseNumber:D3}: {run.TestCaseTitle}", token: token);

                Log.Information("Test run {RunId} deleted by user {UserId}.", testRunId, actorUserId);

                return _responseHelper.Ok<object>(null, "Test run has been deleted.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error deleting test run {TestRunId}.", testRunId);
                return _responseHelper.SystemError<object>();
            }
        }

        // ═══════════════════════════════════════════
        // PRIVATE HELPERS
        // ═══════════════════════════════════════════

        private async Task<int> GetNextCaseNumberAsync(ObjectId projectId, CancellationToken token)
        {
            var key = $"{projectId}_testcases";
            var filter = Builders<Counter>.Filter.Eq(c => c.Id, key);
            var update = Builders<Counter>.Update.Inc(c => c.Seq, 1);
            var opts = new FindOneAndUpdateOptions<Counter>
            {
                ReturnDocument = ReturnDocument.After,
                IsUpsert = true
            };
            var result = await _db.Counters.FindOneAndUpdateAsync(filter, update, opts, token);
            return result.Seq;
        }

        private string? ValidateCreateTestCaseRequest(CreateTestCaseRequest request, int caseNumber)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return $"Test case {caseNumber}: Title is required.";

            var priority = request.Priority?.Trim().ToLowerInvariant() ?? "";
            if (!ValidPriorities.Contains(priority))
                return $"Test case {caseNumber}: Invalid priority. Allowed: {string.Join(", ", ValidPriorities)}.";

            if (request.Steps is null || request.Steps.Count == 0)
                return $"Test case {caseNumber}: At least one step is required.";

            for (int i = 0; i < request.Steps.Count; i++)
            {
                var s = request.Steps[i];
                if (string.IsNullOrWhiteSpace(s.Action))
                    return $"Test case {caseNumber}, Step {i + 1}: Action is required.";
                if (string.IsNullOrWhiteSpace(s.ExpectedOutcome))
                    return $"Test case {caseNumber}, Step {i + 1}: Expected outcome is required.";
            }

            if (string.IsNullOrWhiteSpace(request.ExpectedResult))
                return $"Test case {caseNumber}: Expected result is required.";

            return null;
        }

        private async Task<(Project? project, TestCase? testCase, string? memberError)> FetchProjectAndTestCaseAsync(
            ObjectId projectObjId, ObjectId tcObjId, ObjectId actorObjId, CancellationToken token)
        {
            var projectTask = _db.Projects.Find(p => p.Id == projectObjId.ToString()).FirstOrDefaultAsync(token);
            var testCaseTask = _db.TestCases.Find(tc => tc.Id == tcObjId.ToString() && tc.ProjectId == projectObjId).FirstOrDefaultAsync(token);

            await Task.WhenAll(projectTask, testCaseTask);

            var project = await projectTask;
            var testCase = await testCaseTask;

            if (project is null || testCase is null) return (null, null, null);

            var isMember = project.Members.Any(m => m.UserId == actorObjId.ToString());
            if (!isMember) return (project, testCase, "You are not a member of this project.");

            return (project, testCase, null);
        }

        private static (bool valid, ObjectId actorId, ObjectId projectId, ObjectId entityId, string? error) ParseIds(
            string actorUserId, string projectId, string entityId)
        {
            if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                return (false, default, default, default, "Invalid user ID format.");
            if (!ObjectId.TryParse(projectId, out var projectObjId))
                return (false, default, default, default, "Invalid project ID format.");
            if (!ObjectId.TryParse(entityId, out var entityObjId))
                return (false, default, default, default, "Invalid ID format.");
            return (true, actorObjId, projectObjId, entityObjId, null);
        }

        private static List<string> SanitiseTags(List<string>? tags) =>
            tags?.Where(t => !string.IsNullOrWhiteSpace(t))
                 .Select(t => t.Trim().ToLowerInvariant())
                 .Distinct()
                 .ToList() ?? new List<string>();

        private async Task LogActivityAsync(
            ObjectId projectId, ObjectId actorId, string actorName,
            ActivityAction action, ActivityEntityType entityType, ObjectId entityId,
            string? entityTitle = null,
            Dictionary<string, string>? metadata = null,
            CancellationToken token = default)
        {
            try
            {
                await _db.ActivityLogs.InsertOneAsync(new ActivityLog
                {
                    ProjectId = projectId.ToString(),
                    ActorId = actorId.ToString(),
                    ActorName = actorName,
                    Action = action,
                    EntityType = entityType,
                    EntityId = entityId.ToString(),
                    EntityTitle = entityTitle,
                    Metadata = metadata,
                    CreatedAt = DateTime.UtcNow
                }, cancellationToken: token);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to write activity log for action '{Action}'.", action);
            }
        }

        private static TestCaseResponse MapToTestCaseResponse(TestCase tc) => new()
        {
            Id = tc.Id.ToString(),
            ProjectId = tc.ProjectId.ToString(),
            CaseNumber = tc.CaseNumber,
            Title = tc.Title,
            Description = tc.Description,
            Preconditions = tc.Preconditions,
            Steps = tc.Steps.Select(s => new TestCaseStepResponse
            {
                StepNumber = s.StepNumber,
                Action = s.Action,
                ExpectedOutcome = s.ExpectedOutcome
            }).ToList(),
            ExpectedResult = tc.ExpectedResult,
            Priority = tc.Priority,
            Status = tc.Status,
            CreatedBy = new TestCasePersonRef { UserId = tc.CreatedById.ToString(), FullName = tc.CreatedByName, Email = tc.CreatedByEmail },
            AssignedTo = tc.AssignedToId.HasValue
                ? new TestCasePersonRef { UserId = tc.AssignedToId.ToString()!, FullName = tc.AssignedToName!, Email = tc.AssignedToEmail! }
                : null,
            Tags = tc.Tags,
            CreatedAt = tc.CreatedAt,
            UpdatedAt = tc.UpdatedAt
        };

        private static TestCaseSummaryResponse MapToTestCaseSummary(TestCase tc) => new()
        {
            Id = tc.Id.ToString(),
            CaseNumber = tc.CaseNumber,
            Title = tc.Title,
            Priority = tc.Priority,
            Status = tc.Status,
            StepCount = tc.Steps.Count,
            AssignedTo = tc.AssignedToId.HasValue
                ? new TestCasePersonRef { UserId = tc.AssignedToId.ToString()!, FullName = tc.AssignedToName!, Email = tc.AssignedToEmail! }
                : null,
            Tags = tc.Tags,
            CreatedAt = tc.CreatedAt
        };

        private static TestRunResponse MapToTestRunResponse(TestRun run) => new()
        {
            Id = run.Id.ToString(),
            ProjectId = run.ProjectId.ToString(),
            TestCaseId = run.TestCaseId.ToString(),
            TestCaseCaseNumber = run.TestCaseCaseNumber,
            TestCaseTitle = run.TestCaseTitle,
            ExecutedBy = new TestCasePersonRef { UserId = run.ExecutedById.ToString(), FullName = run.ExecutedByName, Email = run.ExecutedByEmail },
            Result = run.Result,
            Environment = run.Environment,
            AppVersion = run.AppVersion,
            Notes = run.Notes,
            StepResults = run.StepResults.Select(sr => new TestRunStepResultResponse
            {
                StepNumber = sr.StepNumber,
                Result = sr.Result,
                ActualOutcome = sr.ActualOutcome
            }).ToList(),
            LinkedBugId = run.LinkedBugId?.ToString(),
            LinkedBugLabel = run.LinkedBugLabel,
            Duration = run.Duration,
            ExecutedAt = run.ExecutedAt
        };

        public string ExtractExcelText(byte[] fileBytes)
        {
            using var stream = new MemoryStream(fileBytes);
            using var workbook = new XLWorkbook(stream);

            var text = new StringBuilder();

            foreach (var sheet in workbook.Worksheets)
            {
                foreach (var row in sheet.RowsUsed())
                {
                    text.AppendLine(row.Cell(1).Value.ToString());
                }
            }

            return text.ToString();
        }

        public string ExtractDocxText(byte[] fileBytes)
        {
            using var stream = new MemoryStream(fileBytes);
            using var doc = WordprocessingDocument.Open(stream, false);

            return doc.MainDocumentPart.Document.Body.InnerText;
        }

        public string ExtractPdfText(byte[] fileBytes)
        {
            using var stream = new MemoryStream(fileBytes);
            using var doc = UglyToad.PdfPig.PdfDocument.Open(stream);

            var text = new StringBuilder();

            foreach (var page in doc.GetPages())
            {
                text.AppendLine(page.Text);
            }

            return text.ToString();
        }
    }
}
