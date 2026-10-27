using System.Linq;
using CardVault.Core.Cards;
using NUnit.Framework;

namespace CardVault.Tests
{
    /// <summary>Pre-generated card art lookup (tools/art writes images under these keys).</summary>
    public class CardArtTests
    {
        [Test]
        public void BaseParallelUsesTemplateArtOnly()
        {
            var content = TestContent.Database;
            var card = content.Cards["fh26-001"];
            Assert.That(CardArt.Candidates(card, content.Parallel("base")), Is.EqualTo(new[] { "fh26/001" }));
        }

        [Test]
        public void ParallelPrefersVariantArtThenFallsBackToBase()
        {
            var content = TestContent.Database;
            var card = content.Cards["fh26-001"];
            Assert.That(CardArt.Candidates(card, content.Parallel("black1")), Is.EqualTo(new[] { "fh26/001_black1", "fh26/001" }));
        }

        [Test]
        public void EveryCardAndSetHasItsOwnArtKey()
        {
            var content = TestContent.Database;
            var cardKeys = content.Cards.Values.Select(c => c.ArtKey).ToList();
            Assert.That(cardKeys, Has.All.Not.Null.And.Not.Empty);
            Assert.That(cardKeys, Is.Unique);
            Assert.That(content.Sets.Values.Select(s => s.ArtKey), Is.Unique);
            // Variant keys must never collide with a base key.
            Assert.That(cardKeys.Any(k => k.Contains(CardArt.VariantSeparator)), Is.False);
        }
    }
}
