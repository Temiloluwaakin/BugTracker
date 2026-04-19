using BugTracker.Data;
using BugTracker.Data.Models;
using BugTracker.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace BugTracker.API.Hubs
{
    /// <summary>
    /// SignalR Hub for real-time project group chat.
    ///
    /// HOW IT WORKS:
    /// 1. Client connects to /hubs/chat (with their JWT in the query string or header)
    /// 2. Client calls JoinRoom(projectId) → server adds them to a SignalR group for that project
    /// 3. Client calls SendMessage(projectId, content, replyToId?) → server saves to DB and broadcasts
    /// 4. All clients in that group receive ReceiveMessage(message) in real time
    /// 5. Client calls LeaveRoom(projectId) when they navigate away
    ///
    /// GROUP NAMING: SignalR groups are named "project_{projectId}" to avoid collisions.
    /// </summary>
    [Authorize]
    public class DMHub : Hub
    {
        private readonly IDmService _dmService;
        private readonly ILogger<DMHub> _logger;

        public DMHub(IDmService dmService, ILogger<DMHub> logger)
        {
            _dmService = dmService;
            _logger = logger;
        }

        private string? GetCurrentUserId() =>
            Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);

        /// <summary>
        /// Group for a DM conversation
        /// </summary>
        private static string DmGroup(string conversationId) => $"dm_{conversationId}";

        /// <summary>
        /// Optional: user-level group (multi-device support)
        /// </summary>
        private static string UserGroup(string userId) => $"user_{userId}";

        // =============================
        // CONNECTION LIFECYCLE
        // =============================

        public override async Task OnConnectedAsync()
        {
            var userId = GetCurrentUserId();

            if (userId is not null)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
            }

            _logger.LogInformation("DM Hub: user {UserId} connected. ConnectionId: {ConnectionId}.",
                userId, Context.ConnectionId);

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = GetCurrentUserId();

            _logger.LogInformation("DM Hub: user {UserId} disconnected. ConnectionId: {ConnectionId}.",
                userId, Context.ConnectionId);

            if (exception is not null)
            {
                _logger.LogWarning(exception, "DM Hub disconnection error for user {UserId}.", userId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        // =============================
        // JOIN / LEAVE CONVERSATION
        // =============================

        public async Task JoinConversation(string conversationId)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
            {
                await Clients.Caller.SendAsync("Error", "User identity could not be resolved.");
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, DmGroup(conversationId));

            _logger.LogInformation("User {UserId} joined DM conversation {ConversationId}.",
                userId, conversationId);

            await Clients.Caller.SendAsync("JoinedConversation", conversationId);
        }

        public async Task LeaveConversation(string conversationId)
        {
            var userId = GetCurrentUserId();

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, DmGroup(conversationId));

            _logger.LogInformation("User {UserId} left DM conversation {ConversationId}.",
                userId, conversationId);
        }

        // =============================
        // SEND MESSAGE
        // =============================

        public async Task SendDm(string otherUserId, string content, string? replyToId = null)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
            {
                await Clients.Caller.SendAsync("Error", "User identity could not be resolved.");
                return;
            }

            var request = new SendDmRequest
            {
                Content = content,
                ReplyToId = replyToId
            };

            var result = await _dmService.SendMessageAsync(
                userId,
                otherUserId,
                request,
                CancellationToken.None
            );

            if (result.ResponseCode != ResponseCodes.Success.ResponseCode)
            {
                await Clients.Caller.SendAsync("Error", result.ResponseMessage);
                return;
            }

            var conversationId = result.Data.ConversationId;

            // Send to conversation group (if anyone is already there)
            await Clients.Group(DmGroup(conversationId))
                .SendAsync("ReceiveDm", result.Data);

            // ALWAYS send to both users (guaranteed delivery)
            await Clients.Group(UserGroup(userId))
                .SendAsync("ReceiveDm", result.Data);

            await Clients.Group(UserGroup(otherUserId))
                .SendAsync("ReceiveDm", result.Data);
        }

        // =============================
        // EDIT MESSAGE
        // =============================

        public async Task EditMessage(string conversationId, string messageId, string newContent)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
            {
                await Clients.Caller.SendAsync("Error", "User identity could not be resolved.");
                return;
            }

            var request = new EditDmRequest { Content = newContent };

            var result = await _dmService.EditMessageAsync(
                userId,
                conversationId,
                messageId,
                request,
                CancellationToken.None
            );

            if (result.ResponseCode != ResponseCodes.Success.ResponseCode)
            {
                await Clients.Caller.SendAsync("Error", result.ResponseMessage);
                return;
            }

            var broadcast = new
            {
                ConversationId = conversationId,
                MessageId = messageId,
                NewContent = newContent,
                EditedAt = result.Data!.EditedAt!.Value
            };

            await Clients.Group(DmGroup(conversationId))
                .SendAsync("DmMessageEdited", broadcast);
        }

        // =============================
        // DELETE MESSAGE
        // =============================

        public async Task DeleteMessage(string conversationId, string messageId)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
            {
                await Clients.Caller.SendAsync("Error", "User identity could not be resolved.");
                return;
            }

            var result = await _dmService.DeleteMessageAsync(
                userId,
                conversationId,
                messageId,
                CancellationToken.None
            );

            if (result.ResponseCode != ResponseCodes.Success.ResponseCode)
            {
                await Clients.Caller.SendAsync("Error", result.ResponseMessage);
                return;
            }

            var broadcast = new
            {
                ConversationId = conversationId,
                MessageId = messageId
            };

            await Clients.Group(DmGroup(conversationId))
                .SendAsync("DmMessageDeleted", broadcast);
        }

        // =============================
        // TYPING INDICATOR
        // =============================

        public async Task Typing(string conversationId, string userName)
        {
            await Clients.OthersInGroup(DmGroup(conversationId))
                .SendAsync("DmTyping", new { conversationId, userName });
        }
    }
}
