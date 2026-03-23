using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BugTracker.Data.Entities
{
    /// <summary>
    /// Records a single execution of a TestCase.
    /// Each run is its own document, giving a full execution history over time.
    /// Collection: testRuns
    /// </summary>
    //public class TestRun : BaseDocument
    //{
    //    [BsonElement("projectId")]
    //    [BsonRepresentation(BsonType.ObjectId)]
    //    public string ProjectId { get; set; } = string.Empty;

    //    [BsonElement("testCaseId")]
    //    [BsonRepresentation(BsonType.ObjectId)]
    //    public string TestCaseId { get; set; } = string.Empty;

    //    [BsonElement("executedBy")]
    //    [BsonRepresentation(BsonType.ObjectId)]
    //    public string ExecutedBy { get; set; } = string.Empty;

    //    [BsonElement("result")]
    //    [BsonRepresentation(BsonType.String)]
    //    public TestRunResult Result { get; set; }

    //    /// <summary>
    //    /// E.g. "Android 14 / Pixel 7 / Chrome 120"
    //    /// </summary>
    //    [BsonElement("environment")]
    //    [BsonIgnoreIfNull]
    //    public string? Environment { get; set; }

    //    /// <summary>
    //    /// The app build/version that was tested. E.g. "2.3.0-rc1"
    //    /// </summary>
    //    [BsonElement("appVersion")]
    //    [BsonIgnoreIfNull]
    //    public string? AppVersion { get; set; }

    //    /// <summary>
    //    /// Free-form notes from the tester about this specific run.
    //    /// </summary>
    //    [BsonElement("notes")]
    //    [BsonIgnoreIfNull]
    //    public string? Notes { get; set; }

    //    /// <summary>
    //    /// Per-step results. Optional — testers can log overall result without step detail.
    //    /// stepNumber values correspond to TestCase.Steps[].StepNumber.
    //    /// </summary>
    //    [BsonElement("stepResults")]
    //    public List<TestStepResult> StepResults { get; set; } = new();

    //    /// <summary>
    //    /// If the run failed and a bug was filed, link it here.
    //    /// </summary>
    //    [BsonElement("bugId")]
    //    [BsonRepresentation(BsonType.ObjectId)]
    //    [BsonIgnoreIfNull]
    //    public string? BugId { get; set; }

    //    /// <summary>
    //    /// How long the execution took, in seconds.
    //    /// </summary>
    //    [BsonElement("duration")]
    //    [BsonIgnoreIfNull]
    //    public int? Duration { get; set; }

    //    [BsonElement("executedAt")]
    //    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    //}

    //public class TestStepResult
    //{
    //    /// <summary>
    //    /// Matches TestCase.Steps[].StepNumber
    //    /// </summary>
    //    [BsonElement("stepNumber")]
    //    public int StepNumber { get; set; }

    //    [BsonElement("result")]
    //    [BsonRepresentation(BsonType.String)]
    //    public TestRunResult Result { get; set; }

    //    /// <summary>
    //    /// What actually happened at this step — useful when result is Failed or Blocked.
    //    /// </summary>
    //    [BsonElement("actualOutcome")]
    //    [BsonIgnoreIfNull]
    //    public string? ActualOutcome { get; set; }
    //}

    public class TestRun : BaseDocument
    {

        [BsonElement("projectId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId ProjectId { get; set; }

        /// <summary>The test case this run is an execution of.</summary>
        [BsonElement("testCaseId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId TestCaseId { get; set; }

        /// <summary>Denormalised for display — avoids a lookup on test case.</summary>
        [BsonElement("testCaseTitle")]
        public string TestCaseTitle { get; set; } = string.Empty;

        [BsonElement("testCaseCaseNumber")]
        public int TestCaseCaseNumber { get; set; }

        // ── Who ran it ──
        [BsonElement("executedById")]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId ExecutedById { get; set; }

        [BsonElement("executedByName")]
        public string ExecutedByName { get; set; } = string.Empty;

        [BsonElement("executedByEmail")]
        public string ExecutedByEmail { get; set; } = string.Empty;

        /// <summary>
        /// Overall result of this execution.
        /// passed | failed | blocked | skipped
        /// </summary>
        [BsonElement("result")]
        [BsonRepresentation(BsonType.String)]
        public string Result { get; set; } = string.Empty;

        /// <summary>Environment the test was run on. e.g. "Android 14 / Pixel 7"</summary>
        [BsonElement("environment")]
        [BsonIgnoreIfNull]
        public string? Environment { get; set; }

        /// <summary>The build or app version under test. e.g. "2.1.4"</summary>
        [BsonElement("appVersion")]
        [BsonIgnoreIfNull]
        public string? AppVersion { get; set; }

        /// <summary>Free-form notes from the tester about this specific run.</summary>
        [BsonElement("notes")]
        [BsonIgnoreIfNull]
        public string? Notes { get; set; }

        /// <summary>
        /// Per-step pass/fail results. Optional — tester may record overall result only.
        /// If provided, stepNumber must match the test case steps.
        /// </summary>
        [BsonElement("stepResults")]
        public List<TestRunStepResult> StepResults { get; set; } = new();

        /// <summary>
        /// If result is 'failed', the tester can manually link a bug they already filed.
        /// This is NOT auto-created — the tester files the bug separately then links it here.
        /// </summary>
        [BsonElement("linkedBugId")]
        [BsonIgnoreIfNull]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId? LinkedBugId { get; set; }

        /// <summary>Denormalised bug label for display. e.g. "BUG-007"</summary>
        [BsonElement("linkedBugLabel")]
        [BsonIgnoreIfNull]
        public string? LinkedBugLabel { get; set; }

        /// <summary>Execution time in seconds. Optional.</summary>
        [BsonElement("duration")]
        [BsonIgnoreIfNull]
        public int? Duration { get; set; }

        [BsonElement("executedAt")]
        public DateTime ExecutedAt { get; set; }
    }

    // ═══════════════════════════════════════════════════════════
    // TEST RUN STEP RESULT — embedded subdocument
    // Records pass/fail per step during a test run.
    // ═══════════════════════════════════════════════════════════
    public class TestRunStepResult
    {
        /// <summary>Matches the stepNumber from the parent test case's steps array.</summary>
        [BsonElement("stepNumber")]
        public int StepNumber { get; set; }

        /// <summary>passed | failed | blocked | skipped</summary>
        [BsonElement("result")]
        [BsonRepresentation(BsonType.String)]
        public string Result { get; set; } = string.Empty;

        /// <summary>What actually happened at this step — filled in when result is not 'passed'.</summary>
        [BsonElement("actualOutcome")]
        [BsonIgnoreIfNull]
        public string? ActualOutcome { get; set; }
    }

    public enum TestRunResult
    {
        /// <summary>Step or test completed successfully — all expectations met.</summary>
        Passed,

        /// <summary>Step or test did not meet expectations.</summary>
        Failed,

        /// <summary>Could not execute due to a dependency or environment issue.</summary>
        Blocked,

        /// <summary>Intentionally not executed in this run.</summary>
        Skipped
    }
}
