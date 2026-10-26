using CardVault.Core.Content;
using CardVault.Core.Game;
using CardVault.Core.State;

namespace CardVault.Core.Progression
{
    /// <summary>Evaluates data-driven unlock conditions against persistent progression state.</summary>
    public sealed class UnlockEvaluator
    {
        private readonly GameContext _ctx;

        public UnlockEvaluator(GameContext ctx)
        {
            _ctx = ctx;
        }

        public long Stat(StatKey key)
            => _ctx.State.Progression.Stats.TryGetValue(key.ToString(), out var v) ? v : 0;

        public bool IsMet(UnlockCondition condition)
        {
            if (condition == null) return true;
            var p = _ctx.State.Progression;
            switch (condition.Kind)
            {
                case UnlockConditionKind.None: return true;
                case UnlockConditionKind.CollectorRank: return p.CollectorRank >= condition.Value;
                case UnlockConditionKind.SetsCompleted: return Stat(StatKey.SetsCompleted) >= condition.Value;
                case UnlockConditionKind.Achievement: return condition.RefId != null && p.Achievements.ContainsKey(condition.RefId);
                case UnlockConditionKind.Cosmetic: return condition.RefId != null && _ctx.State.Profile.UnlockedCosmetics.Contains(condition.RefId);
                case UnlockConditionKind.CardShowsCompleted: return Stat(StatKey.CardShowsCompleted) >= condition.Value;
                case UnlockConditionKind.CardsGraded: return Stat(StatKey.CardsGraded) >= condition.Value;
                case UnlockConditionKind.VintageProductsOpened: return Stat(StatKey.VintageProductsOpened) >= condition.Value;
                default: return false;
            }
        }

        public string Describe(UnlockCondition condition)
        {
            if (condition == null) return "Available";
            switch (condition.Kind)
            {
                case UnlockConditionKind.None: return "Available";
                case UnlockConditionKind.CollectorRank: return "Reach collector rank " + condition.Value;
                case UnlockConditionKind.SetsCompleted: return "Complete " + condition.Value + (condition.Value == 1 ? " set" : " sets");
                case UnlockConditionKind.Achievement:
                    return _ctx.Content.Achievements.TryGetValue(condition.RefId ?? "", out var a) ? "Achievement: " + a.Name : "Unlock an achievement";
                case UnlockConditionKind.Cosmetic:
                    return _ctx.Content.Cosmetics.TryGetValue(condition.RefId ?? "", out var c) ? "Unlock " + c.Name : "Unlock a cosmetic";
                case UnlockConditionKind.CardShowsCompleted: return "Complete " + condition.Value + (condition.Value == 1 ? " card show" : " card shows");
                case UnlockConditionKind.CardsGraded: return "Grade " + condition.Value + (condition.Value == 1 ? " card" : " cards");
                case UnlockConditionKind.VintageProductsOpened: return "Open " + condition.Value + " vintage product" + (condition.Value == 1 ? "" : "s");
                default: return condition.Kind.ToString();
            }
        }
    }
}
