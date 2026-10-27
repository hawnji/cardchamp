using System.Collections.Generic;
using CardVault.Core.Content;

namespace CardVault.Core.Cards
{
    /// <summary>
    /// Resolves which pre-generated art asset a card shows. A parallel can have its own art
    /// (saved as {artKey}_{parallelId}, used for 1/1s); everything else falls back to the
    /// template's base art, and the renderer falls back to procedural art when neither exists.
    /// </summary>
    public static class CardArt
    {
        public const string VariantSeparator = "_";

        public static string VariantKey(string artKey, string parallelId) => artKey + VariantSeparator + parallelId;

        /// <summary>Art keys to try in order, most specific first.</summary>
        public static IReadOnlyList<string> Candidates(CardTemplateDef template, ParallelDef parallel)
        {
            if (string.IsNullOrEmpty(template.ArtKey)) return new string[0];
            if (parallel == null || parallel.IsBase) return new[] { template.ArtKey };
            return new[] { VariantKey(template.ArtKey, parallel.Id), template.ArtKey };
        }
    }
}
