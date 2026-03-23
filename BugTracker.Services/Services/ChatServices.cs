using BugTracker.Data;
using BugTracker.Data.Context;
using BugTracker.Data.Entities;
using BugTracker.Data.Models;
using BugTracker.Services.Helpers;
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
    public interface IChatService
    {
        /// <summary>Returns all project chat rooms the user is a member of, sorted by last activity.</summary>
        Task<ApiResponse<List<ChatRoomSummaryResponse>>> GetMyRoomsAsync(string actorUserId, CancellationToken token);

        /// <summary>
        /// Load message history for a project chat room.
        /// Uses cursor-based pagination — pass null for initial load,
        /// pass oldest visible message ID to load more history.
        /// </summary>
        Task<ApiResponse<List<ChatMessageResponse>>> GetMessagesAsync(string actorUserId, string projectId, GetMessagesQuery query, CancellationToken token);

        /// <summary>
        /// Persist a new message and return it.
        /// Called by the SignalR hub — the hub handles broadcasting.
        /// </summary>
        Task<ApiResponse<ChatMessageResponse>> SendMessageAsync(string actorUserId, string projectId, SendMessageRequest request, CancellationToken token);

        /// <summary>
        /// Edit an existing message. Only the sender can edit.
        /// Returns the updated message for the hub to broadcast.
        /// </summary>
        Task<ApiResponse<ChatMessageResponse>> EditMessageAsync(string actorUserId, string projectId, string messageId, EditMessageRequest request, CancellationToken token);

        /// <summary>
        /// Soft-delete a message. Sender or project owner can delete.
        /// Returns the message ID for the hub to broadcast the deletion.
        /// </summary>
        Task<ApiResponse<string>> DeleteMessageAsync(string actorUserId, string projectId, string messageId, CancellationToken token);
    }


    public class ChatServices : IChatService
    {
        private readonly DatabaseContext _db;
        private readonly IResponseHelper _responseHelper;

        // Max snippet length stored for reply previews
        private const int SnippetMaxLength = 100;

        public ChatServices(
            DatabaseContext db,
            IResponseHelper responseHelper
            )
        {
            _db = db;
            _responseHelper = responseHelper;
        }

        // ═══════════════════════════════════════════
        // GET MY ROOMS
        // Returns one chat room entry per project the
        // user belongs to, sorted by last activity.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<List<ChatRoomSummaryResponse>>> GetMyRoomsAsync(
            string actorUserId,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return _responseHelper.Fail<List<ChatRoomSummaryResponse>>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                // 1. Get all projects the user is a member of
                var projects = await _db.Projects
                    .Find(p => p.Members.Any(m => m.UserId == actorUserId))
                    .SortByDescending(p => p.CreatedAt)
                    .ToListAsync(token);

                if (projects.Count == 0)
                    return _responseHelper.Ok(new List<ChatRoomSummaryResponse>());

                // 2. For each project, fetch the most recent message in parallel
                var projectIds = projects.Select(p => p.Id).ToList();

                // Fetch last message per project — one query per project, run in parallel
                var lastMessageTasks = projectIds.Select(pid =>
                    _db.ChatMessages
                        .Find(m => m.ProjectId == ObjectId.Parse(pid))
                        .SortByDescending(m => m.SentAt)
                        .Limit(1)
                        .FirstOrDefaultAsync(token)
                ).ToList();

                await Task.WhenAll(lastMessageTasks);

                // 3. Build response — sorted by last activity (most recent chat first)
                var rooms = projects.Select((project, index) =>
                {
                    var lastMessage = lastMessageTasks[index].Result;
                    var myMember = project.Members.First(m => m.UserId == actorUserId);

                    return new ChatRoomSummaryResponse
                    {
                        ProjectId = project.Id.ToString(),
                        ProjectName = project.Name,
                        ProjectStatus = project.Status.ToString(),
                        MemberCount = project.Members.Count,
                        LastMessage = lastMessage is null ? null : MapToMessageResponse(lastMessage, actorObjId),
                        LastActivityAt = lastMessage?.SentAt ?? project.CreatedAt
                    };
                })
                .OrderByDescending(r => r.LastActivityAt)
                .ToList();

                return _responseHelper.Ok(rooms);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching chat rooms for user {UserId}.", actorUserId);
                return _responseHelper.SystemError<List<ChatRoomSummaryResponse>>();
            }
        }


        // ═══════════════════════════════════════════
        // GET MESSAGES (cursor-based pagination)
        // Initial load: pass Before = null → returns latest N messages
        // Load more:    pass Before = oldestVisibleMessageId → returns N messages before that
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<List<ChatMessageResponse>>> GetMessagesAsync(
            string actorUserId,
            string projectId,
            GetMessagesQuery query,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return _responseHelper.Fail<List<ChatMessageResponse>>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                if (!ObjectId.TryParse(projectId, out var projectObjId))
                    return _responseHelper.Fail<List<ChatMessageResponse>>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid project ID format.");

                // 1. Verify project exists and user is a member
                var project = await _db.Projects.Find(p => p.Id == projectId).FirstOrDefaultAsync(token);
                if (project is null)
                    return _responseHelper.Fail<List<ChatMessageResponse>>(ResponseCodes.NoRecordReturned.ResponseCode, "Project not found.");

                if (!project.Members.Any(m => m.UserId == actorUserId))
                    return _responseHelper.Fail<List<ChatMessageResponse>>(ResponseCodes.UnAuthorized.ResponseCode, "You are not a member of this project.");

                // 2. Clamp limit
                var limit = Math.Clamp(query.Limit, 1, 100);

                // 3. Build filter — scope to project, apply cursor if provided
                var fb = Builders<ChatMessage>.Filter;
                var filters = new List<FilterDefinition<ChatMessage>>
                {
                    fb.Eq(m => m.ProjectId, projectObjId)
                };

                if (!string.IsNullOrWhiteSpace(query.Before))
                {
                    if (!ObjectId.TryParse(query.Before, out var cursorObjId))
                        return _responseHelper.Fail<List<ChatMessageResponse>>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid cursor (Before) format.");

                    // Fetch the sentAt of the cursor message so we can do a time-based range query
                    // This is more reliable than comparing ObjectIds for pagination
                    var cursorMessage = await _db.ChatMessages
                        .Find(m => m.Id == cursorObjId && m.ProjectId == projectObjId)
                        .FirstOrDefaultAsync(token);

                    if (cursorMessage is null)
                        return _responseHelper.Fail<List<ChatMessageResponse>>(ResponseCodes.NoRecordReturned.ResponseCode,
                            "Cursor message not found. It may have been deleted.");

                    filters.Add(fb.Lt(m => m.SentAt, cursorMessage.SentAt));
                }

                var combined = fb.And(filters);

                // 4. Fetch — sorted descending (newest first) then reverse for display order
                var messages = await _db.ChatMessages
                    .Find(combined)
                    .SortByDescending(m => m.SentAt)
                    .Limit(limit)
                    .ToListAsync(token);

                // Reverse so messages are returned oldest → newest (natural chat order)
                messages.Reverse();

                var response = messages.Select(m => MapToMessageResponse(m, actorObjId)).ToList();
                return _responseHelper.Ok(response);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching messages for project {ProjectId}.", projectId);
                return _responseHelper.SystemError<List<ChatMessageResponse>>();
            }
        }

        // ═══════════════════════════════════════════
        // SEND MESSAGE
        // Called by the SignalR hub after JWT validation.
        // Persists the message and returns it.
        // The hub is responsible for broadcasting to the room.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<ChatMessageResponse>> SendMessageAsync(
            string actorUserId,
            string projectId,
            SendMessageRequest request,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return _responseHelper.Fail<ChatMessageResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                if (!ObjectId.TryParse(projectId, out var projectObjId))
                    return _responseHelper.Fail<ChatMessageResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid project ID format.");

                // 1. Verify project + membership
                var project = await _db.Projects.Find(p => p.Id == projectId).FirstOrDefaultAsync(token);
                if (project is null)
                    return _responseHelper.Fail<ChatMessageResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Project not found.");

                var callerMember = project.Members.FirstOrDefault(m => m.UserId == actorUserId);
                if (callerMember is null)
                    return _responseHelper.Fail<ChatMessageResponse>(ResponseCodes.UnAuthorized.ResponseCode, "You are not a member of this project.");

                // 2. Cannot send messages in an archived project
                if (project.Status == ProjectStatus.Archived)
                    return _responseHelper.Fail<ChatMessageResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        "This project is archived. No new messages can be sent.");

                // 3. Resolve optional reply
                ObjectId? replyToObjId = null;
                string? replySnippet = null;

                if (!string.IsNullOrWhiteSpace(request.ReplyToId))
                {
                    if (!ObjectId.TryParse(request.ReplyToId, out var replyObjId))
                        return _responseHelper.Fail<ChatMessageResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid ReplyToId format.");

                    var parentMessage = await _db.ChatMessages
                        .Find(m => m.Id == replyObjId && m.ProjectId == projectObjId)
                        .FirstOrDefaultAsync(token);

                    if (parentMessage is null)
                        return _responseHelper.Fail<ChatMessageResponse>(ResponseCodes.NoRecordReturned.ResponseCode,
                            "The message you are replying to was not found.");

                    if (parentMessage.IsDeleted)
                        return _responseHelper.Fail<ChatMessageResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                            "You cannot reply to a deleted message.");

                    replyToObjId = replyObjId;
                    // Truncate snippet for the quote preview
                    replySnippet = parentMessage.Content.Length > SnippetMaxLength
                        ? parentMessage.Content[..SnippetMaxLength] + "..."
                        : parentMessage.Content;
                }

                // 4. Build and insert the message
                var message = new ChatMessage
                {
                    ProjectId = projectObjId,
                    SenderId = actorObjId,
                    SenderName = callerMember.FullName,
                    SenderEmail = callerMember.Email,
                    Content = request.Content.Trim(),
                    IsEdited = false,
                    IsDeleted = false,
                    ReplyToId = replyToObjId,
                    ReplyToSnippet = replySnippet,
                    SentAt = DateTime.UtcNow
                };

                await _db.ChatMessages.InsertOneAsync(message, cancellationToken: token);

                Log.Information("Message sent in project {ProjectId} by user {UserId}.", projectId, actorUserId);

                return _responseHelper.Ok(MapToMessageResponse(message, actorObjId));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error sending message in project {ProjectId} by user {UserId}.", projectId, actorUserId);
                return _responseHelper.SystemError<ChatMessageResponse>();
            }
        }

        // ═══════════════════════════════════════════
        // EDIT MESSAGE
        // Only the original sender can edit.
        // Deleted messages cannot be edited.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<ChatMessageResponse>> EditMessageAsync(
            string actorUserId,
            string projectId,
            string messageId,
            EditMessageRequest request,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return _responseHelper.Fail<ChatMessageResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                if (!ObjectId.TryParse(projectId, out var projectObjId))
                    return _responseHelper.Fail<ChatMessageResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid project ID format.");

                if (!ObjectId.TryParse(messageId, out var messageObjId))
                    return _responseHelper.Fail<ChatMessageResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid message ID format.");

                // 1. Verify membership
                var project = await _db.Projects.Find(p => p.Id == projectId).FirstOrDefaultAsync(token);
                if (project is null)
                    return _responseHelper.Fail<ChatMessageResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Project not found.");

                if (!project.Members.Any(m => m.UserId == actorUserId))
                    return _responseHelper.Fail<ChatMessageResponse>(ResponseCodes.UnAuthorized.ResponseCode, "You are not a member of this project.");

                // 2. Fetch the message
                var message = await _db.ChatMessages
                    .Find(m => m.Id == messageObjId && m.ProjectId == projectObjId)
                    .FirstOrDefaultAsync(token);

                if (message is null)
                    return _responseHelper.Fail<ChatMessageResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Message not found.");

                // 3. Only the sender can edit their own message
                if (message.SenderId != actorObjId)
                    return _responseHelper.Fail<ChatMessageResponse>(ResponseCodes.UnAuthorized.ResponseCode,
                        "You can only edit your own messages.");

                // 4. Cannot edit a deleted message
                if (message.IsDeleted)
                    return _responseHelper.Fail<ChatMessageResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        "This message has been deleted and cannot be edited.");

                // 5. No-op guard
                var newContent = request.Content.Trim();
                if (message.Content == newContent)
                    return _responseHelper.Fail<ChatMessageResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                        "The new content is the same as the current message.");

                var now = DateTime.UtcNow;
                var update = Builders<ChatMessage>.Update.Combine(
                    Builders<ChatMessage>.Update.Set(m => m.Content, newContent),
                    Builders<ChatMessage>.Update.Set(m => m.IsEdited, true),
                    Builders<ChatMessage>.Update.Set(m => m.EditedAt, now)
                );

                await _db.ChatMessages.UpdateOneAsync(m => m.Id == messageObjId, update, cancellationToken: token);

                // Return updated message
                message.Content = newContent;
                message.IsEdited = true;
                message.EditedAt = now;

                return _responseHelper.Ok(MapToMessageResponse(message, actorObjId));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error editing message {MessageId} by user {UserId}.", messageId, actorUserId);
                return _responseHelper.SystemError<ChatMessageResponse>();
            }
        }

        // ═══════════════════════════════════════════
        // DELETE MESSAGE (soft delete)
        // Sender or project owner can delete.
        // Content is blanked out — "This message was deleted."
        // is shown by the UI based on IsDeleted = true.
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<string>> DeleteMessageAsync(
            string actorUserId,
            string projectId,
            string messageId,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return _responseHelper.Fail<string>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                if (!ObjectId.TryParse(projectId, out var projectObjId))
                    return _responseHelper.Fail<string>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid project ID format.");

                if (!ObjectId.TryParse(messageId, out var messageObjId))
                    return _responseHelper.Fail<string>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid message ID format.");

                var project = await _db.Projects.Find(p => p.Id == projectId).FirstOrDefaultAsync(token);
                if (project is null)
                    return _responseHelper.Fail<string>(ResponseCodes.NoRecordReturned.ResponseCode, "Project not found.");

                var callerMember = project.Members.FirstOrDefault(m => m.UserId == actorUserId);
                if (callerMember is null)
                    return _responseHelper.Fail<string>(ResponseCodes.UnAuthorized.ResponseCode, "You are not a member of this project.");

                var message = await _db.ChatMessages
                    .Find(m => m.Id == messageObjId && m.ProjectId == projectObjId)
                    .FirstOrDefaultAsync(token);

                if (message is null)
                    return _responseHelper.Fail<string>(ResponseCodes.NoRecordReturned.ResponseCode, "Message not found.");

                // Sender or owner can delete
                var canDelete = message.SenderId == actorObjId || callerMember.Role == ProjectRole.Owner;
                if (!canDelete)
                    return _responseHelper.Fail<string>(ResponseCodes.UnAuthorized.ResponseCode,
                        "You can only delete your own messages.");

                // Already deleted — no-op
                if (message.IsDeleted)
                    return _responseHelper.Fail<string>(ResponseCodes.InvalidEntryDetected.ResponseCode, "This message is already deleted.");

                // Soft delete: blank the content, flip the flag
                var update = Builders<ChatMessage>.Update.Combine(
                    Builders<ChatMessage>.Update.Set(m => m.IsDeleted, true),
                    Builders<ChatMessage>.Update.Set(m => m.Content, string.Empty)
                );

                await _db.ChatMessages.UpdateOneAsync(m => m.Id == messageObjId, update, cancellationToken: token);

                Log.Information("Message {MessageId} soft-deleted by user {UserId}.", messageId, actorUserId);

                return _responseHelper.Ok(messageId, "Message deleted.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error deleting message {MessageId} by user {UserId}.", messageId, actorUserId);
                return _responseHelper.SystemError<string>();
            }
        }

        // ═══════════════════════════════════════════
        // PRIVATE HELPERS
        // ═══════════════════════════════════════════

        private static ChatMessageResponse MapToMessageResponse(ChatMessage message, ObjectId viewerObjId) => new()
        {
            Id = message.Id.ToString(),
            ProjectId = message.ProjectId.ToString(),
            Sender = new ChatSenderResponse
            {
                UserId = message.SenderId.ToString(),
                FullName = message.SenderName,
                Email = message.SenderEmail
            },
            // Never expose deleted message content — blank it out at the service layer too
            Content = message.IsDeleted ? string.Empty : message.Content,
            IsEdited = message.IsEdited,
            IsDeleted = message.IsDeleted,
            ReplyTo = message.ReplyToId.HasValue ? new ReplyToResponse
            {
                MessageId = message.ReplyToId.ToString()!,
                Snippet = message.ReplyToSnippet ?? string.Empty
            } : null,
            SentAt = message.SentAt,
            EditedAt = message.EditedAt,
            IsMine = message.SenderId == viewerObjId
        };

    }
}
