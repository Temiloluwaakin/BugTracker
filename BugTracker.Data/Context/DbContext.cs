using BugTracker.Data.Entities;
using MongoDB.Driver;

namespace BugTracker.Data.Context
{
    /// <summary>
    /// Central access point for all MongoDB collections.
    /// Register as a singleton in your DI container.
    ///
    /// In Program.cs:
    ///     builder.Services.Configure&lt;DatabaseSettings&gt;(
    ///         builder.Configuration.GetSection("DatabaseSettings"));
    ///     builder.Services.AddSingleton&lt;DatabaseContext&gt;();
    /// </summary>
    public class DatabaseContext
    {
        private readonly IMongoDatabase _database;

        public DatabaseContext(IMongoDatabase database)
        {
            _database = database;

            EnsureIndexes();
        }


        public IMongoCollection<User> Users =>
            _database.GetCollection<User>("users");

        public IMongoCollection<Project> Projects =>
            _database.GetCollection<Project>("projects");

        public IMongoCollection<Invitation> Invitations =>
            _database.GetCollection<Invitation>("invitations");

        public IMongoCollection<Bug> Bugs =>
            _database.GetCollection<Bug>("bugs");

        public IMongoCollection<TestCase> TestCases =>
            _database.GetCollection<TestCase>("testcases");

        public IMongoCollection<TestRun> TestRuns =>
            _database.GetCollection<TestRun>("testRuns");

        public IMongoCollection<Comment> Comments =>
            _database.GetCollection<Comment>("comments");

        public IMongoCollection<ActivityLog> ActivityLogs =>
            _database.GetCollection<ActivityLog>("activityLogs");

        public IMongoCollection<Counter> Counters =>
            _database.GetCollection<Counter>("counters");




        // ── Index Setup 

        /// <summary>
        /// Idempotent — safe to call on every startup.
        /// MongoDB skips index creation if the index already exists.
        /// </summary>
        private void EnsureIndexes()
        {
            // users
            Users.Indexes.CreateOne(new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(u => u.Email),
                new CreateIndexOptions { Unique = true, Name = "idx_users_email" }));

            // projects
            Projects.Indexes.CreateOne(new CreateIndexModel<Project>(
                Builders<Project>.IndexKeys.Ascending("members.userId"),
                new CreateIndexOptions { Name = "idx_projects_memberUserId" }));

            Projects.Indexes.CreateOne(new CreateIndexModel<Project>(
                Builders<Project>.IndexKeys.Ascending(p => p.OwnerId),
                new CreateIndexOptions { Name = "idx_projects_ownerId" }));

            // invitations — unique token + TTL auto-expire
            Invitations.Indexes.CreateOne(new CreateIndexModel<Invitation>(
                Builders<Invitation>.IndexKeys.Ascending(i => i.Token),
                new CreateIndexOptions { Unique = true, Name = "idx_invitations_token" }));

            Invitations.Indexes.CreateOne(new CreateIndexModel<Invitation>(
                Builders<Invitation>.IndexKeys
                    .Ascending(i => i.InvitedEmail)
                    .Ascending(i => i.Status),
                new CreateIndexOptions { Name = "idx_invitations_email_status" }));

            Invitations.Indexes.CreateOne(new CreateIndexModel<Invitation>(
                Builders<Invitation>.IndexKeys.Ascending(i => i.ExpiresAt),
                new CreateIndexOptions
                {
                    ExpireAfter = TimeSpan.Zero, // TTL — MongoDB deletes doc when ExpiresAt is reached
                    Name = "idx_invitations_ttl"
                }));

            // bugs
            Bugs.Indexes.CreateOne(new CreateIndexModel<Bug>(
                Builders<Bug>.IndexKeys
                    .Ascending(b => b.ProjectId)
                    .Ascending(b => b.BugNumber),
                new CreateIndexOptions { Unique = true, Name = "idx_bugs_projectId_bugNumber" }));

            Bugs.Indexes.CreateOne(new CreateIndexModel<Bug>(
                Builders<Bug>.IndexKeys
                    .Ascending(b => b.ProjectId)
                    .Ascending(b => b.Status),
                new CreateIndexOptions { Name = "idx_bugs_projectId_status" }));

            Bugs.Indexes.CreateOne(new CreateIndexModel<Bug>(
                Builders<Bug>.IndexKeys.Ascending(b => b.AssignedTo),
                new CreateIndexOptions { Name = "idx_bugs_assignedTo" }));

            // testcases
            TestCases.Indexes.CreateOne(new CreateIndexModel<TestCase>(
                Builders<TestCase>.IndexKeys
                    .Ascending(tc => tc.ProjectId)
                    .Ascending(tc => tc.CaseNumber),
                new CreateIndexOptions { Unique = true, Name = "idx_testcases_projectId_caseNumber" }));

            // testRuns
            TestRuns.Indexes.CreateOne(new CreateIndexModel<TestRun>(
                Builders<TestRun>.IndexKeys.Ascending(tr => tr.TestCaseId),
                new CreateIndexOptions { Name = "idx_testRuns_testCaseId" }));

            TestRuns.Indexes.CreateOne(new CreateIndexModel<TestRun>(
                Builders<TestRun>.IndexKeys
                    .Ascending(tr => tr.ProjectId)
                    .Descending(tr => tr.ExecutedAt),
                new CreateIndexOptions { Name = "idx_testRuns_projectId_executedAt" }));

            // comments
            Comments.Indexes.CreateOne(new CreateIndexModel<Comment>(
                Builders<Comment>.IndexKeys
                    .Ascending(c => c.EntityType)
                    .Ascending(c => c.EntityId),
                new CreateIndexOptions { Name = "idx_comments_entity" }));

            // activityLogs
            ActivityLogs.Indexes.CreateOne(new CreateIndexModel<ActivityLog>(
                Builders<ActivityLog>.IndexKeys
                    .Ascending(a => a.ProjectId)
                    .Descending(a => a.CreatedAt),
                new CreateIndexOptions { Name = "idx_activityLogs_projectId_createdAt" }));
        }
    }
}
