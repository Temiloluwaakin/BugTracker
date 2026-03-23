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
    public class ChatHub : Hub
    {
        private readonly IChatService _chatService;
        private readonly ILogger<ChatHub> _logger;

        public ChatHub(IChatService chatService, ILogger<ChatHub> logger)
        {
            _chatService = chatService;
            _logger = logger;
        }

        private string? GetCurrentUserId() =>
            Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);

        /// <summary>
        /// Converts a projectId into a consistent SignalR group name.
        /// All hub methods use this — never hardcode the group name format.
        /// </summary>
        private static string RoomGroup(string projectId) => $"project_{projectId}";

      

        /// <summary>
        /// JOIN ROOM
        /// Client calls this when they open a project chat.
        /// Adds the connection to the SignalR group for that project
        /// so they receive broadcasts for that room.
        /// </summary>
        /// <param name="projectId"></param>
        /// <returns></returns>
        public async Task JoinRoom(string projectId)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
            {
                await Clients.Caller.SendAsync("Error", "User identity could not be resolved.");
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(projectId));

            _logger.LogInformation("User {UserId} joined chat room for project {ProjectId}.", userId, projectId);

            // Notify the caller they joined successfully
            await Clients.Caller.SendAsync("JoinedRoom", projectId);
        }


        /// <summary>
        /// LEAVE ROOM
        /// Client calls this when they navigate away from the chat.
        /// Not strictly required (SignalR cleans up on disconnect)
        /// but calling it explicitly stops unnecessary broadcasts.
        /// </summary>
        /// <param name="projectId"></param>
        /// <returns></returns>
        public async Task LeaveRoom(string projectId)
        {
            var userId = GetCurrentUserId();

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomGroup(projectId));

            _logger.LogInformation("User {UserId} left chat room for project {ProjectId}.", userId, projectId);
        }

        
        /// <summary>
        /// Client calls this to send a chat message.
        /// Flow:
        ///   1. Service validates, saves to MongoDB, returns the saved message
        ///   2. Hub broadcasts the message to ALL clients in the room group
        ///      (including the sender — so their UI updates from the broadcast,
        ///       not from a local optimistic update)
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="content"></param>
        /// <param name="replyToId"></param>
        /// <returns></returns>
        public async Task SendMessage(string projectId, string content, string? replyToId = null)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
            {
                await Clients.Caller.SendAsync("Error", "User identity could not be resolved.");
                return;
            }

            var request = new SendMessageRequest
            {
                Content = content,
                ReplyToId = replyToId
            };

            var result = await _chatService.SendMessageAsync(userId, projectId, request, CancellationToken.None);

            if (result.ResponseCode != ResponseCodes.Success.ResponseCode)
            {
                // Send error back to the caller only — others in the room don't need to know
                await Clients.Caller.SendAsync("Error", result.ResponseMessage);
                return;
            }

            // Broadcast to everyone in the room (including sender)
            var broadcast = new NewMessageBroadcast
            {
                ProjectId = projectId,
                Message = result.Data!
            };

            await Clients.Group(RoomGroup(projectId)).SendAsync("ReceiveMessage", broadcast);
        }


        /// <summary>
        /// EDIT MESSAGE
        /// Client calls this to edit one of their messages.
        /// Broadcasts the edit to the room so all clients
        /// update that message in their local state.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="messageId"></param>
        /// <param name="newContent"></param>
        /// <returns></returns>
        public async Task EditMessage(string projectId, string messageId, string newContent)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
            {
                await Clients.Caller.SendAsync("Error", "User identity could not be resolved.");
                return;
            }

            var request = new EditMessageRequest { Content = newContent };
            var result = await _chatService.EditMessageAsync(userId, projectId, messageId, request, CancellationToken.None);

            if (result.ResponseCode != ResponseCodes.Success.ResponseCode)
            {
                await Clients.Caller.SendAsync("Error", result.ResponseMessage);
                return;
            }

            var broadcast = new MessageEditedBroadcast
            {
                ProjectId = projectId,
                MessageId = messageId,
                NewContent = newContent,
                EditedAt = result.Data!.EditedAt!.Value
            };

            await Clients.Group(RoomGroup(projectId)).SendAsync("MessageEdited", broadcast);
        }



        /// <summary>
        /// DELETE MESSAGE
        /// Soft-deletes the message and broadcasts the deletion
        /// so all clients replace the content with
        /// "This message was deleted."
        /// 
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="messageId"></param>
        /// <returns></returns>
        public async Task DeleteMessage(string projectId, string messageId)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
            {
                await Clients.Caller.SendAsync("Error", "User identity could not be resolved.");
                return;
            }

            var result = await _chatService.DeleteMessageAsync(userId, projectId, messageId, CancellationToken.None);

            if (result.ResponseCode != ResponseCodes.Success.ResponseCode)
            {
                await Clients.Caller.SendAsync("Error", result.ResponseMessage);
                return;
            }

            var broadcast = new MessageDeletedBroadcast
            {
                ProjectId = projectId,
                MessageId = messageId
            };

            await Clients.Group(RoomGroup(projectId)).SendAsync("MessageDeleted", broadcast);
        }

        

        /// <summary>
        /// TYPING INDICATOR
        /// Lightweight — no DB write.
        /// Broadcasts to the room (excluding the typer)
        /// that a user is currently typing.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="userName"></param>
        /// <returns></returns>
        public async Task UserTyping(string projectId, string userName)
        {
            // Send to everyone in the room EXCEPT the caller
            await Clients.OthersInGroup(RoomGroup(projectId))
                .SendAsync("UserTyping", new { projectId, userName });
        }

       

        /// <summary>
        /// CONNECTION LIFECYCLE
        /// </summary>
        /// <returns></returns>
        public override async Task OnConnectedAsync()
        {
            var userId = GetCurrentUserId();
            _logger.LogInformation("SignalR: user {UserId} connected. ConnectionId: {ConnectionId}.",
                userId, Context.ConnectionId);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = GetCurrentUserId();
            _logger.LogInformation("SignalR: user {UserId} disconnected. ConnectionId: {ConnectionId}.",
                userId, Context.ConnectionId);

            if (exception is not null)
                _logger.LogWarning(exception, "SignalR disconnection error for user {UserId}.", userId);

            await base.OnDisconnectedAsync(exception);
        }
    }
}
