using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BugTracker.Data.Entities
{
    /// <summary>
    /// Records a single execution of a TestCase.
    /// Each run is its own document, giving a full execution history over time.
    /// Collection: testRuns
    /// </summary>
    public class TestRun : BaseDocument
    {
        [BsonElement("projectId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string ProjectId { get; set; } = string.Empty;

        [BsonElement("testCaseId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string TestCaseId { get; set; } = string.Empty;

        [BsonElement("executedBy")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string ExecutedBy { get; set; } = string.Empty;

        [BsonElement("result")]
        [BsonRepresentation(BsonType.String)]
        public TestRunResult Result { get; set; }

        /// <summary>
        /// E.g. "Android 14 / Pixel 7 / Chrome 120"
        /// </summary>
        [BsonElement("environment")]
        [BsonIgnoreIfNull]
        public string? Environment { get; set; }

        /// <summary>
        /// The app build/version that was tested. E.g. "2.3.0-rc1"
        /// </summary>
        [BsonElement("appVersion")]
        [BsonIgnoreIfNull]
        public string? AppVersion { get; set; }

        /// <summary>
        /// Free-form notes from the tester about this specific run.
        /// </summary>
        [BsonElement("notes")]
        [BsonIgnoreIfNull]
        public string? Notes { get; set; }

        /// <summary>
        /// Per-step results. Optional — testers can log overall result without step detail.
        /// stepNumber values correspond to TestCase.Steps[].StepNumber.
        /// </summary>
        [BsonElement("stepResults")]
        public List<TestStepResult> StepResults { get; set; } = new();

        /// <summary>
        /// If the run failed and a bug was filed, link it here.
        /// </summary>
        [BsonElement("bugId")]
        [BsonRepresentation(BsonType.ObjectId)]
        [BsonIgnoreIfNull]
        public string? BugId { get; set; }

        /// <summary>
        /// How long the execution took, in seconds.
        /// </summary>
        [BsonElement("duration")]
        [BsonIgnoreIfNull]
        public int? Duration { get; set; }

        [BsonElement("executedAt")]
        public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    }

    public class TestStepResult
    {
        /// <summary>
        /// Matches TestCase.Steps[].StepNumber
        /// </summary>
        [BsonElement("stepNumber")]
        public int StepNumber { get; set; }

        [BsonElement("result")]
        [BsonRepresentation(BsonType.String)]
        public TestRunResult Result { get; set; }

        /// <summary>
        /// What actually happened at this step — useful when result is Failed or Blocked.
        /// </summary>
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
