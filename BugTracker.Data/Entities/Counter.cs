using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MongoDB.Bson.Serialization.Attributes;

namespace BugTracker.Data.Entities
{
    /// <summary>
    /// Utility collection for generating atomic sequential numbers per project.
    /// Used to produce human-readable IDs like BUG-042 and TC-011.
    ///
    /// Collection: counters
    ///
    /// Usage in C# (call this before inserting a Bug or TestCase):
    ///
    ///     var filter = Builders&lt;Counter&gt;.Filter.Eq(c => c.Id, $"{projectId}_bugs");
    ///     var update = Builders&lt;Counter&gt;.Update.Inc(c => c.Seq, 1);
    ///     var options = new FindOneAndUpdateOptions&lt;Counter&gt;
    ///     {
    ///         ReturnDocument = ReturnDocument.After,
    ///         IsUpsert = true   // creates the counter doc if it doesn't exist yet
    ///     };
    ///     var counter = await _counters.FindOneAndUpdateAsync(filter, update, options);
    ///     int nextBugNumber = counter.Seq;
    ///
    /// Key naming convention:
    ///     "{projectId}_bugs"      → for Bug.BugNumber
    ///     "{projectId}_testcases" → for TestCase.CaseNumber
    /// </summary>
    public class Counter
    {
        /// <summary>
        /// Natural string key. Format: "{projectId}_{entity}"
        /// E.g. "64f1a2b3c4d5e6f7a8b9c0d1_bugs"
        /// </summary>
        [BsonId]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Current counter value. Starts at 1 on first use (IsUpsert creates it at 0, $inc makes it 1).
        /// </summary>
        [BsonElement("seq")]
        public int Seq { get; set; }
    }
}
