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
    //public class TestCase : BaseDocument
    //{
    //    [BsonElement("projectId")]
    //    [BsonRepresentation(BsonType.ObjectId)]
    //    public string ProjectId { get; set; } = string.Empty;

    //    /// <summary>
    //    /// Human-readable sequential number scoped to the project (e.g. TC-011).
    //    /// Generated atomically via the counters collection.
    //    /// </summary>
    //    [BsonElement("caseNumber")]
    //    public int CaseNumber { get; set; }

    //    [BsonElement("title")]
    //    public string Title { get; set; } = string.Empty;

    //    /// <summary>
    //    /// Context or background for the test. Supports markdown.
    //    /// </summary>
    //    [BsonElement("description")]
    //    [BsonIgnoreIfNull]
    //    public string? Description { get; set; }

    //    /// <summary>
    //    /// State required before executing this test.
    //    /// E.g. "User must be logged out" or "Cart must have at least 2 items"
    //    /// </summary>
    //    [BsonElement("preconditions")]
    //    [BsonIgnoreIfNull]
    //    public string? Preconditions { get; set; }

    //    /// <summary>
    //    /// Ordered list of steps to execute this test case.
    //    /// </summary>
    //    [BsonElement("steps")]
    //    public List<TestCaseStep> Steps { get; set; } = new();

    //    /// <summary>
    //    /// What a passing execution of this test case looks like overall.
    //    /// </summary>
    //    [BsonElement("expectedResult")]
    //    public string ExpectedResult { get; set; } = string.Empty;

    //    [BsonElement("priority")]
    //    [BsonRepresentation(BsonType.String)]
    //    public TestCasePriority Priority { get; set; } = TestCasePriority.Medium;

    //    [BsonElement("status")]
    //    [BsonRepresentation(BsonType.String)]
    //    public TestCaseStatus Status { get; set; } = TestCaseStatus.Draft;

    //    [BsonElement("createdBy")]
    //    [BsonRepresentation(BsonType.ObjectId)]
    //    public string CreatedBy { get; set; } = string.Empty;

    //    /// <summary>
    //    /// The tester primarily responsible for executing this case.
    //    /// </summary>
    //    [BsonElement("assignedTo")]
    //    [BsonRepresentation(BsonType.ObjectId)]
    //    [BsonIgnoreIfNull]
    //    public string? AssignedTo { get; set; }

    //    /// <summary>
    //    /// E.g. ["smoke", "regression", "auth", "checkout"]
    //    /// </summary>
    //    [BsonElement("tags")]
    //    public List<string> Tags { get; set; } = new();
    //}

    //public class TestCaseStep
    //{
    //    /// <summary>
    //    /// 1-based ordering index.
    //    /// </summary>
    //    [BsonElement("stepNumber")]
    //    public int StepNumber { get; set; }

    //    /// <summary>
    //    /// What the tester does at this step.
    //    /// E.g. "Enter a valid email address in the Email field"
    //    /// </summary>
    //    [BsonElement("action")]
    //    public string Action { get; set; } = string.Empty;

    //    /// <summary>
    //    /// Expected result specific to this step.
    //    /// E.g. "The field accepts input and shows no validation error"
    //    /// </summary>
    //    [BsonElement("expectedOutcome")]
    //    public string ExpectedOutcome { get; set; } = string.Empty;
    //}


    // ═══════════════════════════════════════════════════════════
    // TEST CASE — defines what to test upfront.
    // Separate from bugs — a test case is the recipe,
    // a bug is what went wrong when the recipe was followed.
    // ═══════════════════════════════════════════════════════════
    public class TestCase : BaseDocument
    {

        [BsonElement("projectId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId ProjectId { get; set; }

        /// <summary>
        /// Human-readable sequential number per project e.g. TC-011.
        /// Generated atomically via the counters collection using key "{projectId}_testcases".
        /// </summary>
        [BsonElement("caseNumber")]
        public int CaseNumber { get; set; }

        [BsonElement("title")]
        public string Title { get; set; } = string.Empty;

        [BsonElement("description")]
        [BsonIgnoreIfNull]
        public string? Description { get; set; }

        /// <summary>State the system must be in before the test can be run. e.g. "User must be logged out."</summary>
        [BsonElement("preconditions")]
        [BsonIgnoreIfNull]
        public string? Preconditions { get; set; }

        /// <summary>Ordered steps the tester follows during execution.</summary>
        [BsonElement("steps")]
        public List<TestCaseStep> Steps { get; set; } = new();

        /// <summary>What a fully passing execution looks like overall.</summary>
        [BsonElement("expectedResult")]
        public string ExpectedResult { get; set; } = string.Empty;

        /// <summary>high | medium | low</summary>
        [BsonElement("priority")]
        [BsonRepresentation(BsonType.String)]
        public string Priority { get; set; } = string.Empty;

        /// <summary>
        /// draft    — being written, not ready to execute.
        /// active   — ready and available for test runs.
        /// deprecated — no longer relevant, archived from execution.
        /// </summary>
        [BsonElement("status")]
        [BsonRepresentation(BsonType.String)]
        public string Status { get; set; } = "draft";

        // ── Creator ──
        [BsonElement("createdById")]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId CreatedById { get; set; }

        [BsonElement("createdByName")]
        public string CreatedByName { get; set; } = string.Empty;

        [BsonElement("createdByEmail")]
        public string CreatedByEmail { get; set; } = string.Empty;

        /// <summary>
        /// Optional: a specific tester responsible for executing this case.
        /// Any tester/owner can still run it — this is just for ownership clarity.
        /// </summary>
        [BsonElement("assignedToId")]
        [BsonIgnoreIfNull]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId? AssignedToId { get; set; }

        [BsonElement("assignedToName")]
        [BsonIgnoreIfNull]
        public string? AssignedToName { get; set; }

        [BsonElement("assignedToEmail")]
        [BsonIgnoreIfNull]
        public string? AssignedToEmail { get; set; }

        [BsonElement("tags")]
        public List<string> Tags { get; set; } = new();
    }

    // ═══════════════════════════════════════════════════════════
    // TEST CASE STEP — embedded subdocument (ordered)
    // ═══════════════════════════════════════════════════════════
    public class TestCaseStep
    {
        /// <summary>Ordering index starting at 1.</summary>
        [BsonElement("stepNumber")]
        public int StepNumber { get; set; }

        /// <summary>What the tester physically does. e.g. "Enter valid email and password."</summary>
        [BsonElement("action")]
        public string Action { get; set; } = string.Empty;

        /// <summary>Expected outcome of this specific step. e.g. "Password field shows dots."</summary>
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
