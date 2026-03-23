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

        public ChatController(IChatService chatService)
        {
            _chatService = chatService;
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
    }
}
