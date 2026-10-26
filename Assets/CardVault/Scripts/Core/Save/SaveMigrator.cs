using System;
using System.Collections.Generic;
using System.Linq;
using CardVault.Core.State;
using Newtonsoft.Json.Linq;

namespace CardVault.Core.Save
{
    /// <summary>
    /// Step-by-step schema migrations on the raw JSON tree. Each step upgrades exactly one
    /// version, so any old save can be brought forward. Steps must be pure and idempotent.
    /// </summary>
    public sealed class SaveMigrator
    {
        private readonly Dictionary<int, Action<JObject, List<string>>> _steps = new Dictionary<int, Action<JObject, List<string>>>
        {
            { 1, MigrateV1ToV2 },
        };

        public JObject Migrate(JObject root, int fromVersion, List<string> log)
        {
            if (fromVersion > GameState.CurrentSchemaVersion)
                throw new InvalidOperationException("This save was made by a newer version of Card Vault (schema " + fromVersion + ").");

            for (int v = fromVersion; v < GameState.CurrentSchemaVersion; v++)
            {
                if (!_steps.TryGetValue(v, out var step))
                    throw new InvalidOperationException("No migration from save schema " + v + ".");
                step(root, log);
                root["schemaVersion"] = v + 1;
                log?.Add("Migrated save schema " + v + " -> " + (v + 1));
            }
            return root;
        }

        /// <summary>
        /// Schema 1 (core collection only) had no provenance, set records, showcase, calendar,
        /// card show or progression data. Missing containers deserialize to empty defaults; this
        /// step backfills one honest history entry per existing card so every owned card has a
        /// timeline, and seeds historical set progress from current ownership.
        /// </summary>
        private static void MigrateV1ToV2(JObject root, List<string> log)
        {
            var cards = root["cards"] as JObject ?? new JObject();
            var history = root["history"] as JObject;
            if (history == null)
            {
                history = new JObject { ["entries"] = new JArray(), ["nextSequence"] = 0 };
                root["history"] = history;
            }
            var entries = (JArray)(history["entries"] ?? (history["entries"] = new JArray()));
            var withHistory = new HashSet<string>(entries.Select(e => (string)e["cardInstanceId"]));
            long sequence = history.Value<long?>("nextSequence") ?? 0;
            long now = root["clock"]?.Value<long?>("gameSeconds") ?? 0;

            int backfilled = 0;
            foreach (var prop in cards.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (withHistory.Contains(prop.Name)) continue;
                var card = (JObject)prop.Value;
                entries.Add(new JObject
                {
                    ["entryId"] = "hist:migration-v2:" + prop.Name + ":MigrationBackfill",
                    ["cardInstanceId"] = prop.Name,
                    ["type"] = HistoryEventType.MigrationBackfill.ToString(),
                    ["time"] = card.Value<long?>("createdAt") ?? now,
                    ["sequence"] = ++sequence,
                    ["transactionId"] = "migration-v2",
                    ["newOwnerId"] = card.Value<string>("ownerId"),
                    ["newCondition"] = card["condition"],
                    ["newGrade"] = card["grade"],
                    ["setId"] = card.Value<string>("originSetId"),
                    ["description"] = "Record created when your collection was upgraded; earlier details were not tracked.",
                });
                backfilled++;
            }
            history["nextSequence"] = sequence;

            var processed = root["processedTransactions"] as JArray ?? new JArray();
            if (!processed.Any(t => (string)t == "migration-v2")) processed.Add("migration-v2");
            root["processedTransactions"] = processed;

            log?.Add("Backfilled history for " + backfilled + " cards.");
        }
    }
}
