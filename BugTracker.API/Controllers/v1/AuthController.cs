using Asp.Versioning;
using BugTracker.Data.Models;
using BugTracker.Services.Services;
using Microsoft.AspNetCore.Mvc;

namespace BugTracker.API.Controllers.v1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:ApiVersion}/controller")]
    public class AuthController : ControllerBase
    {

        private readonly ILogger<AuthController> _logger;
        private readonly IAuthService _authService;

        public AuthController(ILogger<AuthController> logger, IAuthService authService)
        {
            _logger = logger;
            _authService = authService;
        }



        /// <summary>
        /// POST /api/auth/signup
        /// Creates a new user account and returns a JWT.
        /// </summary>
        [HttpPost("signup")]
        [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(string), 400)] // Added for bad request
        [ProducesResponseType(typeof(string), 500)]
        public async Task<IActionResult> SignUp([FromBody] SignUpRequest request, CancellationToken token)
        {
            if (!ModelState.IsValid)
                return BadRequest("Request body cannot be null.");

            try
            {
                var result = await _authService.SignUpAsync(request, token);
                return StatusCode(StatusCodes.Status201Created, result);
            }
            catch (InvalidOperationException ex)
            {
                // Email already taken
                return Conflict("Error Occured");
            }
        }

        /// <summary>
        /// POST /api/auth/login
        /// Authenticates a user and returns a JWT.
        /// </summary>
        [HttpPost("login")]
        [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(string), 400)] // Added for bad request
        [ProducesResponseType(typeof(string), 500)]
        public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken token)
        {
            if (!ModelState.IsValid)
                return BadRequest("Request body cannot be null.");

            try
            {
                var result = await _authService.LoginAsync(request, token);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized("Unauthorised");
            }
        }
    }
}
