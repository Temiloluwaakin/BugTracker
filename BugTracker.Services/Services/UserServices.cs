using BugTracker.Data;
using BugTracker.Data.Context;
using BugTracker.Data.Entities;
using BugTracker.Data.Models;
using BugTracker.Services.Helpers;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;

namespace BugTracker.Services.Services
{
    public interface IUserService
    {
        Task<ApiResponse<UserDashboardMetricsResponse>> GetUserDashboardMetricsAsync(string actorUserId, CancellationToken token);
        Task<ApiResponse<DashboardMetricsResponse>> GetDashboardMetricsAsync(string actorUserId, DashboardMetricsQuery query, CancellationToken token);
    }
    public class UserServices : IUserService
    {
        private readonly ILogger<UserServices> _logger;
        private readonly DatabaseContext _db;
        private readonly IResponseHelper _responseHelper;

        public UserServices(
            ILogger<UserServices> logger,
            DatabaseContext db,
            IResponseHelper responseHelper
        )
        {
            _logger = logger;
            _db = db;
            _responseHelper = responseHelper;
        }


        /// <summary>
        /// method to get the metrics of a logged in user accross all his projects
        /// </summary>
        /// <param name="actorUserId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<ApiResponse<UserDashboardMetricsResponse>> GetUserDashboardMetricsAsync(
            string actorUserId,
            CancellationToken token)
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return _responseHelper.Fail<UserDashboardMetricsResponse>(
                        ResponseCodes.InvalidEntryDetected.ResponseCode,
                        "Invalid user ID format.");

                var filter = Builders<Bug>.Filter.Or(
                    Builders<Bug>.Filter.Eq(b => b.AssignedDeveloperId, actorObjId),
                    Builders<Bug>.Filter.Eq(b => b.AssignedTesterId, actorObjId),
                    Builders<Bug>.Filter.Eq(b => b.ReportedById, actorObjId)
                );

                var bugs = await _db.Bugs
                    .Find(filter)
                    .ToListAsync(token);

                var total = bugs.Count;

                var open = bugs.Count(b => b.Status == BugStatus.Open);
                var inProgress = bugs.Count(b => b.Status == BugStatus.InProgress);
                var closed = bugs.Count(b => b.Status == BugStatus.Closed);
                var wontFix = bugs.Count(b => b.Status == BugStatus.WontFix);
                var duplicate = bugs.Count(b => b.Status == BugStatus.Duplicate);

                var asDeveloper = bugs.Count(b => b.AssignedDeveloperId == actorObjId);
                var asTester = bugs.Count(b => b.AssignedTesterId == actorObjId);

                var completion = total == 0
                    ? 0
                    : Math.Round((double)closed / total * 100, 2);

                var response = new UserDashboardMetricsResponse
                {
                    TotalBugs = total,
                    Open = open,
                    InProgress = inProgress,
                    Closed = closed,
                    WontFix = wontFix,
                    Duplicate = duplicate,
                    AsDeveloper = asDeveloper,
                    AsTester = asTester,
                    CompletionPercentage = completion
                };

                return _responseHelper.Ok(response);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching dashboard metrics for user {UserId}", actorUserId);
                return _responseHelper.SystemError<UserDashboardMetricsResponse>();
            }
        }


        public async Task<ApiResponse<DashboardMetricsResponse>> GetDashboardMetricsAsync(
            string actorUserId,
            DashboardMetricsQuery query,
            CancellationToken token
        )
        {
            try
            {
                if (!ObjectId.TryParse(actorUserId, out var actorObjId))
                    return _responseHelper.Fail<DashboardMetricsResponse>(
                        ResponseCodes.InvalidEntryDetected.ResponseCode,
                        "Invalid user ID format.");

                var response = new DashboardMetricsResponse();

                var bugFilter = Builders<Bug>.Filter.Or(
                    Builders<Bug>.Filter.Eq(b => b.AssignedDeveloperId, actorObjId),
                    Builders<Bug>.Filter.Eq(b => b.AssignedTesterId, actorObjId)
                );

                if (!string.IsNullOrWhiteSpace(query.ProjectId))
                {
                    bugFilter = Builders<Bug>.Filter.And(
                        bugFilter,
                        Builders<Bug>.Filter.Eq(b => b.ProjectId, query.ProjectId));
                }

                var bugs = await _db.Bugs
                    .Find(bugFilter)
                    .ToListAsync(token);

                if (query.IncludeSummaryCards)
                {
                    var total = bugs.Count;
                    var open = bugs.Count(b => b.Status == BugStatus.Open);
                    var inProgress = bugs.Count(b => b.Status == BugStatus.InProgress);
                    var closed = bugs.Count(b => b.Status == BugStatus.Closed);

                    response.SummaryCards = new DashboardSummaryCards
                    {
                        TotalBugs = total,
                        OpenBugs = open,
                        InProgressBugs = inProgress,
                        ClosedBugs = closed,
                        CompletionPercentage = total == 0 ? 0 : Math.Round((double)closed / total * 100, 2),
                        TotalActivities = (int)await _db.ActivityLogs.CountDocumentsAsync(
                            Builders<ActivityLog>.Filter.Empty,
                            cancellationToken: token
                        )
                    };
                }

                if (query.IncludeBugStatusDistribution)
                {
                    response.BugStatusDistribution = new BugStatusDistribution
                    {
                        Open = bugs.Count(b => b.Status == BugStatus.Open),
                        InProgress = bugs.Count(b => b.Status == BugStatus.InProgress),
                        Closed = bugs.Count(b => b.Status == BugStatus.Closed),
                        WontFix = bugs.Count(b => b.Status == BugStatus.WontFix),
                        Duplicate = bugs.Count(b => b.Status == BugStatus.Duplicate)
                    };
                }

                if (query.IncludeActivityTimeline)
                {
                    var startDate = DateTime.UtcNow.AddDays(-query.ActivityDays);

                    var activities = await _db.ActivityLogs
                        .Find(a => a.CreatedAt >= startDate)
                        .ToListAsync(token);

                    response.ActivityTimeline = activities
                        .GroupBy(a => a.CreatedAt.Date)
                        .Select(g => new ActivityTimelineItem
                        {
                            Date = g.Key,
                            ActivityCount = g.Count()
                        })
                        .OrderBy(x => x.Date)
                        .ToList();
                }

                if (query.IncludeTopContributors)
                {
                    var activities = await _db.ActivityLogs
                        .Find(_ => true)
                        .ToListAsync(token);

                    response.TopContributors = activities
                        .GroupBy(a => a.ActorName)
                        .Select(g => new TopContributorItem
                        {
                            ActorName = g.Key,
                            ActivityCount = g.Count()
                        })
                        .OrderByDescending(x => x.ActivityCount)
                        .Take(5)
                        .ToList();
                }

                return _responseHelper.Ok(response);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error generating dashboard metrics for user {UserId}", actorUserId);
                return _responseHelper.SystemError<DashboardMetricsResponse>();
            }
        }
    }
}
