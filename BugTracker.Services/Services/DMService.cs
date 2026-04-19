using BugTracker.Data;
using BugTracker.Data.Context;
using BugTracker.Data.Entities;
using BugTracker.Data.Models;
using BugTracker.Services.Helpers;
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;

namespace BugTracker.Services.Services
{
    public interface IDmService
    {
        //Task<ApiResponse<DmConversationResponse>> GetOrCreateConversationAsync(string actorUserId, string otherUserId, CancellationToken token);

        Task<ApiResponse<List<DmConversationResponse>>> GetMyConversationsAsync(string actorUserId, CancellationToken token);

        Task<ApiResponse<PagedDmResponse>> GetMessagesAsync(string actorUserId, string conversationId, string? before, int limit, CancellationToken token);

        /// <summary>Send a DM. Called by the hub — hub handles real-time broadcast.</summary>
        Task<ApiResponse<DirectMessageResponse>> SendMessageAsync(string actorUserId, string otherUserId, SendDmRequest request, CancellationToken token);

        /// <summary>Edit a DM. Only the sender can edit.</summary>
        Task<ApiResponse<DirectMessageResponse>> EditMessageAsync(string actorUserId, string conversationId, string messageId, EditDmRequest request, CancellationToken token);

        /// <summary>Soft-delete a DM. Only the sender can delete.</summary>
        Task<ApiResponse<string>> DeleteMessageAsync(string actorUserId, string conversationId, string messageId, CancellationToken token);
    }


    public class DmService : IDmService
    {
        private readonly DatabaseContext _db;
        private readonly IResponseHelper _responseHelper;

        private const int SnippetMaxLength = 100;

        public DmService(DatabaseContext db,
            IResponseHelper responseHelper
            )
        {
            _db = db;
            _responseHelper = responseHelper;
        }

        /// <summary>
        /// GET OR CREATE CONVERSATION
        /// If a conversation already exists between these
        /// two users, return it. Otherwise create a new one.
        /// This is idempotent — safe to call multiple times.
        /// </summary>
        /// <param name="actorUserId"></param>
        /// <param name="otherUserId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<ApiResponse<DmConversationResponse>> GetOrCreateConversationAsync(
            string actorUserId,
            string otherUserId,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return _responseHelper.Fail<DmConversationResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                if (!ObjectId.TryParse(otherUserId, out var otherObjId))
                    return _responseHelper.Fail<DmConversationResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid target user ID format.");

                if (actorObjId == otherObjId)
                    return _responseHelper.Fail<DmConversationResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "You cannot start a conversation with yourself.");

                // Verify both users exist
                var usersTask = _db.Users.Find(u => u.Id == actorUserId || u.Id == otherUserId).ToListAsync(token);
                var users = await usersTask;
                var actorUser = users.FirstOrDefault(u => u.Id == actorUserId);
                var otherUser = users.FirstOrDefault(u => u.Id == otherUserId);

                if (actorUser is null)
                    return _responseHelper.Fail<DmConversationResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Your user account was not found.");

