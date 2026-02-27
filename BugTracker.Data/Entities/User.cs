using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BugTracker.Data.Entities
{
    public class User : BaseDocument
    {
        [BsonElement("firstName")]
        public string FirstName { get; set; } = string.Empty;


        [BsonElement("lastName")]
        public string LastName { get; set; } = string.Empty;


        [BsonElement("fullName")]
        public string FullName { get; set; } = string.Empty;

        /// <summary>
        /// Always stored lowercase. Unique across the system.
        /// </summary>
        [BsonElement("email")]
        public string Email { get; set; } = string.Empty;


        [BsonElement("phoneNumber")]
        public string PhoneNumber { get; set; } = string.Empty;

        /// <summary>
        /// bcrypt hashed password. Never store plaintext.
        /// </summary>
        [BsonElement("passwordHash")]
        public string PasswordHash { get; set; } = string.Empty;


        [BsonElement("avatarUrl")]
        [BsonIgnoreIfNull]
        public string? AvatarUrl { get; set; }

        /// <summary>
        /// True once the user clicks the email verification link.
        /// </summary>
        [BsonElement("isEmailVerified")]
        public bool IsEmailVerified { get; set; } = false;

        [BsonElement("lastLoginAt")]
        [BsonIgnoreIfNull]
        public DateTime? LastLoginAt { get; set; }
    }
}
