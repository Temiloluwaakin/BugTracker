using System.Security.Claims;
using System.Text;
using BugTracker.Data.Entities;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace BugTracker.Services.Helpers
{
    public interface IAuthHelpers
    {
        string GenerateToken(User user, out DateTime expiresAt);
        ClaimsPrincipal? ValidateToken(string token);
    }

    public class AuthHelpers : IAuthHelpers
    {
        private readonly IConfiguration _config;


        public AuthHelpers(IConfiguration config)
        {
            _config = config;
        }


        public string GenerateToken(User user, out DateTime expiresAt)
        {
            var secret = _config["Jwt:key"] ?? throw new InvalidOperationException("JWT secret not configured.");
            var issuer = _config["Jwt:Issuer"] ?? "BugTrackPro";
            var audience = _config["Jwt:Audience"] ?? "BugTrackPro";
            var expiryHours = int.Parse(_config["Jwt:ExpiryHours"] ?? "24");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            expiresAt = DateTime.UtcNow.AddHours(expiryHours);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Name, user.FullName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                    ClaimValueTypes.Integer64)
            };

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: expiresAt,
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public ClaimsPrincipal? ValidateToken(string token)
        {
            var secret = _config["Jwt:Secret"]
                ?? throw new InvalidOperationException("JWT secret not configured.");

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(secret);

            try
            {
                var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = _config["Jwt:Issuer"] ?? "BugTrackPro",
                    ValidateAudience = true,
                    ValidAudience = _config["Jwt:Audience"] ?? "BugTrackPro",
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero // No tolerance — token expires exactly on time
                }, out _);

                return principal;
            }
            catch
            {
                return null;
            }
        }
    }
}
