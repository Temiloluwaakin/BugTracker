using BugTracker.Data;
using BugTracker.Data.Context;
using BugTracker.Data.Entities;
using BugTracker.Data.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BugTracker.Services.Services
{
    public interface IBugService
    {
        Task<ApiResponse<BugResponse>> CreateBugAsync(string actorUserId, string projectId, CreateBugRequest request, CancellationToken token);
        Task<ApiResponse<PagedBugsResponse>> GetBugsAsync(string actorUserId, string projectId, GetBugsQuery query, CancellationToken token);
        Task<ApiResponse<BugResponse>> GetBugByIdAsync(string actorUserId, string projectId, string bugId, CancellationToken token);
        Task<ApiResponse<BugResponse>> UpdateBugAsync(string actorUserId, string projectId, string bugId, UpdateBugRequest request, CancellationToken token);
        Task<ApiResponse<BugResponse>> UpdateBugStatusAsync(string actorUserId, string projectId, string bugId, UpdateBugStatusRequest request, CancellationToken token);
        Task<ApiResponse<BugResponse>> UpdateDeveloperStatusAsync(string actorUserId, string projectId, string bugId, UpdateDeveloperStatusRequest request, CancellationToken token);
        Task<ApiResponse<BugResponse>> AssignDeveloperAsync(string actorUserId, string projectId, string bugId, AssignDeveloperRequest request, CancellationToken token);
        Task<ApiResponse<BugResponse>> ReassignTesterAsync(string actorUserId, string projectId, string bugId, ReassignTesterRequest request, CancellationToken token);
        Task<ApiResponse<BugResponse>> UpsertTesterCommentAsync(string actorUserId, string projectId, string bugId, UpsertTesterCommentRequest request, CancellationToken token);
        Task<ApiResponse<BugResponse>> UpsertDeveloperCommentAsync(string actorUserId, string projectId, string bugId, UpsertDeveloperCommentRequest request, CancellationToken token);
        Task<ApiResponse<BugResponse>> AddAttachmentAsync(string actorUserId, string projectId, string bugId, AddAttachmentRequest request, CancellationToken token);
        Task<ApiResponse<object>> DeleteBugAsync(string actorUserId, string projectId, string bugId, CancellationToken token);
    }

    public class BugService : IBugService
    {
        private readonly DatabaseContext _db;

        private static readonly HashSet<string> ValidSeverities = new() { "critical", "high", "medium", "low" };
        private static readonly HashSet<string> ValidPriorities = new() { "urgent", "normal", "low" };
        private static readonly HashSet<string> ValidBugStatuses = new() { "none", "open", "inprogress", "resolved", "closed", "wontfix", "duplicate" };
        private static readonly HashSet<string> ValidDeveloperStatuses = new() { "none", "notassigned", "notstarted", "ongoing", "blocked", "fixed", "notabug" };
        

        // Statuses that mean a bug is fully closed — developer cannot update a closed bug
        private static readonly HashSet<string> TerminalStatuses = new() { "closed", "NotABug", "duplicate" };

        public BugService(DatabaseContext db)
        {
            _db = db;
        }

        
        /// <summary>
        /// Create Bug. Only testers (or owner) in the project can create bugs.
        /// </summary>
        /// <param name="actorUserId"></param>
        /// <param name="projectId"></param>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<ApiResponse<BugResponse>> CreateBugAsync(
            string actorUserId,
            string projectId,
            CreateBugRequest request,
            CancellationToken token)
        {
            try
            {
                // 1. Validate IDs
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                if (!ObjectId.TryParse(projectId, out var projectObjId))
                    return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid project ID format.");

                // 2. Validate enums
                if (!ValidSeverities.Contains(request.Severity.ToLowerInvariant()))
                    return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"Invalid severity. Allowed: {string.Join(", ", ValidSeverities)}.");

                if (!ValidPriorities.Contains(request.Priority.ToLowerInvariant()))
                    return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"Invalid priority. Allowed: {string.Join(", ", ValidPriorities)}.");

                // 3. Fetch project and verify it exists and is active
                var project = await _db.Projects.Find(p => p.Id == projectId).FirstOrDefaultAsync(token);
                if (project is null)
                    return Fail<BugResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Project not found.");

                if (project.Status == ProjectStatus.Archived)
                    return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Cannot create bugs in an archived project.");

                // 4. Verify caller is a member and is tester or owner (not viewer or developer)
                var callerMember = project.Members.FirstOrDefault(m => m.UserId == actorUserId);
                if (callerMember is null)
                    return Fail<BugResponse>(ResponseCodes.UnAuthorized.ResponseCode, "You are not a member of this project.");

                if (callerMember.Role is not (ProjectRole.Tester or ProjectRole.Owner))
                    return Fail<BugResponse>(ResponseCodes.UnAuthorized.ResponseCode, "Only testers and owners can create bug reports.");

                // 5. Validate and resolve optional developer assignment
                ProjectMember? assignedDevMember = null;
                ObjectId? assignedDevObjId = null;

                if (!string.IsNullOrWhiteSpace(request.AssignedDeveloperId))
                {
                    if (!ObjectId.TryParse(request.AssignedDeveloperId, out var devObjId))
                        return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid developer ID format.");

                    assignedDevMember = project.Members.FirstOrDefault(m => m.UserId == request.AssignedDeveloperId);
                    if (assignedDevMember is null)
                        return Fail<BugResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "The specified developer is not a member of this project.");

                    if (assignedDevMember.Role != ProjectRole.Developer)
                        return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "The assigned user does not have the 'developer' role.");

                    assignedDevObjId = devObjId;
                }

                if (!TryParseEnum<BugSeverity>(request.Severity, out var severity))
                {
                    Log.Warning("invalid project priority detected");
                    return new ApiResponse<BugResponse>
                    {
                        ResponseCode = ResponseCodes.InvalidEntryDetected.ResponseCode,
                        ResponseMessage = $"Invalid project priority '{request.Severity}'."
                    };
                }
                if (!TryParseEnum<BugPriority>(request.Priority, out var priority))
                {
                    Log.Warning("invalid project priority detected");
                    return new ApiResponse<BugResponse>
                    {
                        ResponseCode = ResponseCodes.InvalidEntryDetected.ResponseCode,
                        ResponseMessage = $"Invalid project priority '{request.Priority}'."
                    };
                }

                // 6. Get next atomic bug number for this project
                var bugNumber = await GetNextBugNumberAsync(projectObjId, token);

                // 7. Build and insert the bug document
                var now = DateTime.UtcNow;

                var bug = new Bug
                {
                    ProjectId = projectId,
                    BugNumber = bugNumber,
                    Title = request.Title.Trim(),
                    Description = request.Description.Trim(),
                    StepsToReproduce = request.StepsToReproduce?.Trim(),
                    ExpectedBehavior = request.ExpectedBehavior?.Trim(),
                    ActualBehavior = request.ActualBehavior?.Trim(),
                    Severity = severity,
                    Priority = priority,
                    Status = BugStatus.Open,
                    DeveloperStatus = assignedDevObjId.HasValue ? DevelopersStatus.NotStarted : null,
                    Environment = request.Environment?.Trim(),
                    Version = request.Version?.Trim(),
                    ReportedById = actorObjId,
                    ReportedByName = callerMember.FullName,
                    ReportedByEmail = callerMember.Email,
                    AssignedTesterId = actorObjId,       // reporter is the initial assigned tester
                    AssignedTesterName = callerMember.FullName,
                    AssignedTesterEmail = callerMember.Email,
                    AssignedDeveloperId = assignedDevObjId,
                    AssignedDeveloperName = assignedDevMember?.FullName,
                    AssignedDeveloperEmail = assignedDevMember?.Email,
                    Tags = SanitiseTags(request.Tags),
                    Attachments = new List<BugAttachment>(),
                    StatusHistory = new List<BugStatusHistory>
                    {
                        new()
                        {
                            FromStatus  = BugStatus.none,
                            ToStatus    = BugStatus.Open,
                            ChangedBy = actorObjId.ToString(),
                            ChangedAt   = now,
                            Comment     = "Bug created."
                        }
                    },
                    TesterComment = null,
                    DeveloperComment = null,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                await _db.Bugs.InsertOneAsync(bug, cancellationToken: token);

                await LogActivityAsync(
                    projectId: projectObjId,
                    actorId: actorObjId,
                    actorName: callerMember.FullName,
                    action: ActivityAction.BugCreated,
                    entityType: ActivityEntityType.Bug,
                    entityId: ObjectId.Parse(bug.Id),
                    entityTitle: $"BUG-{bugNumber:D3}: {bug.Title}",
                    token: token);

                Log.Information("Bug {BugLabel} created in project {ProjectId} by user {UserId}.",
                    $"BUG-{bugNumber:D3}", projectId, actorUserId);

                return Ok(MapToBugResponse(bug));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error creating bug in project {ProjectId} by user {UserId}.", projectId, actorUserId);
                return SystemError<BugResponse>();
            }
        }

      
        /// <summary>
        /// GET BUGS (paged + filtered) - Any project member can view bugs.
        /// </summary>
        /// <param name="actorUserId"></param>
        /// <param name="projectId"></param>
        /// <param name="query"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<ApiResponse<PagedBugsResponse>> GetBugsAsync(
            string actorUserId,
            string projectId,
            GetBugsQuery query,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return Fail<PagedBugsResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                if (!ObjectId.TryParse(projectId, out var projectObjId))
                    return Fail<PagedBugsResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid project ID format.");

                // Verify membership
                var project = await _db.Projects.Find(p => p.Id == projectId).FirstOrDefaultAsync(token);
                if (project is null)
                    return Fail<PagedBugsResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Project not found.");

                if (!project.Members.Any(m => m.UserId == actorUserId))
                    return Fail<PagedBugsResponse>(ResponseCodes.UnAuthorized.ResponseCode, "You are not a member of this project.");

                // Build dynamic filter
                var filterBuilder = Builders<Bug>.Filter;
                var filters = new List<FilterDefinition<Bug>>
                {
                    filterBuilder.Eq(b => b.ProjectId, projectId)
                };

                if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse<BugStatus>(query.Status, true, out var status))
                {
                    filters.Add(filterBuilder.Eq(b => b.Status, status));
                }

                if (!string.IsNullOrWhiteSpace(query.Severity) && Enum.TryParse<BugSeverity>(query.Severity, true, out var severity))
                {
                    filters.Add(filterBuilder.Eq(b => b.Severity, severity));
                }

                if (!string.IsNullOrWhiteSpace(query.Priority) && Enum.TryParse<BugPriority>(query.Priority, true, out var priority))
                {
                    filters.Add(filterBuilder.Eq(b => b.Priority, priority));
                }

                if (query.loggedinUser)
                {
                    filters.Add(
                        filterBuilder.Or(
                            filterBuilder.Eq(b => b.AssignedDeveloperId, actorObjId),
                            filterBuilder.Eq(b => b.AssignedTesterId, actorObjId),
                            filterBuilder.Eq(b => b.ReportedById, actorObjId)
                        )
                    );
                }

                if (!string.IsNullOrWhiteSpace(query.AssignedDeveloperId) &&
                    ObjectId.TryParse(query.AssignedDeveloperId, out var devFilterId))
                    filters.Add(filterBuilder.Eq(b => b.AssignedDeveloperId, devFilterId));

                if (!string.IsNullOrWhiteSpace(query.AssignedTesterId) &&
                    ObjectId.TryParse(query.AssignedTesterId, out var testerFilterId))
                    filters.Add(filterBuilder.Eq(b => b.AssignedTesterId, testerFilterId));

                if (!string.IsNullOrWhiteSpace(query.Tag))
                    filters.Add(filterBuilder.AnyEq(b => b.Tags, query.Tag.ToLowerInvariant()));

                var combinedFilter = filterBuilder.And(filters);

                // Clamp page size
                var pageSize = Math.Clamp(query.PageSize, 1, 100);
                var page = Math.Max(query.Page, 1);
                var skip = (page - 1) * pageSize;

                var totalCount = (int)await _db.Bugs.CountDocumentsAsync(combinedFilter, cancellationToken: token);

                var bugs = await _db.Bugs
                    .Find(combinedFilter)
                    .SortByDescending(b => b.CreatedAt)
                    .Skip(skip)
                    .Limit(pageSize)
                    .ToListAsync(token);

                var summaries = bugs.Select(MapToBugSummary).ToList();

                return Ok(new PagedBugsResponse
                {
                    Bugs = summaries,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching bugs for project {ProjectId}.", projectId);
                return SystemError<PagedBugsResponse>();
            }
        }


        // ═══════════════════════════════════════════
        // GET BUG BY ID
        // Any project member can view.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<BugResponse>> GetBugByIdAsync(
            string actorUserId,
            string projectId,
            string bugId,
            CancellationToken token)
        {
            try
            {
                var (valid, actorObjId, projectObjId, bugObjId, error) = ParseIds(actorUserId, projectId, bugId);
                if (!valid) return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, error!);

                var (project, bug, memberError) = await FetchProjectAndBugAsync(projectObjId, bugObjId, actorObjId, token);
                if (memberError is not null) return Fail<BugResponse>(ResponseCodes.UnAuthorized.ResponseCode, memberError);
                if (project is null || bug is null) return Fail<BugResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Bug or project not found.");

                return Ok(MapToBugResponse(bug));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching bug {BugId}.", bugId);
                return SystemError<BugResponse>();
            }
        }



        // ═══════════════════════════════════════════
        // UPDATE BUG (metadata only)
        // Owner, the assigned tester, or the original reporter.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<BugResponse>> UpdateBugAsync(
            string actorUserId,
            string projectId,
            string bugId,
            UpdateBugRequest request,
            CancellationToken token)
        {
            try
            {
                var (valid, actorObjId, projectObjId, bugObjId, error) = ParseIds(actorUserId, projectId, bugId);
                if (!valid) return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, error!);

                var (project, bug, memberError) = await FetchProjectAndBugAsync(projectObjId, bugObjId, actorObjId, token);
                if (memberError is not null) return Fail<BugResponse>(ResponseCodes.UnAuthorized.ResponseCode, memberError);
                if (project is null || bug is null) return Fail<BugResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Bug or project not found.");

                // Only owner, original reporter, or assigned tester can edit metadata
                var callerMember = project.Members.First(m => m.UserId == actorUserId);
                var canEdit = callerMember.Role == ProjectRole.Owner
                           || bug.ReportedById == actorObjId
                           || bug.AssignedTesterId == actorObjId;

                if (!canEdit)
                    return Fail<BugResponse>(ResponseCodes.UnAuthorized.ResponseCode,
                        "Only the project owner, the bug reporter, or the assigned tester can edit this bug.");

                // Cannot edit a terminal bug
                if (TerminalStatuses.Contains(bug.Status.ToString()))
                    return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"Bug is '{bug.Status}' and can no longer be edited. Reopen it first.");

                // Validate enums if provided
                if (request.Severity is not null && !ValidSeverities.Contains(request.Severity.ToLowerInvariant()))
                    return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"Invalid severity. Allowed: {string.Join(", ", ValidSeverities)}.");

                if (request.Priority is not null && !ValidPriorities.Contains(request.Priority.ToLowerInvariant()))
                    return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"Invalid priority. Allowed: {string.Join(", ", ValidPriorities)}.");

                // Build partial update
                var updates = new List<UpdateDefinition<Bug>>();

                if (!string.IsNullOrWhiteSpace(request.Title))
                    updates.Add(Builders<Bug>.Update.Set(b => b.Title, request.Title.Trim()));

                if (request.Description is not null)
                    updates.Add(Builders<Bug>.Update.Set(b => b.Description, request.Description.Trim()));

                if (request.StepsToReproduce is not null)
                    updates.Add(Builders<Bug>.Update.Set(b => b.StepsToReproduce, request.StepsToReproduce.Trim()));

                if (request.ExpectedBehavior is not null)
                    updates.Add(Builders<Bug>.Update.Set(b => b.ExpectedBehavior, request.ExpectedBehavior.Trim()));

                if (request.ActualBehavior is not null)
                    updates.Add(Builders<Bug>.Update.Set(b => b.ActualBehavior, request.ActualBehavior.Trim()));

                if (request.Severity is not null && Enum.TryParse<BugSeverity>(request.Severity, true, out var severity))
                {
                    updates.Add(Builders<Bug>.Update.Set(b => b.Severity, severity));
                }

                if (request.Priority is not null && Enum.TryParse<BugPriority>(request.Priority, true, out var priority))
                {
                    updates.Add(Builders<Bug>.Update.Set(b => b.Priority, priority));
                }

                if (request.Environment is not null)
                    updates.Add(Builders<Bug>.Update.Set(b => b.Environment, request.Environment.Trim()));

                if (request.Version is not null)
                    updates.Add(Builders<Bug>.Update.Set(b => b.Version, request.Version.Trim()));

                if (request.Tags is not null)
                    updates.Add(Builders<Bug>.Update.Set(b => b.Tags, SanitiseTags(request.Tags)));

                updates.Add(Builders<Bug>.Update.Set(b => b.UpdatedAt, DateTime.UtcNow));

                if (updates.Count == 1)
                    return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "No valid fields provided to update.");

                await _db.Bugs.UpdateOneAsync(b => b.Id == bugId, Builders<Bug>.Update.Combine(updates), cancellationToken: token);

                await LogActivityAsync(projectObjId, actorObjId, callerMember.FullName,
                    ActivityAction.BugUpdated, ActivityEntityType.Bug, bugObjId, $"BUG-{bug.BugNumber:D3}: {bug.Title}", token: token);

                var updated = await _db.Bugs.Find(b => b.Id == bugId).FirstOrDefaultAsync(token);
                return Ok(MapToBugResponse(updated!));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error updating bug {BugId}.", bugId);
                return SystemError<BugResponse>();
            }
        }



        // ═══════════════════════════════════════════
        // UPDATE BUG STATUS (tester-controlled)
        // Only the assigned tester or the original reporter can change
        // the main bug status. The bug is only truly done when
        // the tester sets it to 'closed'.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<BugResponse>> UpdateBugStatusAsync(
            string actorUserId,
            string projectId,
            string bugId,
            UpdateBugStatusRequest request,
            CancellationToken token)
        {
            try
            {
                var (valid, actorObjId, projectObjId, bugObjId, error) = ParseIds(actorUserId, projectId, bugId);
                if (!valid) return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, error!);

                var newStatus = request.Status.Trim().ToLowerInvariant();
                if (!ValidBugStatuses.Contains(newStatus))
                    return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"Invalid status. Allowed: {string.Join(", ", ValidBugStatuses)}.");

                var (project, bug, memberError) = await FetchProjectAndBugAsync(projectObjId, bugObjId, actorObjId, token);
                if (memberError is not null) return Fail<BugResponse>(ResponseCodes.UnAuthorized.ResponseCode, memberError);
                if (project is null || bug is null) return Fail<BugResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Bug or project not found.");

                var callerMember = project.Members.First(m => m.UserId == actorUserId);

                // Only assigned tester or original reporter can change bug status
                var canChangeStatus = bug.AssignedTesterId == actorObjId
                                    || bug.ReportedById == actorObjId
                                    || callerMember.Role == ProjectRole.Owner;

                if (!canChangeStatus)
                    return Fail<BugResponse>(ResponseCodes.UnAuthorized.ResponseCode,
                        "Only the assigned tester, the original reporter, or the project owner can change the bug status.");

                // No-op guard
                if (bug.Status.ToString() == newStatus)
                    return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, $"Bug is already '{newStatus}'.");

                // If marking as duplicate, the duplicateOfBugId is required and must exist in this project
                ObjectId? duplicateOfObjId = null;
                if (newStatus == "duplicate")
                {
                    if (string.IsNullOrWhiteSpace(request.DuplicateOfBugId))
                        return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                            "DuplicateOfBugId is required when marking a bug as duplicate.");

                    if (!ObjectId.TryParse(request.DuplicateOfBugId, out var dupId))
                        return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid DuplicateOfBugId format.");

                    if (dupId == bugObjId)
                        return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "A bug cannot be a duplicate of itself.");

                    var originalBug = await _db.Bugs
                        .Find(b => b.Id == request.DuplicateOfBugId && b.ProjectId == projectId)
                        .FirstOrDefaultAsync(token);

                    if (originalBug is null)
                        return Fail<BugResponse>(ResponseCodes.NoRecordReturned.ResponseCode,
                            "The original bug referenced by DuplicateOfBugId was not found in this project.");

                    duplicateOfObjId = dupId;
                }

                var now = DateTime.UtcNow;

                var statuuus = TryParseEnum<BugStatus>(newStatus, out var statushistorystatus);

                var historyEntry = new BugStatusHistory
                {
                    FromStatus = bug.Status,
                    ToStatus = statushistorystatus,
                    ChangedBy = actorObjId.ToString(),
                    ChangedAt = now,
                    Comment = request.Comment?.Trim()
                };

                var updates = new List<UpdateDefinition<Bug>>
                {
                    Builders<Bug>.Update.Set(b => b.Status, statushistorystatus),
                    Builders<Bug>.Update.Push(b => b.StatusHistory, historyEntry),
                    Builders<Bug>.Update.Set(b => b.UpdatedAt, now)
                };

                // Set resolvedAt when closing
                if (newStatus is "closed" or "resolved")
                    updates.Add(Builders<Bug>.Update.Set(b => b.ResolvedAt, now));

                // Clear resolvedAt when reopening
                if (newStatus == "open" || newStatus == "in_progress")
                    updates.Add(Builders<Bug>.Update.Unset(b => b.ResolvedAt));

                if (duplicateOfObjId.HasValue)
                    updates.Add(Builders<Bug>.Update.Set(b => b.DuplicateOfId, duplicateOfObjId));

                await _db.Bugs.UpdateOneAsync(b => b.Id == bugId, Builders<Bug>.Update.Combine(updates), cancellationToken: token);

                await LogActivityAsync(
                    projectId: projectObjId,
                    actorId: actorObjId,
                    actorName: callerMember.FullName,
                    action: ActivityAction.BugStatusChanged,
                    entityType: ActivityEntityType.Bug,
                    entityId: bugObjId,
                    entityTitle: $"BUG-{bug.BugNumber:D3}: {bug.Title}",
                    metadata: new Dictionary<string, string> { { "fromStatus", bug.Status.ToString() }, { "toStatus", newStatus } },
                    token: token);

                Log.Information("Bug {BugLabel} status changed from '{Old}' to '{New}' by user {UserId}.",
                    $"BUG-{bug.BugNumber:D3}", bug.Status, newStatus, actorUserId);

                var updated = await _db.Bugs.Find(b => b.Id == bugId).FirstOrDefaultAsync(token);
                return Ok(MapToBugResponse(updated!));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error updating status on bug {BugId}.", bugId);
                return SystemError<BugResponse>();
            }
        }



        // ═══════════════════════════════════════════
        // UPDATE DEVELOPER STATUS
        // Only the assigned developer can call this.
        // Cannot update a bug that is in a terminal state.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<BugResponse>> UpdateDeveloperStatusAsync(
            string actorUserId,
            string projectId,
            string bugId,
            UpdateDeveloperStatusRequest request,
            CancellationToken token)
        {
            try
            {
                var (valid, actorObjId, projectObjId, bugObjId, error) = ParseIds(actorUserId, projectId, bugId);
                if (!valid) return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, error!);

                var newDevStatus = request.DeveloperStatus.Trim().ToLowerInvariant();
                if (!ValidDeveloperStatuses.Contains(newDevStatus))
                    return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"Invalid developer status. Allowed: {string.Join(", ", ValidDeveloperStatuses)}.");

                var (project, bug, memberError) = await FetchProjectAndBugAsync(projectObjId, bugObjId, actorObjId, token);
                if (memberError is not null) return Fail<BugResponse>(ResponseCodes.UnAuthorized.ResponseCode, memberError);
                if (project is null || bug is null) return Fail<BugResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Bug or project not found.");

                // Only the assigned developer can update developer status
                if (bug.AssignedDeveloperId != actorObjId)
                    return Fail<BugResponse>(ResponseCodes.UnAuthorized.ResponseCode,
                        "Only the developer assigned to this bug can update the developer status.");

                // Cannot update a terminal bug
                if (TerminalStatuses.Contains(bug.Status.ToString()))
                    return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"This bug is '{bug.Status}'. Developer status cannot be updated on a closed bug.");

                // No-op guard
                if (bug.DeveloperStatus.ToString() == newDevStatus)
                    return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, $"Developer status is already '{newDevStatus}'.");

                var callerMember = project.Members.First(m => m.UserId == actorUserId);

                var devStatu = TryParseEnum<DevelopersStatus>(newDevStatus, out var devStatus);
                var newComment = new EmbeddedComment
                {
                    AuthorId = actorObjId,
                    AuthorName = callerMember.FullName,
                    Content = request.Comment.Trim(),
                    IsEdited = false,
                    CreatedAt = DateTime.UtcNow
                };

                var updates = new List<UpdateDefinition<Bug>>
                {
                    Builders<Bug>.Update.Set(b => b.DeveloperStatus, devStatus),
                    Builders<Bug>.Update.Set(b => b.DeveloperComment, newComment),
                    Builders<Bug>.Update.Set(b => b.UpdatedAt, DateTime.UtcNow)
                };

                await _db.Bugs.UpdateOneAsync(b => b.Id == bugId, Builders<Bug>.Update.Combine(updates), cancellationToken: token);

                await LogActivityAsync(
                    projectId: projectObjId,
                    actorId: actorObjId,
                    actorName: callerMember.FullName,
                    action: ActivityAction.BugDeveloperStatusChanged,
                    entityType: ActivityEntityType.Bug,
                    entityId: bugObjId,
                    entityTitle: $"BUG-{bug.BugNumber:D3}: {bug.Title}",
                    metadata: new Dictionary<string, string> { { "fromDevStatus", bug.DeveloperStatus.ToString() ?? DevelopersStatus.none.ToString()}, { "toDevStatus", newDevStatus } },
                    token: token);

                Log.Information("Bug {BugLabel} developer status updated to '{Status}' by developer {UserId}.",
                    $"BUG-{bug.BugNumber:D3}", newDevStatus, actorUserId);

                var updated = await _db.Bugs.Find(b => b.Id == bugId).FirstOrDefaultAsync(token);
                return Ok(MapToBugResponse(updated!));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error updating developer status on bug {BugId}.", bugId);
                return SystemError<BugResponse>();
            }
        }



        // ═══════════════════════════════════════════
        // ASSIGN DEVELOPER
        // Owner or the assigned tester can assign/unassign a developer.
        // The target must have the 'developer' role in the project.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<BugResponse>> AssignDeveloperAsync(
            string actorUserId,
            string projectId,
            string bugId,
            AssignDeveloperRequest request,
            CancellationToken token)
        {
            try
            {
                var (valid, actorObjId, projectObjId, bugObjId, error) = ParseIds(actorUserId, projectId, bugId);
                if (!valid) return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, error!);

                var (project, bug, memberError) = await FetchProjectAndBugAsync(projectObjId, bugObjId, actorObjId, token);
                if (memberError is not null) return Fail<BugResponse>(ResponseCodes.UnAuthorized.ResponseCode, memberError);
                if (project is null || bug is null) return Fail<BugResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Bug or project not found.");

                var callerMember = project.Members.First(m => m.UserId == actorUserId);

                // Only owner or assigned tester can assign a developer
                var canAssign = callerMember.Role == ProjectRole.Owner || bug.AssignedTesterId == actorObjId;
                if (!canAssign)
                    return Fail<BugResponse>(ResponseCodes.UnAuthorized.ResponseCode,
                        "Only the project owner or the assigned tester can assign a developer to this bug.");

                if (TerminalStatuses.Contains(bug.Status.ToString()))
                    return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"Cannot assign a developer to a bug with status '{bug.Status}'.");

                var updates = new List<UpdateDefinition<Bug>>();

                if (string.IsNullOrWhiteSpace(request.DeveloperId))
                {
                    // ── Unassign ──
                    if (!bug.AssignedDeveloperId.HasValue)
                        return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "This bug has no developer assigned.");

                    updates.Add(Builders<Bug>.Update.Unset(b => b.AssignedDeveloperId));
                    updates.Add(Builders<Bug>.Update.Unset(b => b.AssignedDeveloperName));
                    updates.Add(Builders<Bug>.Update.Unset(b => b.AssignedDeveloperEmail));
                    updates.Add(Builders<Bug>.Update.Unset(b => b.DeveloperStatus));
                    updates.Add(Builders<Bug>.Update.Set(b => b.UpdatedAt, DateTime.UtcNow));

                    await _db.Bugs.UpdateOneAsync(b => b.Id == bugId, Builders<Bug>.Update.Combine(updates), cancellationToken: token);

                    await LogActivityAsync(projectObjId, actorObjId, callerMember.FullName,
                        ActivityAction.BugDeveloperUnassigned, ActivityEntityType.Bug, bugObjId, $"BUG-{bug.BugNumber:D3}: {bug.Title}", token: token);

                    Log.Information("Developer unassigned from bug {BugLabel} by user {UserId}.",
                        $"BUG-{bug.BugNumber:D3}", actorUserId);
                }
                else
                {
                    // ── Assign ──
                    if (!ObjectId.TryParse(request.DeveloperId, out var devObjId))
                        return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid developer ID format.");

                    var devMember = project.Members.FirstOrDefault(m => m.UserId == request.DeveloperId);
                    if (devMember is null)
                        return Fail<BugResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "The specified developer is not a member of this project.");

                    if (devMember.Role != ProjectRole.Developer)
                        return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                            "The specified user does not have the 'developer' role in this project.");

                    // No-op guard
                    if (bug.AssignedDeveloperId == devObjId)
                        return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "This developer is already assigned to the bug.");

                    updates.Add(Builders<Bug>.Update.Set(b => b.AssignedDeveloperId, devObjId));
                    updates.Add(Builders<Bug>.Update.Set(b => b.AssignedDeveloperName, devMember.FullName));
                    updates.Add(Builders<Bug>.Update.Set(b => b.AssignedDeveloperEmail, devMember.Email));
                    updates.Add(Builders<Bug>.Update.Set(b => b.DeveloperStatus, DevelopersStatus.NotStarted));
                    updates.Add(Builders<Bug>.Update.Set(b => b.UpdatedAt, DateTime.UtcNow));

                    await _db.Bugs.UpdateOneAsync(b => b.Id == bugId, Builders<Bug>.Update.Combine(updates), cancellationToken: token);

                    await LogActivityAsync(
                        projectId: projectObjId,
                        actorId: actorObjId,
                        actorName: callerMember.FullName,
                        action: ActivityAction.BugDeveloperAssigned,
                        entityType: ActivityEntityType.Bug,
                        entityId: bugObjId,
                        entityTitle: $"BUG-{bug.BugNumber:D3}: {bug.Title}",
                        metadata: new Dictionary<string, string> { { "developerName", devMember.FullName } },
                        token: token);

                    Log.Information("Developer {DevId} assigned to bug {BugLabel} by user {UserId}.",
                        request.DeveloperId, $"BUG-{bug.BugNumber:D3}", actorUserId);
                }

                var updated = await _db.Bugs.Find(b => b.Id == bugId).FirstOrDefaultAsync(token);
                return Ok(MapToBugResponse(updated!));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error assigning developer to bug {BugId}.", bugId);
                return SystemError<BugResponse>();
            }
        }




        // ═══════════════════════════════════════════
        // REASSIGN TESTER
        // Owner or the current assigned tester can hand the bug
        // to a different tester in the same project.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<BugResponse>> ReassignTesterAsync(
            string actorUserId,
            string projectId,
            string bugId,
            ReassignTesterRequest request,
            CancellationToken token)
        {
            try
            {
                var (valid, actorObjId, projectObjId, bugObjId, error) = ParseIds(actorUserId, projectId, bugId);
                if (!valid) return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, error!);

                if (!ObjectId.TryParse(request.NewTesterId, out var newTesterObjId))
                    return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid new tester ID format.");

                var (project, bug, memberError) = await FetchProjectAndBugAsync(projectObjId, bugObjId, actorObjId, token);
                if (memberError is not null) return Fail<BugResponse>(ResponseCodes.UnAuthorized.ResponseCode, memberError);
                if (project is null || bug is null) return Fail<BugResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Bug or project not found.");

                var callerMember = project.Members.First(m => m.UserId == actorUserId);

                // Only owner or current assigned tester
                var canReassign = callerMember.Role == ProjectRole.Owner || bug.AssignedTesterId == actorObjId;
                if (!canReassign)
                    return Fail<BugResponse>(ResponseCodes.UnAuthorized.ResponseCode,
                        "Only the project owner or the currently assigned tester can reassign this bug.");

                // Can't reassign to yourself if you're already assigned
                if (bug.AssignedTesterId == newTesterObjId)
                    return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "This tester is already assigned to the bug.");

                // New tester must be a member with tester or owner role
                var newTesterMember = project.Members.FirstOrDefault(m => m.UserId == request.NewTesterId);
                if (newTesterMember is null)
                    return Fail<BugResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "The specified tester is not a member of this project.");

                if (newTesterMember.Role is not (ProjectRole.Tester or ProjectRole.Owner))
                    return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        "The specified user must have the 'tester' or 'owner' role to be assigned to a bug.");

                if (TerminalStatuses.Contains(bug.Status.ToString()))
                    return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"Cannot reassign a bug with status '{bug.Status}'.");

                var updates = Builders<Bug>.Update.Combine(
                    Builders<Bug>.Update.Set(b => b.AssignedTesterId, newTesterObjId),
                    Builders<Bug>.Update.Set(b => b.AssignedTesterName, newTesterMember.FullName),
                    Builders<Bug>.Update.Set(b => b.AssignedTesterEmail, newTesterMember.Email),
                    Builders<Bug>.Update.Set(b => b.UpdatedAt, DateTime.UtcNow)
                );

                await _db.Bugs.UpdateOneAsync(b => b.Id == bugId, updates, cancellationToken: token);

                await LogActivityAsync(
                    projectId: projectObjId,
                    actorId: actorObjId,
                    actorName: callerMember.FullName,
                    action: ActivityAction.BugTesterReassigned,
                    entityType: ActivityEntityType.Bug,
                    entityId: bugObjId,
                    entityTitle: $"BUG-{bug.BugNumber:D3}: {bug.Title}",
                    metadata: new Dictionary<string, string>
                    {
                    { "previousTester", bug.AssignedTesterName ?? "none" },
                    { "newTester",      newTesterMember.FullName }
                    },
                    token: token);

                Log.Information("Bug {BugLabel} reassigned from tester '{Old}' to '{New}' by user {UserId}.",
                    $"BUG-{bug.BugNumber:D3}", bug.AssignedTesterName, newTesterMember.FullName, actorUserId);

                var updated = await _db.Bugs.Find(b => b.Id == bugId).FirstOrDefaultAsync(token);
                return Ok(MapToBugResponse(updated!));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error reassigning tester on bug {BugId}.", bugId);
                return SystemError<BugResponse>();
            }
        }




        // ═══════════════════════════════════════════
        // UPSERT TESTER COMMENT
        // Only the assigned tester or the original reporter.
        // Overwrites the single tester comment on the bug.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<BugResponse>> UpsertTesterCommentAsync(
            string actorUserId,
            string projectId,
            string bugId,
            UpsertTesterCommentRequest request,
            CancellationToken token)
        {
            try
            {
                var (valid, actorObjId, projectObjId, bugObjId, error) = ParseIds(actorUserId, projectId, bugId);
                if (!valid) return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, error!);

                var (project, bug, memberError) = await FetchProjectAndBugAsync(projectObjId, bugObjId, actorObjId, token);
                if (memberError is not null) return Fail<BugResponse>(ResponseCodes.UnAuthorized.ResponseCode, memberError);
                if (project is null || bug is null) return Fail<BugResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Bug or project not found.");

                // Only assigned tester or reporter can write the tester comment
                var canComment = bug.AssignedTesterId == actorObjId || bug.ReportedById == actorObjId;
                if (!canComment)
                    return Fail<BugResponse>(ResponseCodes.UnAuthorized.ResponseCode,
                        "Only the assigned tester or the original reporter can write the tester comment on this bug.");

                var callerMember = project.Members.First(m => m.UserId == actorUserId);
                var now = DateTime.UtcNow;
                var isEdit = bug.TesterComment is not null;

                var newComment = new EmbeddedComment
                {
                    AuthorId = actorObjId,
                    AuthorName = callerMember.FullName,
                    Content = request.Content.Trim(),
                    IsEdited = isEdit,
                    CreatedAt = isEdit ? bug.TesterComment!.CreatedAt : now,
                    UpdatedAt = now
                };

                var update = Builders<Bug>.Update.Combine(
                    Builders<Bug>.Update.Set(b => b.TesterComment, newComment),
                    Builders<Bug>.Update.Set(b => b.UpdatedAt, now)
                );

                await _db.Bugs.UpdateOneAsync(b => b.Id == bugId, update, cancellationToken: token);

                await LogActivityAsync(projectObjId, actorObjId, callerMember.FullName,
                    isEdit ? ActivityAction.BugTesterCommentEdited : ActivityAction.BugTesterCommentAdded,
                    ActivityEntityType.Bug, bugObjId, $"BUG-{bug.BugNumber:D3}: {bug.Title}", token: token);

                var updated = await _db.Bugs.Find(b => b.Id == bugId).FirstOrDefaultAsync(token);
                return Ok(MapToBugResponse(updated!));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error upserting tester comment on bug {BugId}.", bugId);
                return SystemError<BugResponse>();
            }
        }




        // ═══════════════════════════════════════════
        // UPSERT DEVELOPER COMMENT
        // Only the assigned developer can write/overwrite this.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<BugResponse>> UpsertDeveloperCommentAsync(
            string actorUserId,
            string projectId,
            string bugId,
            UpsertDeveloperCommentRequest request,
            CancellationToken token)
        {
            try
            {
                var (valid, actorObjId, projectObjId, bugObjId, error) = ParseIds(actorUserId, projectId, bugId);
                if (!valid) return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, error!);

                var (project, bug, memberError) = await FetchProjectAndBugAsync(projectObjId, bugObjId, actorObjId, token);
                if (memberError is not null) return Fail<BugResponse>(ResponseCodes.UnAuthorized.ResponseCode, memberError);
                if (project is null || bug is null) return Fail<BugResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Bug or project not found.");

                // Must be the assigned developer
                if (bug.AssignedDeveloperId != actorObjId)
                    return Fail<BugResponse>(ResponseCodes.UnAuthorized.ResponseCode,
                        "Only the developer assigned to this bug can write the developer comment.");

                if (TerminalStatuses.Contains(bug.Status.ToString()))
                    return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"This bug is '{bug.Status}'. Comments cannot be updated on a closed bug.");

                var callerMember = project.Members.First(m => m.UserId == actorUserId);
                var now = DateTime.UtcNow;
                var isEdit = bug.DeveloperComment is not null;

                var newComment = new EmbeddedComment
                {
                    AuthorId = actorObjId,
                    AuthorName = callerMember.FullName,
                    Content = request.Content.Trim(),
                    IsEdited = isEdit,
                    CreatedAt = isEdit ? bug.DeveloperComment!.CreatedAt : now,
                    UpdatedAt = now
                };

                var update = Builders<Bug>.Update.Combine(
                    Builders<Bug>.Update.Set(b => b.DeveloperComment, newComment),
                    Builders<Bug>.Update.Set(b => b.UpdatedAt, now)
                );

                await _db.Bugs.UpdateOneAsync(b => b.Id == bugId, update, cancellationToken: token);

                await LogActivityAsync(projectObjId, actorObjId, callerMember.FullName,
                    isEdit ? ActivityAction.BugDeveloperCommentEdited : ActivityAction.BugDeveloperCommentAdded,
                    ActivityEntityType.Bug, bugObjId, $"BUG-{bug.BugNumber:D3}: {bug.Title}", token: token);

                var updated = await _db.Bugs.Find(b => b.Id == bugId).FirstOrDefaultAsync(token);
                return Ok(MapToBugResponse(updated!));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error upserting developer comment on bug {BugId}.", bugId);
                return SystemError<BugResponse>();
            }
        }




        // ═══════════════════════════════════════════
        // ADD ATTACHMENT
        // Any project member can attach files to a bug.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<BugResponse>> AddAttachmentAsync(
            string actorUserId,
            string projectId,
            string bugId,
            AddAttachmentRequest request,
            CancellationToken token)
        {
            try
            {
                var (valid, actorObjId, projectObjId, bugObjId, error) = ParseIds(actorUserId, projectId, bugId);
                if (!valid) return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, error!);

                var (project, bug, memberError) = await FetchProjectAndBugAsync(projectObjId, bugObjId, actorObjId, token);
                if (memberError is not null) return Fail<BugResponse>(ResponseCodes.UnAuthorized.ResponseCode, memberError);
                if (project is null || bug is null) return Fail<BugResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Bug or project not found.");

                if (TerminalStatuses.Contains(bug.Status.ToString()))
                    return Fail<BugResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"Cannot add attachments to a bug with status '{bug.Status}'.");

                var attachment = new BugAttachment
                {
                    Url = request.Url.Trim(),
                    FileName = request.FileName.Trim(),
                    FileType = request.FileType.Trim(),
                    UploadedBy = actorUserId,
                    UploadedAt = DateTime.UtcNow
                };

                var update = Builders<Bug>.Update.Combine(
                    Builders<Bug>.Update.Push(b => b.Attachments, attachment),
                    Builders<Bug>.Update.Set(b => b.UpdatedAt, DateTime.UtcNow)
                );

                await _db.Bugs.UpdateOneAsync(b => b.Id == bugId, update, cancellationToken: token);

                var callerMember = project.Members.First(m => m.UserId == actorUserId);
                await LogActivityAsync(projectObjId, actorObjId, callerMember.FullName,
                    ActivityAction.BugAttachmentAdded, ActivityEntityType.Bug, bugObjId, $"BUG-{bug.BugNumber:D3}: {bug.Title}", token: token);

                var updated = await _db.Bugs.Find(b => b.Id == bugId).FirstOrDefaultAsync(token);
                return Ok(MapToBugResponse(updated!));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error adding attachment to bug {BugId}.", bugId);
                return SystemError<BugResponse>();
            }
        }





        // ═══════════════════════════════════════════
        // DELETE BUG
        // Only owner or the original reporter can delete.
        // Cannot delete a closed/resolved bug.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<object>> DeleteBugAsync(
            string actorUserId,
            string projectId,
            string bugId,
            CancellationToken token)
        {
            try
            {
                var (valid, actorObjId, projectObjId, bugObjId, error) = ParseIds(actorUserId, projectId, bugId);
                if (!valid) return Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode, error!);

                var (project, bug, memberError) = await FetchProjectAndBugAsync(projectObjId, bugObjId, actorObjId, token);
                if (memberError is not null) return Fail<object>(ResponseCodes.UnAuthorized.ResponseCode, memberError);
                if (project is null || bug is null) return Fail<object>(ResponseCodes.NoRecordReturned.ResponseCode, "Bug or project not found.");

                var callerMember = project.Members.First(m => m.UserId == actorUserId);
                var canDelete = callerMember.Role == ProjectRole.Owner || bug.ReportedById == actorObjId;

                if (!canDelete)
                    return Fail<object>(ResponseCodes.UnAuthorized.ResponseCode,
                        "Only the project owner or the original bug reporter can delete this bug.");

                if (bug.Status is BugStatus.Resolved or BugStatus.Closed)
                    return Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"A '{bug.Status}' bug cannot be deleted. Archive the project if you want to remove all data.");

                await _db.Bugs.DeleteOneAsync(b => b.Id == bugId, token);

                await LogActivityAsync(projectObjId, actorObjId, callerMember.FullName,
                    ActivityAction.BugDeleted, ActivityEntityType.Bug, bugObjId, $"BUG-{bug.BugNumber:D3}: {bug.Title}", token: token);

                Log.Information("Bug {BugLabel} deleted by user {UserId}.", $"BUG-{bug.BugNumber:D3}", actorUserId);

                return Ok<object>(null, $"BUG-{bug.BugNumber:D3} has been deleted.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error deleting bug {BugId}.", bugId);
                return SystemError<object>();
            }
        }




        // ═══════════════════════════════════════════
        // PRIVATE HELPERS
        // ═══════════════════════════════════════════

        /// <summary>
        /// Atomically increments and returns the next bug number for a project.
        /// Uses the counters collection to avoid race conditions.
        /// </summary>
        private async Task<int> GetNextBugNumberAsync(ObjectId projectId, CancellationToken token)
        {
            var key = $"{projectId}_bugs";
            var filter = Builders<Counter>.Filter.Eq(c => c.Id, key);
            var update = Builders<Counter>.Update.Inc(c => c.Seq, 1);
            var opts = new FindOneAndUpdateOptions<Counter> { ReturnDocument = ReturnDocument.After, IsUpsert = true };

            var result = await _db.Counters.FindOneAndUpdateAsync(filter, update, opts, token);
            return result.Seq;
        }

        /// <summary>
        /// Fetches both the project and bug in parallel, verifies project membership.
        /// Returns a tuple so individual methods don't repeat these lookups.
        /// </summary>
        private async Task<(Project? project, Bug? bug, string? memberError)> FetchProjectAndBugAsync(
            ObjectId projectObjId,
            ObjectId bugObjId,
            ObjectId actorObjId,
            CancellationToken token)
        {
            var projectTask = _db.Projects.Find(p => p.Id == projectObjId.ToString()).FirstOrDefaultAsync(token);
            var bugTask = _db.Bugs.Find(b => b.Id == bugObjId.ToString() && b.ProjectId == projectObjId.ToString()).FirstOrDefaultAsync(token);

            await Task.WhenAll(projectTask, bugTask);

            var project = await projectTask;
            var bug = await bugTask;

            if (project is null || bug is null) return (null, null, null);

            var isMember = project.Members.Any(m => m.UserId == actorObjId.ToString());
            if (!isMember) return (project, bug, "You are not a member of this project.");

            return (project, bug, null);
        }

        /// <summary>Parse all three IDs in one call. Returns a named tuple for clean destructuring.</summary>
        private static (bool valid, ObjectId actorId, ObjectId projectId, ObjectId bugId, string? error) ParseIds(
            string actorUserId, string projectId, string bugId)
        {
            if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                return (false, default, default, default, "Invalid user ID format.");

            if (!ObjectId.TryParse(projectId, out var projectObjId))
                return (false, default, default, default, "Invalid project ID format.");

            if (!ObjectId.TryParse(bugId, out var bugObjId))
                return (false, default, default, default, "Invalid bug ID format.");

            return (true, actorObjId, projectObjId, bugObjId, null);
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

        private static BugResponse MapToBugResponse(Bug bug) => new()
        {
            Id = bug.Id.ToString(),
            ProjectId = bug.ProjectId.ToString(),
            BugNumber = bug.BugNumber,
            Title = bug.Title,
            Description = bug.Description,
            StepsToReproduce = bug.StepsToReproduce,
            ExpectedBehavior = bug.ExpectedBehavior,
            ActualBehavior = bug.ActualBehavior,
            Severity = bug.Severity.ToString(),
            Priority = bug.Priority.ToString(),
            Status = bug.Status.ToString(),
            DeveloperStatus = bug.DeveloperStatus.ToString() ?? DevelopersStatus.NotAssigned.ToString(),
            Environment = bug.Environment,
            Version = bug.Version,
            ReportedBy = new BugPersonRef { UserId = bug.ReportedById.ToString(), FullName = bug.ReportedByName, Email = bug.ReportedByEmail },
            AssignedTester = bug.AssignedTesterId.HasValue
                ? new BugPersonRef { UserId = bug.AssignedTesterId.ToString()!, FullName = bug.AssignedTesterName!, Email = bug.AssignedTesterEmail! }
                : null,
            AssignedDeveloper = bug.AssignedDeveloperId.HasValue
                ? new BugPersonRef { UserId = bug.AssignedDeveloperId.ToString()!, FullName = bug.AssignedDeveloperName!, Email = bug.AssignedDeveloperEmail! }
                : null,
            TesterComment = bug.TesterComment is null ? null : new BugComment
            {
                AuthorId = bug.TesterComment.AuthorId.ToString(),
                AuthorName = bug.TesterComment.AuthorName,
                Content = bug.TesterComment.Content,
                IsEdited = bug.TesterComment.IsEdited,
                CreatedAt = bug.TesterComment.CreatedAt,
                UpdatedAt = bug.TesterComment.UpdatedAt
            },
            DeveloperComment = bug.DeveloperComment is null ? null : new BugComment
            {
                AuthorId = bug.DeveloperComment.AuthorId.ToString(),
                AuthorName = bug.DeveloperComment.AuthorName,
                Content = bug.DeveloperComment.Content,
                IsEdited = bug.DeveloperComment.IsEdited,
                CreatedAt = bug.DeveloperComment.CreatedAt,
                UpdatedAt = bug.DeveloperComment.UpdatedAt
            },
            Attachments = bug.Attachments.Select(a => new BugAttachmentResponse
            {
                Url = a.Url,
                FileName = a.FileName,
                FileType = a.FileType,
                UploadedBy = a.UploadedBy.ToString(),
                UploadedAt = a.UploadedAt
            }).ToList(),
            Tags = bug.Tags,
            DuplicateOf = bug.DuplicateOfId?.ToString(),
            StatusHistory = bug.StatusHistory.Select(h => new StatusHistoryResponse
            {
                FromStatus = h.FromStatus.ToString(),
                ToStatus = h.ToStatus.ToString(),
                ChangedBy = h.ChangedBy.ToString(),
                Comment = h.Comment,
                ChangedAt = h.ChangedAt
            }).ToList(),
            ResolvedAt = bug.ResolvedAt,
            CreatedAt = bug.CreatedAt,
            UpdatedAt = bug.UpdatedAt
        };

        private static BugSummaryResponse MapToBugSummary(Bug bug) => new()
        {
            Id = bug.Id.ToString(),
            BugNumber = bug.BugNumber,
            Title = bug.Title,
            Severity = bug.Severity.ToString(),
            Priority = bug.Priority.ToString(),
            Status = bug.Status.ToString(),
            DeveloperStatus = bug.DeveloperStatus.ToString() ?? DevelopersStatus.NotAssigned.ToString(),
            AssignedDeveloper = bug.AssignedDeveloperId.HasValue
                ? new BugPersonRef { UserId = bug.AssignedDeveloperId.ToString()!, FullName = bug.AssignedDeveloperName!, Email = bug.AssignedDeveloperEmail! }
                : null,
            AssignedTester = bug.AssignedTesterId.HasValue
                ? new BugPersonRef { UserId = bug.AssignedTesterId.ToString()!, FullName = bug.AssignedTesterName!, Email = bug.AssignedTesterEmail! }
                : null,
            Tags = bug.Tags,
            CreatedAt = bug.CreatedAt
        };

        private static ApiResponse<T> Ok<T>(T? data, string message = "Success") => new()
        {
            ResponseCode = ResponseCodes.Success.ResponseCode,
            ResponseMessage = message,
            Data = data
        };

        private static ApiResponse<T> Fail<T>(string code, string message) => new()
        {
            ResponseCode = code,
            ResponseMessage = message
        };

        private static ApiResponse<T> SystemError<T>() => new()
        {
            ResponseCode = ResponseCodes.SystemMalfunction.ResponseCode,
            ResponseMessage = "An unexpected error occurred. Please try again later."
        };

        private static bool TryParseEnum<T>(string value, out T result) where T : struct
        {
            return Enum.TryParse(value, true, out result) && Enum.IsDefined(typeof(T), result);
        }
    }
}
