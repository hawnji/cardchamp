using System;
using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Common;

namespace CardVault.Core.Content
{
    /// <summary>Root JSON document for game content.</summary>
    public sealed class ContentPack
    {
        public int ContentVersion = 1;
        /// <summary>Calendar date that game time 0 maps to.</summary>
        public DateTime CalendarEpochUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public List<BrandDef> Brands = new List<BrandDef>();
        public List<UniverseDef> Universes = new List<UniverseDef>();
        public List<LeagueDef> Leagues = new List<LeagueDef>();
        public List<TeamDef> Teams = new List<TeamDef>();
        public List<SubjectDef> Subjects = new List<SubjectDef>();
        public List<ParallelDef> Parallels = new List<ParallelDef>();
        public List<CardTemplateDef> Cards = new List<CardTemplateDef>();
        public List<SetDef> Sets = new List<SetDef>();
        public List<ProductDef> Products = new List<ProductDef>();
        public List<NpcDef> Npcs = new List<NpcDef>();
        public List<CosmeticDef> Cosmetics = new List<CosmeticDef>();
        public List<ReleaseDef> Releases = new List<ReleaseDef>();
        public List<CardShowEventDef> CardShows = new List<CardShowEventDef>();
        public List<AchievementDef> Achievements = new List<AchievementDef>();
        public List<CollectorRankDef> CollectorRanks = new List<CollectorRankDef>();

        public ShowcaseConfig Showcase = new ShowcaseConfig();
        public GradingConfig Grading = new GradingConfig();
        public MarketConfig Market = new MarketConfig();
        public EconomyConfig Economy = new EconomyConfig();
        public XpConfig Xp = new XpConfig();
    }

    /// <summary>
    /// Indexed, validated, read-only view of a ContentPack. All lookups are by stable ID.
    /// </summary>
    public sealed class ContentDatabase
    {
        public const string BaseParallelId = "base";

        public ContentPack Pack { get; }

        public IReadOnlyDictionary<string, BrandDef> Brands { get; }
        public IReadOnlyDictionary<string, UniverseDef> Universes { get; }
        public IReadOnlyDictionary<string, LeagueDef> Leagues { get; }
        public IReadOnlyDictionary<string, TeamDef> Teams { get; }
        public IReadOnlyDictionary<string, SubjectDef> Subjects { get; }
        public IReadOnlyDictionary<string, ParallelDef> Parallels { get; }
        public IReadOnlyDictionary<string, CardTemplateDef> Cards { get; }
        public IReadOnlyDictionary<string, SetDef> Sets { get; }
        public IReadOnlyDictionary<string, ProductDef> Products { get; }
        public IReadOnlyDictionary<string, NpcDef> Npcs { get; }
        public IReadOnlyDictionary<string, CosmeticDef> Cosmetics { get; }
        public IReadOnlyDictionary<string, ReleaseDef> Releases { get; }
        public IReadOnlyDictionary<string, CardShowEventDef> CardShows { get; }
        public IReadOnlyDictionary<string, AchievementDef> Achievements { get; }

        public IReadOnlyList<CollectorRankDef> Ranks { get; }

        private readonly Dictionary<string, List<CardTemplateDef>> _cardsBySet;

        public ContentDatabase(ContentPack pack)
        {
            Pack = pack ?? throw new ArgumentNullException(nameof(pack));

            Brands = Index(pack.Brands, b => b.Id, "brand");
            Universes = Index(pack.Universes, u => u.Id, "universe");
            Leagues = Index(pack.Leagues, l => l.Id, "league");
            Teams = Index(pack.Teams, t => t.Id, "team");
            Subjects = Index(pack.Subjects, s => s.Id, "subject");
            Parallels = Index(pack.Parallels, p => p.Id, "parallel");
            Cards = Index(pack.Cards, c => c.Id, "card");
            Sets = Index(pack.Sets, s => s.Id, "set");
            Products = Index(pack.Products, p => p.Id, "product");
            Npcs = Index(pack.Npcs, n => n.Id, "npc");
            Cosmetics = Index(pack.Cosmetics, c => c.Id, "cosmetic");
            Releases = Index(pack.Releases, r => r.Id, "release");
            CardShows = Index(pack.CardShows, e => e.Id, "card show");
            Achievements = Index(pack.Achievements, a => a.Id, "achievement");
            Ranks = pack.CollectorRanks.OrderBy(r => r.Level).ToList();

            _cardsBySet = new Dictionary<string, List<CardTemplateDef>>();
            foreach (var set in pack.Sets)
            {
                _cardsBySet[set.Id] = set.CardTemplateIds.Select(id => Cards[id]).ToList();
            }

            Validate();
        }

        public static ContentDatabase FromJson(string json) => new ContentDatabase(Json.Deserialize<ContentPack>(json));

        public IReadOnlyList<CardTemplateDef> CardsInSet(string setId)
            => _cardsBySet.TryGetValue(setId, out var list) ? list : (IReadOnlyList<CardTemplateDef>)Array.Empty<CardTemplateDef>();

        public ParallelDef Parallel(string parallelId)
            => Parallels.TryGetValue(parallelId ?? BaseParallelId, out var p) ? p : Parallels[BaseParallelId];

        public SetDef SetOfCard(string templateId) => Sets[Cards[templateId].SetId];

        public BrandDef BrandOfSet(string setId) => Brands[Sets[setId].BrandId];

        public SetMilestoneDef Milestone(string setId, string milestoneId)
            => Sets.TryGetValue(setId, out var set) ? set.Milestones.FirstOrDefault(m => m.Id == milestoneId) : null;

