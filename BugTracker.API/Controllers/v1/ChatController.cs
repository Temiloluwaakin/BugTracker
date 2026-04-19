using Asp.Versioning;
using BugTracker.Data.Models;
using BugTracker.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BugTracker.API.Controllers.v1
{
    /// <summary>
    /// REST endpoints for chat.
    /// These handle: loading chat list, loading message history, and
    /// any message actions that don't need real-time (e.g. history load).
    ///
    /// Real-time send/edit/delete goes through the SignalR hub (ChatHub).
    /// The REST endpoints here are for:
    ///   - GET  /api/chat/rooms          → load the user's chat room list
    ///   - GET  /api/projects/{id}/chat  → load message history for a room
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:ApiVersion}/[controller]")]
    [Authorize]
    public class ChatController : Controller
    {
        private readonly IChatService _chatService;
        private readonly IDmService _dmService;

        public ChatController(
            IChatService chatService,
            IDmService dmService
            )
        {
            _chatService = chatService;
            _dmService = dmService;
        }

        private string? GetCurrentUserId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier);

        private IActionResult UnauthorizedResponse() =>
            Unauthorized(new { message = "User identity could not be resolved." });

        
        /// <summary>
        /// GET /api/chat/rooms
        /// Returns all project chat rooms for the logged-in user,
        /// sorted by last activity. This is the chat list screen.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpGet("rooms")]
        public async Task<IActionResult> GetMyRooms(CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _chatService.GetMyRoomsAsync(userId, token);
            return Ok(result);
        }


        /// <summary>
        ///  Load message history for a project chat room.
        ///  Used on initial room open and when scrolling up to load older messages.
        ///  Query params:
        ///  before={messageId}  → load messages older than this ID (pagination cursor)
        ///  limit={n}           → how many to return (default 50, max 100)
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="query"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpGet("projects/{projectId}")]
        public async Task<IActionResult> GetMessages(
            string projectId,
            [FromQuery] GetMessagesQuery query,
            CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();

            var result = await _chatService.GetMessagesAsync(userId, projectId, query, token);
            return Ok(result);
        }









        ///// <summary>
        ///// GET OR CREATE CONVERSATION
        ///// If a conversation already exists between these
        ///// two users, return it. Otherwise create a new one.
        ///// This is idempotent — safe to call multiple times.
        ///// </summary>
        ///// <param name="otherUserId"></param>
        ///// <param name="token"></param>
        ///// <returns></returns>
        //[HttpPost("dm/conversations/{otherUserId}")]
        //public async Task<IActionResult> GetOrCreateConversation(string otherUserId, CancellationToken token)
        //{
        //    var userId = GetCurrentUserId();
        //    if (userId is null) return UnauthorizedResponse();
        //    var result = await _dmService.GetOrCreateConversationAsync(userId, otherUserId, token);
        //    return Ok(result);
        //}


        /// <summary>
        /// Get all conversations for the caller, sorted by last activity.
        /// this is used in the dm page to get all the conversation the logged in user is part of
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpGet("dm/conversations")]
        public async Task<IActionResult> GetMyConversations(CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();
            var result = await _dmService.GetMyConversationsAsync(userId, token);
            return Ok(result);
        }


        /// <summary>
        /// Load message history for a conversation. Cursor-based pagination.
        /// </summary>
        /// <param name="conversationId"></param>
        /// <param name="before"></param>
        /// <param name="limit"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [HttpGet("dm/conversations/{conversationId}")]
        public async Task<IActionResult> GetMessages(
            string conversationId,
            [FromQuery] string? before,
            [FromQuery] int limit = 50,
            CancellationToken token = default)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();
            var result = await _dmService.GetMessagesAsync(userId, conversationId, before, limit, token);
            return Ok(result);
        }

        //// POST /api/dm/conversations/{conversationId}/messages
        //[HttpPost("dm/conversations/{conversationId}/messages")]
        //public async Task<IActionResult> SendMessage(
        //    string otherUserId,
        //    [FromBody] SendDmRequest request,
        //    CancellationToken token)
        //{
        //    var userId = GetCurrentUserId();
        //    if (userId is null) return UnauthorizedResponse();
        //    var result = await _dmService.SendMessageAsync(userId, otherUserId, request, token);
        //    return Ok(result);
        //}

        // PATCH /api/dm/conversations/{conversationId}/messages/{messageId}
        [HttpPatch("dm/conversations/{conversationId}/messages/{messageId}")]
        public async Task<IActionResult> EditMessage(
             string conversationId,
             string messageId,
             [FromBody] EditDmRequest request,
             CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();
            var result = await _dmService.EditMessageAsync(userId, conversationId, messageId, request, token);
            return Ok(result);
        }

        // DELETE /api/dm/conversations/{conversationId}/messages/{messageId}
        [HttpDelete("dm/conversations/{conversationId}/messages/{messageId}")]
        public async Task<IActionResult> DeleteMessage(
             string conversationId,
             string messageId,
             CancellationToken token)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return UnauthorizedResponse();
            var result = await _dmService.DeleteMessageAsync(userId, conversationId, messageId, token);
            return Ok(result);
        }
    }
}
