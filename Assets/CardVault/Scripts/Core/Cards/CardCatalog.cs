using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Content;
using CardVault.Core.State;

namespace CardVault.Core.Cards
{
    /// <summary>
    /// Card template service: read-only queries over card definitions and the metadata
    /// derived from them (set, brand, category, vintage status, display labels).
    /// </summary>
    public sealed class CardCatalog
    {
        private readonly ContentDatabase _content;

        public CardCatalog(ContentDatabase content)
        {
            _content = content;
        }

        public CardTemplateDef Template(string templateId) => _content.Cards[templateId];
        public SetDef SetOf(string templateId) => _content.SetOfCard(templateId);
        public BrandDef BrandOf(string templateId) => _content.BrandOfSet(Template(templateId).SetId);
        public ParallelDef Parallel(string parallelId) => _content.Parallel(parallelId);
        public SubjectDef SubjectOf(string templateId)
        {
            var t = Template(templateId);
            return t.SubjectId != null && _content.Subjects.TryGetValue(t.SubjectId, out var s) ? s : null;
        }

        public CardCategory CategoryOf(string templateId) => SetOf(templateId).Category;
        public bool IsVintage(string templateId) => SetOf(templateId).IsVintage;

        public IEnumerable<CardTemplateDef> Matching(string setId, PackSlotOption option)
        {
            return _content.CardsInSet(setId).Where(c =>
                (option.Rarities == null || option.Rarities.Count == 0 || option.Rarities.Contains(c.Rarity)) &&
                (!option.RookieOnly || c.IsRookie) &&
                (!option.AutographOnly || c.IsAutograph) &&
                (!option.AlternateArtOnly || c.IsAlternateArt));
        }

        /// <summary>Player-facing name: "Mara Voss #12 Gold Refractor 07/25 · PSG 9.5".</summary>
        public string DisplayName(CardInstance card)
        {
            var t = Template(card.TemplateId);
            var p = Parallel(card.ParallelId);
            string name = t.Name + " #" + t.Number;
            if (!p.IsBase) name += " " + p.Name;
            if (card.IsSerialNumbered) name += " " + SerialLabel(card);
            if (card.IsGraded) name += " · Grade " + card.Grade.Value.ToString("0.#");
            return name;
        }

        public static string SerialLabel(CardInstance card)
        {
            if (!card.IsSerialNumbered) return null;
            if (card.IsOneOfOne) return "1/1";
            int width = card.PrintRun.ToString().Length;
            return card.SerialNumber.ToString().PadLeft(width, '0') + "/" + card.PrintRun;
        }
    }
}
