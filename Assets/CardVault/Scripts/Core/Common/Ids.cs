using System;

namespace CardVault.Core.Common
{
    /// <summary>
    /// Owner references. Ownership is always a stable ID, never a display name, so a future
    /// backend can map "player" to an authenticated account ID without changing the model.
    /// </summary>
    public static class Owners
    {
        public const string Player = "player";
        public const string Unallocated = "system:unallocated";
        public const string NpcPrefix = "npc:";

        public static string Npc(string npcId) => NpcPrefix + npcId;
        public static bool IsNpc(string ownerId) => ownerId != null && ownerId.StartsWith(NpcPrefix, StringComparison.Ordinal);
        public static string NpcIdOf(string ownerId) => IsNpc(ownerId) ? ownerId.Substring(NpcPrefix.Length) : null;
    }

    /// <summary>
    /// Generates IDs from a persisted counter. IDs are unique within a save, stable across
    /// reloads, and prefixed by kind so they are readable in logs and tests.
    /// </summary>
    public sealed class IdGenerator
    {
        private readonly IdState _state;

        public IdGenerator(IdState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public string Next(string prefix)
        {
            _state.NextSequence++;
            return prefix + "-" + _state.Namespace + "-" + _state.NextSequence.ToString("D7");
        }
    }

    public sealed class IdState
    {
        /// <summary>Random per-save namespace so IDs from two installs never collide if merged online later.</summary>
        public string Namespace = "local";
        public long NextSequence;
    }

    public static class IdPrefixes
    {
        public const string Card = "card";
        public const string Sealed = "seal";
        public const string Transaction = "tx";
        public const string History = "hist";
        public const string Listing = "lst";
        public const string Trade = "trd";
        public const string Grading = "grd";
        public const string Action = "act";
        public const string Notification = "ntf";
    }
}
