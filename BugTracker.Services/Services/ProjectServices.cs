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
    public interface IProjectService
    {
        Task<ApiResponse<ProjectResponse>> CreateProjectAsync(string actorUserId, CreateProjectRequest request, CancellationToken token);
        Task<ApiResponse<List<ProjectSummaryResponse>>> GetMyProjectsAsync(string actorUserId, CancellationToken token);
        Task<ApiResponse<ProjectResponse>> GetProjectByIdAsync(string actorUserId, string projectId, CancellationToken token);
        Task<ApiResponse<ProjectResponse>> UpdateProjectAsync(string actorUserId, string projectId, UpdateProjectRequest request, CancellationToken token);
        Task<ApiResponse<object>> DeleteProjectAsync(string actorUserId, string projectId, CancellationToken token);
        Task<ApiResponse<object>> InviteMemberAsync(string actorUserId, string projectId, InviteMemberRequest request, CancellationToken token);
        Task<ApiResponse<object>> UpdateMemberRoleAsync(string actorUserId, string projectId, string targetUserId, UpdateMemberRoleRequest request, CancellationToken token);
        Task<ApiResponse<object>> RemoveMemberAsync(string actorUserId, string projectId, string targetUserId, CancellationToken token);
        Task<ApiResponse<List<MemberResponse>>> GetMembersAsync(string actorUserId, string projectId, CancellationToken token);
    }


    public class ProjectServices : IProjectService
    {
        private readonly DatabaseContext _db;

        private static readonly HashSet<string> ValidProjectStatuses = new() { "active", "archived", "completed" };
        private static readonly HashSet<string> ValidInviteRoles = new() { "tester", "viewer" };
        private static readonly HashSet<string> ValidUpdateRoles = new() { "tester", "viewer" };

        public ProjectServices(DatabaseContext db)
        {
            _db = db;
        }

        
        // CREATE PROJECT
        public async Task<ApiResponse<ProjectResponse>> CreateProjectAsync(string actorUserId, CreateProjectRequest request, CancellationToken token)
        {
            try
            {
                // 1. Resolve the acting user — we need their display info for the members embed
                var actor = await _db.Users.Find(u => u.Id == actorUserId).FirstOrDefaultAsync(token);
                if (actor is null)
                {
                    Log.Warning("CreateProject: acting user {UserId} not found in database.", actorUserId);
                    return new ApiResponse<ProjectResponse>
                    {
                        ResponseCode = ResponseCodes.NoRecordReturned.ResponseCode,
                        ResponseMessage = "Authenticated user account not found."
                    };
                }

                if (!TryParseEnum<ProjectPriorities>(request.ProjectPriority, out var priority))
                {
                    return new ApiResponse<ProjectResponse>
                    {
                        ResponseCode = ResponseCodes.InvalidEntryDetected.ResponseCode,
                        ResponseMessage = $"Invalid project priority '{request.ProjectPriority}'."
                    };
                }

                // 2. Sanitise inputs
                var projectName = request.Name.Trim();
                var tags = request.Tags?
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim().ToLowerInvariant())
                    .Distinct()
                    .ToList() ?? new List<string>();

                // 3. Build the owner member subdocument (embedded inside the project)
                var ownerMember = new ProjectMember
                {
                    UserId = actor.Id,
                    Email = actor.Email,
                    FullName = actor.FullName,
                    Role = ProjectRole.Owner,
                    JoinedAt = DateTime.UtcNow,
                    AddedBy = actor.Id   // owner added themselves
                };

                var members = new List<ProjectMember> { ownerMember };

                // 4. Process additional members
                if (request.Members != null && request.Members.Any())
                {
                    var normalizedEmails = request.Members
                        .Where(m => !string.IsNullOrWhiteSpace(m.Email))
                        .Select(m => m.Email.Trim().ToLower())
                        .Distinct()
                        .ToList();

                    var users = await _db.Users
                        .Find(u => normalizedEmails.Contains(u.Email.ToLower()))
                        .ToListAsync(token);

                    foreach (var reqMember in request.Members)
                    {
                        var email = reqMember.Email.Trim().ToLower();

                        var user = users.FirstOrDefault(u =>
                            u.Email.ToLower() == email);

                        if (user == null)
                            continue;

                        if (user.Id == actor.Id)
                            continue;

                        if (members.Any(m => m.UserId == user.Id))
                            continue;

                        if (!TryParseEnum<ProjectRole>(reqMember.Role, out var role))
                        {
                            Log.Warning("Invalid role '{Role}' supplied for {Email}", reqMember.Role, reqMember.Email);
                            continue;
                        }

                        members.Add(new ProjectMember
                        {
                            UserId = user.Id,
                            Email = user.Email,
                            FullName = user.FullName,
                            Role = role,
                            JoinedAt = DateTime.UtcNow,
                            AddedBy = actor.Id
                        });
                    }
                }

                // 5. Create project
                var project = new Project
                {
                    Name = projectName,
                    Description = request.Description?.Trim(),
                    OwnerId = actor.Id,
                    Status = ProjectStatus.Active,
                    Members = members,
                    Tags = tags,
                    ProjectStartDate = request.ProjectStartDate,
                    ProjectDueDate = request.ProjectDueDate,
                    ProjectPriority = priority,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _db.Projects.InsertOneAsync(project, cancellationToken: token);

                // 5. Log the activity
                await LogActivityAsync(
                    projectId: ObjectId.Parse(project.Id),
                    actorId: ObjectId.Parse(actor.Id),
                    actorName: actor.FullName,
                    action: ActivityAction.ProjectCreated,
                    entityType: ActivityEntityType.Project,
                    entityId: ObjectId.Parse(project.Id),
                    entityTitle: project.Name,
                    token: token
                );

                Log.Information("Project {ProjectId} '{Name}' created by user {UserId}.", project.Id, project.Name, actorUserId);
                return Ok(MapToProjectResponse(project));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error creating project for user {UserId}.", actorUserId);
                return SystemError<ProjectResponse>();
            }
        }

        
        // GET MY PROJECTS
        
        public async Task<ApiResponse<List<ProjectSummaryResponse>>> GetMyProjectsAsync(string actorUserId, CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return Fail<List<ProjectSummaryResponse>>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                // Compound index on members.userId will serve this query efficiently
                var projects = await _db.Projects
                    .Find(p => p.Members.Any(m => m.UserId == actorUserId))
                    .SortByDescending(p => p.CreatedAt)
                    .ToListAsync(token);

                var summaries = projects.Select(p =>
                {
                    var myMember = p.Members.First(m => m.UserId == actorUserId);
                    return new ProjectSummaryResponse
                    {
                        Id = p.Id.ToString(),
                        Name = p.Name,
                        Description = p.Description,
                        Status = p.Status.ToString(),
                        ProjectStartDate = p.ProjectStartDate,
                        ProjectDueDate = p.ProjectDueDate,
                        Priority = p.ProjectPriority.ToString(),
                        YourRole = myMember.Role.ToString(),
                        MemberCount = p.Members.Count,
                        Tags = p.Tags,
                        CreatedAt = p.CreatedAt
                    };
                }).ToList();

                return Ok(summaries);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching projects for user {UserId}.", actorUserId);
                return SystemError<List<ProjectSummaryResponse>>();
            }
        }

        
        // GET PROJECT BY ID
        public async Task<ApiResponse<ProjectResponse>> GetProjectByIdAsync(string actorUserId, string projectId, CancellationToken token)
        {
            try
            {
                // 1. Validate IDs
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return Fail<ProjectResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                if (!ObjectId.TryParse(projectId, out var projectObjId))
                    return Fail<ProjectResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid project ID format.");

                // 2. Fetch project
                var project = await _db.Projects
                    .Find(p => p.Id == projectId)
                    .FirstOrDefaultAsync(token);

                if (project is null)
                    return Fail<ProjectResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Project not found.");

                // 3. Membership gate — caller must be a member to view
                var callerMember = project.Members.FirstOrDefault(m => m.UserId == actorUserId);
                if (callerMember is null)
                    return Fail<ProjectResponse>(ResponseCodes.UnAuthorized.ResponseCode, "You are not a member of this project.");

                return Ok(MapToProjectResponse(project));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching project {ProjectId} for user {UserId}.", projectId, actorUserId);
                return SystemError<ProjectResponse>();
            }
        }

        
        // UPDATE PROJECT
        public async Task<ApiResponse<ProjectResponse>> UpdateProjectAsync(
            string actorUserId,
            string projectId,
            UpdateProjectRequest request,
            CancellationToken token)
        {
            try
            {
                // 1. Validate IDs
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return Fail<ProjectResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                if (!ObjectId.TryParse(projectId, out var projectObjId))
                    return Fail<ProjectResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid project ID format.");

                // 2. Fetch project
                var project = await _db.Projects
                    .Find(p => p.Id  == projectId)
                    .FirstOrDefaultAsync(token);

                if (project is null)
                    return Fail<ProjectResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Project not found.");

                // 3. Only the owner can update project metadata
                if (project.OwnerId != actorUserId)
                {
                    Log.Warning("UpdateProject: user {UserId} attempted to update project {ProjectId} but is not owner.",
                        actorUserId, projectId);
                    return Fail<ProjectResponse>(ResponseCodes.UnAuthorized.ResponseCode, "Only the project owner can update project details.");
                }

                // 4. Validate status if provided
                if (request.Status is not null && !ValidProjectStatuses.Contains(request.Status))
                    return Fail<ProjectResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"Invalid status. Allowed values: {string.Join(", ", ValidProjectStatuses)}.");

                // 5. Build update definition — only set fields that were actually provided
                var updates = new List<UpdateDefinition<Project>>();

                if (!string.IsNullOrWhiteSpace(request.Name))
                    updates.Add(Builders<Project>.Update.Set(p => p.Name, request.Name.Trim()));

                if (request.Description is not null)
                    updates.Add(Builders<Project>.Update.Set(p => p.Description, request.Description.Trim()));

                if (request.Status is not null)
                    updates.Add(Builders<Project>.Update.Set(p => p.Status.ToString(), request.Status));

                if (request.Tags is not null)
                {
                    var sanitisedTags = request.Tags
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Select(t => t.Trim().ToLowerInvariant())
                        .Distinct()
                        .ToList();
                    updates.Add(Builders<Project>.Update.Set(p => p.Tags, sanitisedTags));
                }

                // Always bump updatedAt
                updates.Add(Builders<Project>.Update.Set(p => p.UpdatedAt, DateTime.UtcNow));

                if (updates.Count == 1) // only updatedAt — nothing meaningful changed
                    return Fail<ProjectResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "No valid fields provided to update.");

                var combined = Builders<Project>.Update.Combine(updates);
                await _db.Projects.UpdateOneAsync(p => p.Id  == projectId, combined, cancellationToken: token);

                // 6. Re-fetch the updated document to return fresh state
                var updated = await _db.Projects.Find(p => p.Id  == projectId).FirstOrDefaultAsync(token);

                // 7. Activity log
                await LogActivityAsync(
                    projectId: projectObjId,
                    actorId: actorObjId,
                    actorName: project.Members.First(m => m.UserId == actorUserId).FullName,
                    action: ActivityAction.ProjectUpdated,
                    entityType: ActivityEntityType.Project,
                    entityId: projectObjId,
                    entityTitle: updated!.Name,
                    token: token);

                Log.Information("Project {ProjectId} updated by owner {UserId}.", projectId, actorUserId);

                return Ok(MapToProjectResponse(updated));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error updating project {ProjectId} for user {UserId}.", projectId, actorUserId);
                return SystemError<ProjectResponse>();
            }
        }

        
        // DELETE (ARCHIVE) PROJECT
        // We do a soft-delete: status → "archived".
        // Hard-delete is destructive and loses all bugs/test cases,
        // so we guard against that by design.
        public async Task<ApiResponse<object>> DeleteProjectAsync(
            string actorUserId,
            string projectId,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                if (!ObjectId.TryParse(projectId, out var projectObjId))
                    return Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid project ID format.");

                var project = await _db.Projects
                    .Find(p => p.Id  == projectId)
                    .FirstOrDefaultAsync(token);

                if (project is null)
                    return Fail<object>(ResponseCodes.NoRecordReturned.ResponseCode, "Project not found.");

                // Only owner can delete
                if (project.OwnerId != actorUserId)
                {
                    Log.Warning("DeleteProject: user {UserId} attempted to delete project {ProjectId} but is not owner.",
                        actorUserId, projectId);
                    return Fail<object>(ResponseCodes.UnAuthorized.ResponseCode, "Only the project owner can delete this project.");
                }

                // Prevent double-archiving
                if (project.Status == ProjectStatus.Archived)
                    return Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Project is already archived.");

                var update = Builders<Project>.Update
                    .Set(p => p.Status, ProjectStatus.Archived)
                    .Set(p => p.UpdatedAt, DateTime.UtcNow);

                await _db.Projects.UpdateOneAsync(p => p.Id  == projectId, update, cancellationToken: token);

                await LogActivityAsync(
                    projectId: projectObjId,
                    actorId: actorObjId,
                    actorName: project.Members.First(m => m.UserId == actorUserId).FullName,
                    action: ActivityAction.ProjectArchived,
                    entityType: ActivityEntityType.Project,
                    entityId: projectObjId,
                    entityTitle: project.Name,
                    token: token);

                Log.Information("Project {ProjectId} archived by owner {UserId}.", projectId, actorUserId);

                return Ok<object>(null, "Project has been archived successfully.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error deleting project {ProjectId} for user {UserId}.", projectId, actorUserId);
                return SystemError<object>();
            }
        }

        
        // INVITE MEMBER
        
        public async Task<ApiResponse<object>> InviteMemberAsync(
            string actorUserId,
            string projectId,
            InviteMemberRequest request,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                if (!ObjectId.TryParse(projectId, out var projectObjId))
                    return Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid project ID format.");

                    // 1. Validate role
                    var role = request.Role.Trim().ToLowerInvariant();
                if (!ValidInviteRoles.Contains(role))
                    return Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"Invalid role. You can only invite members as: {string.Join(", ", ValidInviteRoles)}.");

                // 2. Normalise email
                var invitedEmail = request.Email.Trim().ToLowerInvariant();

                // 3. Fetch project and verify caller is owner
                var project = await _db.Projects
                    .Find(p => p.Id  == projectId)
                    .FirstOrDefaultAsync(token);

                if (project is null)
                    return Fail<object>(ResponseCodes.NoRecordReturned.ResponseCode, "Project not found.");

                if (project.OwnerId != actorUserId)
                    return Fail<object>(ResponseCodes.UnAuthorized.ResponseCode, "Only the project owner can invite members.");

                // 4. Prevent owner from inviting themselves
                var actor = await _db.Users.Find(u => u.Id == actorUserId).FirstOrDefaultAsync(token);
                if (actor is null)
                    return Fail<object>(ResponseCodes.NoRecordReturned.ResponseCode, "Authenticated user account not found.");

                if (actor.Email == invitedEmail)
                    return Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode, "You cannot invite yourself to your own project.");

                // 5. Check if this email is already a member
                var alreadyMember = project.Members.Any(m => m.Email == invitedEmail);
                if (alreadyMember)
                    return Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode, "This user is already a member of the project.");

                // 6. Check for an existing pending invite for this email in this project
                var existingInvite = await _db.Invitations
                    .Find(i => i.ProjectId == projectId
                             && i.InvitedEmail == invitedEmail
                             && i.Status == InvitationStatus.Pending)
                    .FirstOrDefaultAsync(token);

                if (existingInvite is not null)
                    return Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        "A pending invitation has already been sent to this email address.");

                // 7. Does the invitee already have an account?
                var existingUser = await _db.Users
                    .Find(u => u.Email == invitedEmail)
                    .FirstOrDefaultAsync(token);

                if (existingUser is not null)
                {
                    // ── Fast path: user exists → add them directly as a member ──
                    var newMember = new ProjectMember
                    {
                        UserId = existingUser.Id,
                        Email = existingUser.Email,
                        FullName = existingUser.FullName,
                        Role = role == "tester" ? ProjectRole.Tester : ProjectRole.Viewer,
                        JoinedAt = DateTime.UtcNow,
                        AddedBy = actorUserId
                    };

                    var memberUpdate = Builders<Project>.Update
                        .Push(p => p.Members, newMember)
                        .Set(p => p.UpdatedAt, DateTime.UtcNow);

                    await _db.Projects.UpdateOneAsync(p => p.Id  == projectId, memberUpdate, cancellationToken: token);

                    await LogActivityAsync(
                        projectId: projectObjId,
                        actorId: actorObjId,
                        actorName: actor.FullName,
                        action: ActivityAction.MemberAdded,
                        entityType: ActivityEntityType.Member,
                        entityId: ObjectId.Parse(existingUser.Id),
                        entityTitle: existingUser.FullName,
                        metadata: new Dictionary<string, string> { { "role", role } },
                        token: token);

                    Log.Information("User {InvitedUserId} directly added to project {ProjectId} by owner {OwnerId}.",
                        existingUser.Id, projectId, actorUserId);

                    return Ok<object>(null, $"{existingUser.FullName} has been added to the project as {role}.");
                }
                else
                {
                    // ── Slow path: user doesn't exist → create an invitation ──
                    var invitation = new Invitation
                    {
                        ProjectId = projectId,
                        ProjectName = project.Name,
                        InvitedEmail = invitedEmail,
                        InvitedBy = actorUserId,
                        Role = role == "tester" ? ProjectRole.Tester : ProjectRole.Viewer,
                        Token = GenerateSecureToken(),
                        Status = InvitationStatus.Pending,
                        ExpiresAt = DateTime.UtcNow.AddDays(7),
                        CreatedAt = DateTime.UtcNow
                    };

                    await _db.Invitations.InsertOneAsync(invitation, cancellationToken: token);

                    // TODO: dispatch an email with the invite link containing invitation.Token
                    // e.g. await _emailService.SendInvitationEmailAsync(invitedEmail, project.Name, invitation.Token, role);

                    await LogActivityAsync(
                        projectId: projectObjId,
                        actorId: actorObjId,
                        actorName: actor.FullName,
                        action: ActivityAction.MemberInvited,
                        entityType: ActivityEntityType.Member,
                        entityId: ObjectId.Parse(invitation.Id),
                        entityTitle: invitedEmail,
                        metadata: new Dictionary<string, string> { { "role", role }, { "email", invitedEmail } },
                        token: token);

                    Log.Information("Invitation sent to {Email} for project {ProjectId} by owner {OwnerId}.",
                        invitedEmail, projectId, actorUserId);

                    return Ok<object>(null, $"Invitation sent to {invitedEmail}. They have 7 days to accept.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error inviting member to project {ProjectId} by user {UserId}.", projectId, actorUserId);
                return SystemError<object>();
            }
        }

        
        // UPDATE MEMBER ROLE
        public async Task<ApiResponse<object>> UpdateMemberRoleAsync(
            string actorUserId,
            string projectId,
            string targetUserId,
            UpdateMemberRoleRequest request,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid actor user ID format.");

                if (!ObjectId.TryParse(projectId, out var projectObjId))
                    return Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid project ID format.");

                if (!ObjectId.TryParse(targetUserId, out var targetObjId))
                    return Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid target user ID format.");

                // 1. Validate the new role
                var newRole = request.Role.Trim().ToLowerInvariant();
                if (!ValidUpdateRoles.Contains(newRole))
                    return Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"Invalid role. Allowed values: {string.Join(", ", ValidUpdateRoles)}. " +
                        "The owner role cannot be assigned through this endpoint.");

                // 2. Fetch project
                var project = await _db.Projects
                    .Find(p => p.Id  == projectId)
                    .FirstOrDefaultAsync(token);

                if (project is null)
                    return Fail<object>(ResponseCodes.NoRecordReturned.ResponseCode, "Project not found.");

                // 3. Only owner can change roles
                if (project.OwnerId != actorUserId)
                    return Fail<object>(ResponseCodes.UnAuthorized.ResponseCode, "Only the project owner can change member roles.");

                // 4. Owner cannot change their own role (would effectively demote themselves)
                if (targetObjId == actorObjId)
                    return Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        "You cannot change your own role. Transfer ownership is not supported via this endpoint.");

                // 5. Find the target member
                var targetMember = project.Members.FirstOrDefault(m => m.UserId == targetUserId);
                if (targetMember is null)
                    return Fail<object>(ResponseCodes.NoRecordReturned.ResponseCode, "The specified user is not a member of this project.");

                // 6. No-op guard — role is already what's being set
                if (targetMember.Role.ToString() == newRole)
                    return Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        $"This member already has the '{newRole}' role.");

                // 7. Update the specific member's role inside the array using positional operator
                var filter = Builders<Project>.Filter.And(
                    Builders<Project>.Filter.Eq(p => p.Id, projectId),
                    Builders<Project>.Filter.ElemMatch(p => p.Members, m => m.UserId == targetUserId)
                );

                var update = Builders<Project>.Update
                    .Set("members.$.role", newRole)
                    .Set(p => p.UpdatedAt, DateTime.UtcNow);

                await _db.Projects.UpdateOneAsync(filter, update, cancellationToken: token);

                // 8. Get actor name for activity log
                var actorMember = project.Members.First(m => m.UserId == actorUserId);

                await LogActivityAsync(
                    projectId: projectObjId,
                    actorId: actorObjId,
                    actorName: actorMember.FullName,
                    action: ActivityAction.MemberRoleChanged,
                    entityType: ActivityEntityType.Member,
                    entityId: targetObjId,
                    entityTitle: targetMember.FullName,
                    metadata: new Dictionary<string, string>
                    {
                    { "fromRole", targetMember.Role.ToString() },
                    { "toRole",   newRole }
                    },
                    token: token);

                Log.Information(
                    "Member {TargetUserId} role changed from '{OldRole}' to '{NewRole}' in project {ProjectId} by owner {OwnerId}.",
                    targetUserId, targetMember.Role, newRole, projectId, actorUserId);

                return Ok<object>(null, $"Member role updated to '{newRole}' successfully.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error updating member role in project {ProjectId} by user {UserId}.", projectId, actorUserId);
                return SystemError<object>();
            }
        }

        
        // REMOVE MEMBER
        public async Task<ApiResponse<object>> RemoveMemberAsync(
            string actorUserId,
            string projectId,
            string targetUserId,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid actor user ID format.");

                if (!ObjectId.TryParse(projectId, out var projectObjId))
                    return Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid project ID format.");

                if (!ObjectId.TryParse(targetUserId, out var targetObjId))
                    return Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid target user ID format.");

                // 1. Fetch project
                var project = await _db.Projects
                    .Find(p => p.Id  == projectId)
                    .FirstOrDefaultAsync(token);

                if (project is null)
                    return Fail<object>(ResponseCodes.NoRecordReturned.ResponseCode, "Project not found.");

                // 2. Only owner can remove members
                if (project.OwnerId != actorUserId)
                    return Fail<object>(ResponseCodes.UnAuthorized.ResponseCode, "Only the project owner can remove members.");

                // 3. Owner cannot remove themselves (project must always have an owner)
                if (targetObjId == actorObjId)
                    return Fail<object>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        "You cannot remove yourself from your own project. Archive the project if you no longer need it.");

                // 4. Check the target is actually a member
                var targetMember = project.Members.FirstOrDefault(m => m.UserId == targetUserId);
                if (targetMember is null)
                    return Fail<object>(ResponseCodes.NoRecordReturned.ResponseCode, "The specified user is not a member of this project.");

                // 5. Pull the member subdocument from the array
                var update = Builders<Project>.Update
                    .PullFilter(p => p.Members, m => m.UserId == targetUserId)
                    .Set(p => p.UpdatedAt, DateTime.UtcNow);

                await _db.Projects.UpdateOneAsync(p => p.Id  == projectId, update, cancellationToken: token);

                // 6. Activity log
                var actorMember = project.Members.First(m => m.UserId == actorUserId);

                await LogActivityAsync(
                    projectId: projectObjId,
                    actorId: actorObjId,
                    actorName: actorMember.FullName,
                    action: ActivityAction.MemberRemoved,
                    entityType: ActivityEntityType.Member,
                    entityId: targetObjId,
                    entityTitle: targetMember.FullName,
                    token: token);

                Log.Information("Member {TargetUserId} removed from project {ProjectId} by owner {OwnerId}.",
                    targetUserId, projectId, actorUserId);

                return Ok<object>(null, $"{targetMember.FullName} has been removed from the project.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error removing member from project {ProjectId} by user {UserId}.", projectId, actorUserId);
                return SystemError<object>();
            }
        }

        
        // GET MEMBERS        
        public async Task<ApiResponse<List<MemberResponse>>> GetMembersAsync(
            string actorUserId,
            string projectId,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return Fail<List<MemberResponse>>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                if (!ObjectId.TryParse(projectId, out var projectObjId))
                    return Fail<List<MemberResponse>>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid project ID format.");

                var project = await _db.Projects
                    .Find(p => p.Id  == projectId)
                    .FirstOrDefaultAsync(token);

                if (project is null)
                    return Fail<List<MemberResponse>>(ResponseCodes.NoRecordReturned.ResponseCode, "Project not found.");

                // Any member (owner / tester / viewer) can view the member list
                var isMember = project.Members.Any(m => m.UserId == actorUserId);
                if (!isMember)
                    return Fail<List<MemberResponse>>(ResponseCodes.UnAuthorized.ResponseCode, "You are not a member of this project.");

                var members = project.Members
                    .OrderBy(m => m.JoinedAt)
                    .Select(m => new MemberResponse
                    {
                        UserId = m.UserId.ToString(),
                        Email = m.Email,
                        FullName = m.FullName,
                        Role = m.Role.ToString(),
                        JoinedAt = m.JoinedAt,
                        AddedBy = m.AddedBy.ToString()
                    }).ToList();

                return Ok(members);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching members for project {ProjectId} by user {UserId}.", projectId, actorUserId);
                return SystemError<List<MemberResponse>>();
            }
        }

        



        //--------------------------------------------------------
        // PRIVATE HELPERS     

        /// <summary>
        /// Writes an immutable activity log entry. Fire-and-forget safe — errors are
        /// logged but do NOT bubble up to fail the parent operation.
        /// </summary>
        private async Task LogActivityAsync(
            ObjectId projectId,
            ObjectId actorId,
            string actorName,
            ActivityAction action,
            ActivityEntityType entityType,
            ObjectId entityId,
            string? entityTitle = null,
            Dictionary<string, string>? metadata = null,
            CancellationToken token = default)
        {
            try
            {
                var log = new ActivityLog
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
                };

                await _db.ActivityLogs.InsertOneAsync(log, cancellationToken: token);
            }
            catch (Exception ex)
            {
                // Activity logging must never break the main flow
                Log.Error(ex, "Failed to write activity log for action '{Action}' on project {ProjectId}.",
                    action, projectId);
            }
        }


        /// <summary>Generates a cryptographically secure random token for invitation links.</summary>
        private static string GenerateSecureToken()
        {
            var bytes = new byte[32];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');   // URL-safe Base64
        }


        /// <summary>Maps a Project document to the full ProjectResponse DTO.</summary>
        private static ProjectResponse MapToProjectResponse(Project project) => new()
        {
            Id = project.Id.ToString(),
            Name = project.Name,
            Description = project.Description,
            OwnerId = project.OwnerId.ToString(),
            Status = project.Status.ToString(),
            ProjectStartDate = project.ProjectStartDate,
            ProjectDueDate = project.ProjectDueDate,
            Priority = project.ProjectPriority.ToString(),
            Tags = project.Tags,
            Members = project.Members
                .OrderBy(m => m.JoinedAt)
                .Select(m => new MemberResponse
                {
                    UserId = m.UserId.ToString(),
                    Email = m.Email,
                    FullName = m.FullName,
                    Role = m.Role.ToString(),
                    JoinedAt = m.JoinedAt,
                    AddedBy = m.AddedBy.ToString()
                }).ToList(),
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt
        };



        // Response builder shortcuts (mirrors your auth service style)
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

        public static bool TryParseEnum<T>(string value, out T result) where T : struct
        {
            return Enum.TryParse(value, true, out result) && Enum.IsDefined(typeof(T), result);
        }
    }
}