                if (otherUser is null)
                    return _responseHelper.Fail<DmConversationResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "The specified user was not found.");

                // Sort IDs so the pair is always stored in a consistent order
                var sortedIds = new List<ObjectId> { actorObjId, otherObjId }
                    .OrderBy(id => id.ToString())
                    .ToList();

                // Check if conversation already exists
                var existing = await _db.DmConversations
                    .Find(c => c.ParticipantIds == sortedIds)
                    .FirstOrDefaultAsync(token);

                if (existing is not null)
                    return Ok(MapToConversationResponse(existing, actorObjId));

                // Create new conversation
                var conversation = new DmConversation
                {
                    ParticipantIds = sortedIds,
                    Participants = new List<DmParticipant>
                {
                    new() { UserId = actorObjId, FullName = actorUser.FullName, Email = actorUser.Email },
                    new() { UserId = otherObjId, FullName = otherUser.FullName, Email = otherUser.Email }
                },
                    CreatedAt = DateTime.UtcNow
                };

                await _db.DmConversations.InsertOneAsync(conversation, cancellationToken: token);

                Log.Information("DM conversation created between {UserA} and {UserB}.", actorUserId, otherUserId);

                return Ok(MapToConversationResponse(conversation, actorObjId));
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                // Race condition — two simultaneous requests created the same conversation.
                // Just fetch and return the existing one.
                if (!ObjectId.TryParse(actorUserId, out var actorObjId) ||
                    !ObjectId.TryParse(otherUserId, out var otherObjId))
                    return SystemError<DmConversationResponse>();

                var sortedIds = new List<ObjectId> { actorObjId, otherObjId }
                    .OrderBy(id => id.ToString()).ToList();

                var conv = await _db.DmConversations
                    .Find(c => c.ParticipantIds == sortedIds)
                    .FirstOrDefaultAsync(token);

                return Ok(MapToConversationResponse(conv!, actorObjId));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error creating/fetching DM conversation.");
                return SystemError<DmConversationResponse>();
            }
        }


        /// <summary>
        /// Get all conversations for the caller, sorted by last activity.
        /// this is used in the dm page to get all the conversation the logged in user is part of
        /// </summary>
        /// <param name="actorUserId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<ApiResponse<List<DmConversationResponse>>> GetMyConversationsAsync(
            string actorUserId,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return _responseHelper.Fail<List<DmConversationResponse>>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                var conversations = await _db.DmConversations
                    .Find(c => c.ParticipantIds.Contains(actorObjId))
                    .SortByDescending(c => c.LastMessageAt)
                    .ToListAsync(token);

                var response = conversations
                    .Select(c => MapToConversationResponse(c, actorObjId))
                    .ToList();

                return Ok(response);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching conversations for user {UserId}.", actorUserId);
                return SystemError<List<DmConversationResponse>>();
            }
        }

        
        /// <summary>
        /// Load message history for a conversation. Cursor-based pagination.
        /// </summary>
        /// <param name="actorUserId"></param>
        /// <param name="conversationId"></param>
        /// <param name="before"></param>
        /// <param name="limit"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<ApiResponse<PagedDmResponse>> GetMessagesAsync(
            string actorUserId,
            string conversationId,
            string? before,
            int limit,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return _responseHelper.Fail<PagedDmResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                if (!ObjectId.TryParse(conversationId, out var convObjId))
                    return _responseHelper.Fail<PagedDmResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid conversation ID format.");

                // Verify caller is a participant
                var conversation = await _db.DmConversations
                    .Find(c => c.Id == convObjId)
                    .FirstOrDefaultAsync(token);

                if (conversation is null)
                    return _responseHelper.Fail<PagedDmResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Conversation not found.");

                if (!conversation.ParticipantIds.Contains(actorObjId))
                    return _responseHelper.Fail<PagedDmResponse>(ResponseCodes.UnAuthorized.ResponseCode, "You are not a participant in this conversation.");

                limit = Math.Clamp(limit, 1, 100);

                var fb = Builders<DirectMessage>.Filter;
                var filters = new List<FilterDefinition<DirectMessage>>
            {
                fb.Eq(m => m.ConversationId, convObjId)
            };

                if (!string.IsNullOrWhiteSpace(before))
                {
                    if (!ObjectId.TryParse(before, out var cursorObjId))
                        return _responseHelper.Fail<PagedDmResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid cursor format.");

                    var cursorMsg = await _db.DirectMessages
                        .Find(m => m.Id == cursorObjId)
                        .FirstOrDefaultAsync(token);

                    if (cursorMsg is not null)
                        filters.Add(fb.Lt(m => m.SentAt, cursorMsg.SentAt));
                }

                var combined = fb.And(filters);
                var totalCount = (int)await _db.DirectMessages.CountDocumentsAsync(combined, cancellationToken: token);

                var messages = await _db.DirectMessages
                    .Find(combined)
                    .SortByDescending(m => m.SentAt)
                    .Limit(limit)
                    .ToListAsync(token);

                messages.Reverse(); // oldest → newest for display

                var olderCursor = messages.Count == limit ? messages.First().Id.ToString() : null;

                return Ok(new PagedDmResponse
                {
                    Messages = messages.Select(m => MapToMessageResponse(m, actorObjId)).ToList(),
                    TotalCount = totalCount,
                    OlderCursor = olderCursor
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching DM messages for conversation {ConversationId}.", conversationId);
                return SystemError<PagedDmResponse>();
            }
        }

        
        /// <summary>
        /// called by signal r. hub handles real - time broadcast.
        /// when a user clicks on send message it first checks if there is a conversation if not it creates one and then sends the message
        /// </summary>
        /// <param name="actorUserId"></param>
        /// <param name="conversationId"></param>
        /// <param name="request"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<ApiResponse<DirectMessageResponse>> SendMessageAsync(
            string actorUserId,
            //string conversationId,
            string otherUserId,
            SendDmRequest request,
            CancellationToken token)
        {
            try
            {
                //if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                //    return _responseHelper.Fail<DirectMessageResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                //if (!ObjectId.TryParse(conversationId, out var convObjId))
                //    return _responseHelper.Fail<DirectMessageResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid conversation ID format.");

                //var conversation = await _db.DmConversations
                //    .Find(c => c.Id == convObjId)
                //    .FirstOrDefaultAsync(token);

                //if (conversation is null)
                //    return _responseHelper.Fail<DirectMessageResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Conversation not found.");

                //if (!conversation.ParticipantIds.Contains(actorObjId))
                //    return _responseHelper.Fail<DirectMessageResponse>(ResponseCodes.UnAuthorized.ResponseCode, "You are not a participant in this conversation.");

                var conversation = await GetOrCreateConversationAsync(actorUserId, otherUserId, token);

                if (conversation.ResponseCode != ResponseCodes.Success.ResponseCode)
                    return _responseHelper.Fail<DirectMessageResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Conversation not found or failed to create one");

                var actorObjId = ObjectId.Parse(actorUserId);
                var now = DateTime.UtcNow;

                ObjectId? replyToObjId = null;
                string? replySnippet = null;

                if (!string.IsNullOrWhiteSpace(request.ReplyToId))
                {
                    if (!ObjectId.TryParse(request.ReplyToId, out var replyObjId))
                        return _responseHelper.Fail<DirectMessageResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid ReplyToId format.");

                    var parentMessage = await _db.DirectMessages
                        .Find(m => m.Id == replyObjId && m.ConversationId == ObjectId.Parse(conversation.Data.Id))
                        .FirstOrDefaultAsync(token);

                    if (parentMessage is null)
                        return _responseHelper.Fail<DirectMessageResponse>(ResponseCodes.NoRecordReturned.ResponseCode,
                            "The message you are replying to was not found.");

                    if (parentMessage.IsDeleted)
                        return _responseHelper.Fail<DirectMessageResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode,
                            "You cannot reply to a deleted message.");

                    replyToObjId = replyObjId;
                    // Truncate snippet for the quote preview
                    replySnippet = parentMessage.Content.Length > SnippetMaxLength
                        ? parentMessage.Content[..SnippetMaxLength] + "..."
                        : parentMessage.Content;
                }

                var message = new DirectMessage
                {
                    ConversationId = ObjectId.Parse(conversation.Data.Id),
                    SenderId = actorObjId,
                    SenderName = conversation.Data.Sender.FullName,
                    Content = request.Content.Trim(),
                    ReplyToId = replyToObjId,
                    ReplyToSnippet = replySnippet,
                    SentAt = now
                };

                await _db.DirectMessages.InsertOneAsync(message, cancellationToken: token);

                // Update the conversation's last message preview
                var snippet = message.Content.Length > 60
                    ? message.Content[..60] + "..."
                    : message.Content;

                var convUpdate = Builders<DmConversation>.Update.Combine(
                    Builders<DmConversation>.Update.Set(c => c.LastMessageSnippet, snippet),
                    Builders<DmConversation>.Update.Set(c => c.LastMessageAt, now),
                    Builders<DmConversation>.Update.Set(c => c.LastMessageSenderId, actorObjId)
                );

                await _db.DmConversations.UpdateOneAsync(c => c.Id == ObjectId.Parse(conversation.Data.Id), convUpdate, cancellationToken: token);

                return Ok(MapToMessageResponse(message, actorObjId));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error sending DM");
                return SystemError<DirectMessageResponse>();
            }
        }

        // ═══════════════════════════════════════════
        // EDIT MESSAGE
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<DirectMessageResponse>> EditMessageAsync(
            string actorUserId,
            string conversationId,
            string messageId,
            EditDmRequest request,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return _responseHelper.Fail<DirectMessageResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                if (!ObjectId.TryParse(conversationId, out var convObjId))
                    return _responseHelper.Fail<DirectMessageResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid conversation ID format.");

                if (!ObjectId.TryParse(messageId, out var msgObjId))
                    return _responseHelper.Fail<DirectMessageResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid message ID format.");

                var message = await _db.DirectMessages
                    .Find(m => m.Id == msgObjId && m.ConversationId == convObjId)
                    .FirstOrDefaultAsync(token);

                if (message is null)
                    return _responseHelper.Fail<DirectMessageResponse>(ResponseCodes.NoRecordReturned.ResponseCode, "Message not found.");

                if (message.SenderId != actorObjId)
                    return _responseHelper.Fail<DirectMessageResponse>(ResponseCodes.UnAuthorized.ResponseCode, "You can only edit your own messages.");

                if (message.IsDeleted)
                    return _responseHelper.Fail<DirectMessageResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Cannot edit a deleted message.");

                var now = DateTime.UtcNow;
                var newContent = request.Content.Trim();

                if (message.Content == newContent)
                    return _responseHelper.Fail<DirectMessageResponse>(ResponseCodes.InvalidEntryDetected.ResponseCode, "No changes detected.");

                var update = Builders<DirectMessage>.Update.Combine(
                    Builders<DirectMessage>.Update.Set(m => m.Content, newContent),
                    Builders<DirectMessage>.Update.Set(m => m.IsEdited, true),
                    Builders<DirectMessage>.Update.Set(m => m.EditedAt, now)
                );

                await _db.DirectMessages.UpdateOneAsync(m => m.Id == msgObjId, update, cancellationToken: token);

                message.Content = newContent;
                message.IsEdited = true;
                message.EditedAt = now;

                return Ok(MapToMessageResponse(message, actorObjId));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error editing DM {MessageId}.", messageId);
                return SystemError<DirectMessageResponse>();
            }
        }

        // ═══════════════════════════════════════════
        // DELETE MESSAGE (soft delete)
        // ═══════════════════════════════════════════
        public async Task<ApiResponse<string>> DeleteMessageAsync(
            string actorUserId,
            string conversationId,
            string messageId,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return _responseHelper.Fail<string>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid user ID format.");

                if (!ObjectId.TryParse(conversationId, out var convObjId))
                    return _responseHelper.Fail<string>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid conversation ID format.");

                if (!ObjectId.TryParse(messageId, out var msgObjId))
                    return _responseHelper.Fail<string>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Invalid message ID format.");

                var message = await _db.DirectMessages
                    .Find(m => m.Id == msgObjId && m.ConversationId == convObjId)
                    .FirstOrDefaultAsync(token);

                if (message is null)
                    return _responseHelper.Fail<string>(ResponseCodes.NoRecordReturned.ResponseCode, "Message not found.");

                if (message.SenderId != actorObjId)
                    return _responseHelper.Fail<string>(ResponseCodes.UnAuthorized.ResponseCode, "You can only delete your own messages.");

                if (message.IsDeleted)
                    return _responseHelper.Fail<string>(ResponseCodes.InvalidEntryDetected.ResponseCode, "Message is already deleted.");

                var update = Builders<DirectMessage>.Update.Combine(
                    Builders<DirectMessage>.Update.Set(m => m.IsDeleted, true),
                    Builders<DirectMessage>.Update.Set(m => m.Content, string.Empty)
                );

                await _db.DirectMessages.UpdateOneAsync(m => m.Id == msgObjId, update, cancellationToken: token);

                return Ok(messageId, "Message deleted.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error deleting DM {MessageId}.", messageId);
                return SystemError<string>();
            }
        }

        // ═══════════════════════════════════════════
        // PRIVATE HELPERS
        // ═══════════════════════════════════════════

        private static DmConversationResponse MapToConversationResponse(DmConversation c, ObjectId viewerObjId)
        {
            var other = c.Participants.FirstOrDefault(p => p.UserId != viewerObjId)
                     ?? c.Participants.First(); // fallback — shouldn't happen

            var sender = c.Participants.FirstOrDefault(p => p.UserId == viewerObjId) ?? c.Participants.First();

            return new DmConversationResponse
            {
                Id = c.Id.ToString(),
                OtherParticipant = new DmParticipantResponse
                {
                    UserId = other.UserId.ToString(),
                    FullName = other.FullName,
                    Email = other.Email
                },
                Sender = new DmParticipantResponse
                {
                    UserId = sender.UserId.ToString(),
                    FullName = sender.FullName,
                    Email = sender.Email
                },
                LastMessageSnippet = c.LastMessageSnippet,
                LastMessageAt = c.LastMessageAt,
                LastMessageIsMine = c.LastMessageSenderId == viewerObjId
            };
        }

        private static DirectMessageResponse MapToMessageResponse(DirectMessage m, ObjectId viewerObjId) => new()
        {
            Id = m.Id.ToString(),
            ConversationId = m.ConversationId.ToString(),
            Sender = new DmParticipantResponse
            {
                UserId = m.SenderId.ToString(),
                FullName = m.SenderName,
                Email = string.Empty  // not stored on message — fetch from profile if needed
            },
            Content = m.IsDeleted ? string.Empty : m.Content,
            IsEdited = m.IsEdited,
            IsDeleted = m.IsDeleted,
            ReplyTo = m.ReplyToId.HasValue ? new ReplyToResponse
            {
                MessageId = m.ReplyToId.ToString()!,
                Snippet = m.ReplyToSnippet ?? string.Empty
            } : null,
            IsMine = m.SenderId == viewerObjId,
            SentAt = m.SentAt,
            EditedAt = m.EditedAt
        };

        private static ApiResponse<T> Ok<T>(T? data, string message = "Success") => new()
        {
            ResponseCode = ResponseCodes.Success.ResponseCode,
            ResponseMessage = message,
            Data = data
        };

        private static ApiResponse<T> SystemError<T>() => new()
        {
            ResponseCode = ResponseCodes.SystemMalfunction.ResponseCode,
            ResponseMessage = "An unexpected error occurred. Please try again later."
        };
    }
}
