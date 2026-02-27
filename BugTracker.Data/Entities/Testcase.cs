using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace BugTracker.Data.Entities
{
    /// <summary>
    /// Represents a defined test case within a project.
    /// Test cases describe what to test; TestRuns record each execution attempt.
    /// Collection: testcases
    /// </summary>
    public class TestCase : BaseDocument
    {
        [BsonElement("projectId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string ProjectId { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable sequential number scoped to the project (e.g. TC-011).
        /// Generated atomically via the counters collection.
        /// </summary>
        [BsonElement("caseNumber")]
        public int CaseNumber { get; set; }

        [BsonElement("title")]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Context or background for the test. Supports markdown.
        /// </summary>
        [BsonElement("description")]
        [BsonIgnoreIfNull]
        public string? Description { get; set; }

        /// <summary>
        /// State required before executing this test.
        /// E.g. "User must be logged out" or "Cart must have at least 2 items"
        /// </summary>
        [BsonElement("preconditions")]
        [BsonIgnoreIfNull]
        public string? Preconditions { get; set; }

        /// <summary>
        /// Ordered list of steps to execute this test case.
        /// </summary>
        [BsonElement("steps")]
        public List<TestCaseStep> Steps { get; set; } = new();

        /// <summary>
        /// What a passing execution of this test case looks like overall.
        /// </summary>
        [BsonElement("expectedResult")]
        public string ExpectedResult { get; set; } = string.Empty;

        [BsonElement("priority")]
        [BsonRepresentation(BsonType.String)]
        public TestCasePriority Priority { get; set; } = TestCasePriority.Medium;

        [BsonElement("status")]
        [BsonRepresentation(BsonType.String)]
        public TestCaseStatus Status { get; set; } = TestCaseStatus.Draft;

        [BsonElement("createdBy")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string CreatedBy { get; set; } = string.Empty;

        /// <summary>
        /// The tester primarily responsible for executing this case.
        /// </summary>
        [BsonElement("assignedTo")]
        [BsonRepresentation(BsonType.ObjectId)]
        [BsonIgnoreIfNull]
        public string? AssignedTo { get; set; }

        /// <summary>
        /// E.g. ["smoke", "regression", "auth", "checkout"]
        /// </summary>
        [BsonElement("tags")]
        public List<string> Tags { get; set; } = new();
    }

    public class TestCaseStep
    {
        /// <summary>
        /// 1-based ordering index.
        /// </summary>
        [BsonElement("stepNumber")]
        public int StepNumber { get; set; }

        /// <summary>
        /// What the tester does at this step.
        /// E.g. "Enter a valid email address in the Email field"
        /// </summary>
        [BsonElement("action")]
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// Expected result specific to this step.
        /// E.g. "The field accepts input and shows no validation error"
        /// </summary>
        [BsonElement("expectedOutcome")]
        public string ExpectedOutcome { get; set; } = string.Empty;
    }

    public enum TestCasePriority
    {
        High,
        Medium,
        Low
    }

    public enum TestCaseStatus
    {
        /// <summary>Still being written — not ready for execution.</summary>
        Draft,

        /// <summary>Ready and available for testers to execute.</summary>
        Active,

        /// <summary>No longer relevant — kept for history but shouldn't be executed.</summary>
        Deprecated
    }
}