        private static IReadOnlyDictionary<string, T> Index<T>(IEnumerable<T> items, Func<T, string> key, string kind)
        {
            var dict = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                string id = key(item);
                if (string.IsNullOrEmpty(id)) throw new ContentException($"A {kind} definition is missing its id.");
                if (dict.ContainsKey(id)) throw new ContentException($"Duplicate {kind} id '{id}'.");
                dict[id] = item;
            }
            return dict;
        }

        private void Validate()
        {
            if (!Parallels.ContainsKey(BaseParallelId))
                throw new ContentException("Content must define the 'base' parallel.");

            foreach (var card in Pack.Cards)
            {
                Require(Sets.ContainsKey(card.SetId), $"Card '{card.Id}' references unknown set '{card.SetId}'.");
                Require(string.IsNullOrEmpty(card.SubjectId) || Subjects.ContainsKey(card.SubjectId),
                    $"Card '{card.Id}' references unknown subject '{card.SubjectId}'.");
            }

            foreach (var set in Pack.Sets)
            {
                Require(Brands.ContainsKey(set.BrandId), $"Set '{set.Id}' references unknown brand '{set.BrandId}'.");
                foreach (var id in set.CardTemplateIds)
                {
                    Require(Cards.ContainsKey(id), $"Set '{set.Id}' lists unknown card '{id}'.");
                    Require(Cards[id].SetId == set.Id, $"Card '{id}' is listed in set '{set.Id}' but belongs to '{Cards[id].SetId}'.");
                }
                foreach (var pid in set.ParallelIds)
                    Require(Parallels.ContainsKey(pid), $"Set '{set.Id}' lists unknown parallel '{pid}'.");
                var milestoneIds = new HashSet<string>();
                foreach (var m in set.Milestones)
                {
                    Require(milestoneIds.Add(m.Id), $"Set '{set.Id}' has duplicate milestone '{m.Id}'.");
                    if (m.Kind == SetMilestoneKind.ParallelComplete)
                        Require(Parallels.ContainsKey(m.ParallelId ?? ""), $"Milestone '{m.Id}' references unknown parallel.");
                    ValidateReward(m.Reward, $"milestone '{set.Id}/{m.Id}'");
                }
            }

            foreach (var product in Pack.Products)
            {
                Require(Sets.ContainsKey(product.SetId), $"Product '{product.Id}' references unknown set '{product.SetId}'.");
                if (product.IsContainer)
                    Require(Products.ContainsKey(product.ContainsProductId), $"Product '{product.Id}' contains unknown product '{product.ContainsProductId}'.");
                foreach (var slot in product.Slots)
                foreach (var option in slot.Options)
                    Require(option.ParallelId == null || Parallels.ContainsKey(option.ParallelId),
                        $"Product '{product.Id}' references unknown parallel '{option.ParallelId}'.");
            }

            foreach (var release in Pack.Releases)
            {
                Require(Sets.ContainsKey(release.SetId), $"Release '{release.Id}' references unknown set '{release.SetId}'.");
                Require(release.ReleaseAt >= release.AnnounceAt, $"Release '{release.Id}' is released before it is announced.");
                Require(release.RetireAt == 0 || release.RetireAt > release.ReleaseAt, $"Release '{release.Id}' retires before release.");
                Require(release.ArchiveAt == 0 || release.ArchiveAt >= release.RetireAt, $"Release '{release.Id}' archives before retiring.");
                foreach (var p in release.Products)
                    Require(Products.ContainsKey(p.ProductId), $"Release '{release.Id}' references unknown product '{p.ProductId}'.");
                foreach (var e in release.EventIds)
                    Require(CardShows.ContainsKey(e), $"Release '{release.Id}' links unknown card show '{e}'.");
            }

            foreach (var show in Pack.CardShows)
            {
                var ids = new HashSet<string>();
                foreach (var c in show.Challenges) Require(ids.Add("c:" + c.Id), $"Card show '{show.Id}' has duplicate challenge '{c.Id}'.");
                foreach (var r in show.Rewards)
                {
                    Require(ids.Add("r:" + r.Id), $"Card show '{show.Id}' has duplicate reward '{r.Id}'.");
                    ValidateReward(r.Reward, $"card show reward '{show.Id}/{r.Id}'");
                }
                foreach (var v in show.Vendors)
                {
                    Require(Npcs.ContainsKey(v.NpcId), $"Vendor '{v.Id}' references unknown NPC '{v.NpcId}'.");
                    foreach (var s in v.Stock)
                    {
                        Require(ids.Add("s:" + s.Id), $"Card show '{show.Id}' has duplicate stock id '{s.Id}'.");
                        Require((s.ProductId != null) ^ (s.TemplateId != null), $"Stock '{s.Id}' must sell exactly one product or card.");
                        Require(s.ProductId == null || Products.ContainsKey(s.ProductId), $"Stock '{s.Id}' references unknown product.");
                        Require(s.TemplateId == null || Cards.ContainsKey(s.TemplateId), $"Stock '{s.Id}' references unknown card.");
                    }
                }
            }

            foreach (var area in Pack.Showcase.Areas)
                Require(area.BaseSlots >= 0, $"Showcase area '{area.Id}' has negative slots.");
        }

        private void ValidateReward(RewardDef reward, string where)
        {
            if (reward == null) return;
            foreach (var c in reward.CosmeticIds)
                Require(Cosmetics.ContainsKey(c), $"Reward in {where} references unknown cosmetic '{c}'.");
            foreach (var card in reward.Cards)
            {
                Require(Cards.ContainsKey(card.TemplateId), $"Reward in {where} references unknown card '{card.TemplateId}'.");
                Require(card.ParallelId == null || Parallels.ContainsKey(card.ParallelId), $"Reward in {where} references unknown parallel.");
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new ContentException(message);
        }
    }

    public sealed class ContentException : Exception
    {
        public ContentException(string message) : base(message) { }
    }
}
