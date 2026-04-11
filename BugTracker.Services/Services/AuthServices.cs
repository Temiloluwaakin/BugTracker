using BugTracker.Data;
using BugTracker.Data.Context;
using BugTracker.Data.Entities;
using BugTracker.Data.Models;
using BugTracker.Services.Helpers;
using MongoDB.Driver;
using Serilog;

namespace BugTracker.Services.Services
{
    public interface IAuthService
    {
        Task<ApiResponse<AuthResponse>> SignUpAsync(SignUpRequest request, CancellationToken token);
        Task<ApiResponse<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken token);
    }

    public class AuthServices : IAuthService
    {
        private readonly DatabaseContext _db;
        private readonly IAuthHelpers _authHelpers;
        private readonly IEmailHelper _emailHelper;

        public AuthServices(DatabaseContext db, IAuthHelpers authHelpers, IEmailHelper emailHelper)
        {
            _db = db;
            _authHelpers = authHelpers;
            _emailHelper = emailHelper;
        }


        public async Task<ApiResponse<AuthResponse>> SignUpAsync(SignUpRequest request, CancellationToken token)
        {
            try
            {
                var email = request.Email;

                // 1. Check if email is already taken
                var existing = await _db.Users.Find(u => u.Email == email).FirstOrDefaultAsync();

                if (existing != null)
                {
                    Log.Information("The Account already exist with this mail {mail}", request.Email);
                    return new ApiResponse<AuthResponse>
                    {
                        ResponseCode = ResponseCodes.DuplicateRecord.ResponseCode,
                        ResponseMessage = "The Email already Registered"
                    };
                }

                // 2. Hash the password
                var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

                // 3. Create the user document
                var user = new User
                {
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    FullName = $"{request.FirstName} {request.LastName}",
                    Email = email,
                    PasswordHash = passwordHash,
                    IsEmailVerified = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _db.Users.InsertOneAsync(user);

                // 4. Check for pending invitations and process them
                if (!string.IsNullOrWhiteSpace(request.InviteToken))
                {
                    await RedeemInviteDuringSignupAsync(user, request.InviteToken);
                }
                await ProcessPendingInvitationsAsync(user);

                // 5. Issue a JWT
                return BuildAuthResponse(user);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during user registration for email {Email}", request.Email);
                return new ApiResponse<AuthResponse>
                {
                    ResponseCode = ResponseCodes.SystemMalfunction.ResponseCode,
                    ResponseMessage = "An error occurred during SignUp. Please try again later."
                };
            }
        }




        public async Task<ApiResponse<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken token)
        {
            try
            {
                var email = request.Email.Trim().ToLowerInvariant();

                // 1. Find user by email
                var user = await _db.Users.Find(u => u.Email == email).FirstOrDefaultAsync();

                if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                {
                    Log.Information("Invalid Credentials inputed for email: {email}", request.Email);
                    return new ApiResponse<AuthResponse>
                    {
                        ResponseCode = ResponseCodes.UnAuthorized.ResponseCode,
                        ResponseMessage = "Invalid email or password."
                    };
                }

                // 2. Update lastLoginAt
                var update = Builders<User>.Update.Set(u => u.LastLoginAt, DateTime.UtcNow).Set(u => u.UpdatedAt, DateTime.UtcNow);

                await _db.Users.UpdateOneAsync(u => u.Id == user.Id, update);

                user.LastLoginAt = DateTime.UtcNow;

                // 3. Issue a JWT
                return BuildAuthResponse(user);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during user login for email {Email}", request.Email);
                return new ApiResponse<AuthResponse>
                {
                    ResponseCode = ResponseCodes.SystemMalfunction.ResponseCode,
                    ResponseMessage = "An error occurred during login. Please try again later."
                };
            }
        }
        



        /// <summary>
        /// When a new user registers, check if they have any pending invitations
        /// and automatically add them to those projects.
        /// </summary>
        private async Task ProcessPendingInvitationsAsync(User user)
        {
            try
            {
                var pendingInvites = await _db.Invitations
                .Find(i => i.InvitedEmail == user.Email && i.Status == InvitationStatus.Pending)
                .ToListAsync();

                if (pendingInvites.Count == 0) return;

                foreach (var invite in pendingInvites)
                {
                    // Add user to the project's members array
                    var newMember = new ProjectMember
                    {
                        UserId = user.Id,
                        Email = user.Email,
                        FullName = user.FullName,
                        Role = invite.Role,
                        JoinedAt = DateTime.UtcNow,
                        AddedBy = invite.InvitedBy
                    };

                    var projectUpdate = Builders<Project>.Update
                        .Push(p => p.Members, newMember)
                        .Set(p => p.UpdatedAt, DateTime.UtcNow);

                    await _db.Projects.UpdateOneAsync(p => p.Id == invite.ProjectId, projectUpdate);

                    // Mark the invitation as accepted
                    var inviteUpdate = Builders<Invitation>.Update
                        .Set(i => i.Status, InvitationStatus.Accepted)
                        .Set(i => i.RespondedAt, DateTime.UtcNow);

                    await _db.Invitations.UpdateOneAsync(i => i.Id == invite.Id, inviteUpdate);
                }

                return;
            
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing pending invitation for email {Email}", user.Email);
            }
        }


        private async Task RedeemInviteDuringSignupAsync(User user, string token)
        {
            var invite = await _db.Invitations
                .Find(i => i.Token == token)
                .FirstOrDefaultAsync();

            if (invite == null)
            {
                Log.Warning("Invalid invite token used during signup");
                return;
            }

            if (invite.Status != InvitationStatus.Pending)
            {
                Log.Warning("Invite already used");
                return;
            }

            if (invite.ExpiresAt < DateTime.UtcNow)
            {
                Log.Warning("Invite expired");
                return;
            }

            if (invite.InvitedEmail != user.Email)
            {
                Log.Warning("Invite email mismatch");
                return;
            }

            //Add user to project
            var member = new ProjectMember
            {
                UserId = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Role = invite.Role,
                JoinedAt = DateTime.UtcNow,
                AddedBy = invite.InvitedBy
            };

            var update = Builders<Project>.Update
                .Push(p => p.Members, member)
                .Set(p => p.UpdatedAt, DateTime.UtcNow);

            await _db.Projects.UpdateOneAsync(
                p => p.Id == invite.ProjectId,
                update);

            //Mark invite as used
            var inviteUpdate = Builders<Invitation>.Update
                .Set(i => i.Status, InvitationStatus.Accepted);

            await _db.Invitations.UpdateOneAsync(
                i => i.Id == invite.Id,
                inviteUpdate);

            Log.Information("User {UserId} joined project {ProjectId} via invite",
                user.Id, invite.ProjectId);
        }

        private ApiResponse<AuthResponse> BuildAuthResponse(User user)
        {
            var token = _authHelpers.GenerateToken(user, out var expiresAt);

            return new ApiResponse<AuthResponse>
            {
                ResponseCode = ResponseCodes.Success.ResponseCode,
                ResponseMessage = ResponseCodes.Success.ResponseMessage,
                Data = new AuthResponse
                {
                    AccessToken = token,
                    ExpiresAt = expiresAt,
                    User = new UserDto
                    {
                        Id = user.Id,
                        FullName = user.FullName,
                        Email = user.Email,
                        AvatarUrl = user.AvatarUrl,
                        IsEmailVerified = user.IsEmailVerified
                    }
                }
            };
        }
    }
}
