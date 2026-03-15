using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BugTracker.Data.Models
{
    public class UserDashboardMetricsResponse
    {
        public int TotalBugs { get; set; }

        public int Open { get; set; }
        public int InProgress { get; set; }
        public int Closed { get; set; }
        public int WontFix { get; set; }
        public int Duplicate { get; set; }

        public int AsDeveloper { get; set; }
        public int AsTester { get; set; }

        public double CompletionPercentage { get; set; }
    }

    public class DashboardMetricsQuery
    {
        /// <summary>
        /// Optional project filter. 
        /// If null, metrics are calculated across ALL projects the user belongs to.
        /// </summary>
        public string? ProjectId { get; set; }

        /// <summary>
        /// Number of days to look back for activity charts.
        /// Default recommended: 7 or 30.
        /// </summary>
        public int ActivityDays { get; set; } = 7;

        /// <summary>
        /// Include bug status distribution.
        /// Used for Pie Chart.
        /// </summary>
        public bool IncludeBugStatusDistribution { get; set; }

        /// <summary>
        /// Include bug creation vs closure metrics.
        /// Used for Line Chart.
        /// </summary>
        public bool IncludeBugLifecycleTrend { get; set; }

        /// <summary>
        /// Include activity timeline metrics.
        /// Used for Bar Chart.
        /// </summary>
        public bool IncludeActivityTimeline { get; set; }

        /// <summary>
        /// Include top contributors.
        /// Used for Bar Chart.
        /// </summary>
        public bool IncludeTopContributors { get; set; }

        /// <summary>
        /// Include summary cards.
        /// Used for dashboard cards.
        /// </summary>
        public bool IncludeSummaryCards { get; set; }
    }


    public class DashboardMetricsResponse
    {
        public DashboardSummaryCards? SummaryCards { get; set; }

        public BugStatusDistribution? BugStatusDistribution { get; set; }

        public List<BugLifecycleTrendItem>? BugLifecycleTrend { get; set; }

        public List<ActivityTimelineItem>? ActivityTimeline { get; set; }

        public List<TopContributorItem>? TopContributors { get; set; }
    }

    public class TopContributorItem
    {
        /// <summary>User name.</summary>
        public string ActorName { get; set; } = string.Empty;

        /// <summary>Total actions performed.</summary>
        public int ActivityCount { get; set; }
    }

    public class ActivityTimelineItem
    {
        /// <summary>Date bucket.</summary>
        public DateTime Date { get; set; }

        /// <summary>Total activities recorded on this date.</summary>
        public int ActivityCount { get; set; }
    }

    public class BugLifecycleTrendItem
    {
        /// <summary>Date bucket.</summary>
        public DateTime Date { get; set; }

        /// <summary>Number of bugs created on this date.</summary>
        public int Created { get; set; }

        /// <summary>Number of bugs closed on this date.</summary>
        public int Closed { get; set; }
    }

    public class BugStatusDistribution
    {
        public int Open { get; set; }
        public int InProgress { get; set; }
        public int Closed { get; set; }
        public int WontFix { get; set; }
        public int Duplicate { get; set; }
    }


    public class DashboardSummaryCards
    {
        /// <summary>Total bugs across the scope.</summary>
        public int TotalBugs { get; set; }

        /// <summary>Bugs currently open.</summary>
        public int OpenBugs { get; set; }

        /// <summary>Bugs currently in progress.</summary>
        public int InProgressBugs { get; set; }

        /// <summary>Bugs closed.</summary>
        public int ClosedBugs { get; set; }

        /// <summary>Percentage of bugs closed.</summary>
        public double CompletionPercentage { get; set; }

        /// <summary>Total activity events.</summary>
        public int TotalActivities { get; set; }
    }
}
