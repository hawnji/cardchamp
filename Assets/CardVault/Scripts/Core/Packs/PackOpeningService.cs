using System;
using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Cards;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Game;
using CardVault.Core.History;
using CardVault.Core.State;

namespace CardVault.Core.Packs
{
    public sealed class PackOpeningResult
    {
        public string TransactionId;
        public string ProductId;
        public List<CardInstance> Cards = new List<CardInstance>();
        /// <summary>Packs produced when a box/bundle is opened.</summary>
        public List<SealedProductInstance> ContainedProducts = new List<SealedProductInstance>();
    }

    /// <summary>Sealed product inventory (unopened packs, boxes and tins).</summary>
    public sealed class SealedInventoryService
    {
        private readonly GameContext _ctx;

        public SealedInventoryService(GameContext ctx)
        {
            _ctx = ctx;
        }

        public SealedProductInstance Get(string id)
            => id != null && _ctx.State.SealedProducts.TryGetValue(id, out var s) ? s : null;

        public IEnumerable<SealedProductInstance> UnopenedBy(string ownerId)
            => _ctx.State.SealedProducts.Values.Where(s => s.OwnerId == ownerId && !s.Opened);

        public IEnumerable<SealedProductInstance> PlayerUnopened => UnopenedBy(Owners.Player);

        public SealedProductInstance Create(Transaction tx, string productId, string ownerId, string sourceRef)
        {
            if (!_ctx.Content.Products.ContainsKey(productId)) throw new ArgumentException("Unknown product " + productId);
            var item = new SealedProductInstance
            {
                InstanceId = _ctx.Ids.Next(IdPrefixes.Sealed),
                ProductId = productId,
                OwnerId = ownerId,
                AcquiredAt = _ctx.Now,
                SourceRef = sourceRef,
            };
            _ctx.State.SealedProducts[item.InstanceId] = item;
            return item;
        }
    }

    /// <summary>
    /// Opens sealed products using configurable slot odds. Each pulled card is a new unique
    /// instance with Created + PackOpened history recorded in the same transaction.
    /// </summary>
    public sealed class PackOpeningService
    {
        private readonly GameContext _ctx;
        private readonly CardInventoryService _inventory;
        private readonly SealedInventoryService _sealed;
        private readonly CardHistoryService _history;
        private readonly CardCatalog _catalog;

        public PackOpeningService(GameContext ctx, CardInventoryService inventory, SealedInventoryService sealedInventory,
            CardHistoryService history, CardCatalog catalog)
        {
            _ctx = ctx;
            _inventory = inventory;
            _sealed = sealedInventory;
            _history = history;
            _catalog = catalog;
        }

        public OpResult<PackOpeningResult> Open(string sealedInstanceId, string transactionId = null)
        {
            if (_ctx.IsProcessed(transactionId)) return OpResult<PackOpeningResult>.Fail(OpError.AlreadyProcessed, "This product was already opened.");

            var item = _sealed.Get(sealedInstanceId);
            if (item == null) return OpResult<PackOpeningResult>.Fail(OpError.NotFound, "Product not found.");
            if (item.OwnerId != Owners.Player) return OpResult<PackOpeningResult>.Fail(OpError.NotOwned, "You do not own this product.");
            if (item.Opened) return OpResult<PackOpeningResult>.Fail(OpError.InvalidState, "This product has already been opened.");

            var product = _ctx.Content.Products[item.ProductId];
            var set = _ctx.Content.Sets[product.SetId];

            using (var tx = _ctx.Begin(transactionId))
            {
                var result = new PackOpeningResult { TransactionId = tx.Id, ProductId = product.Id };
                item.Opened = true;
                item.OpenedAt = _ctx.Now;

                if (product.IsContainer)
                {
                    for (int i = 0; i < product.ContainsCount; i++)
                        result.ContainedProducts.Add(_sealed.Create(tx, product.ContainsProductId, Owners.Player, item.InstanceId));
                }
                else
                {
                    foreach (var slot in product.Slots)
                    for (int i = 0; i < slot.Count; i++)
                    {
                        var card = PullCard(tx, product, slot);
                        if (card != null) result.Cards.Add(card);
                    }
                }

                _ctx.RecordAction(tx, GameActionType.PackOpened, a =>
                {
                    a.ProductId = product.Id;
                    a.SetId = set.Id;
                    a.BrandId = set.BrandId;
                    a.IsVintage = product.IsVintage || set.IsVintage;
                    a.Category = set.Category;
                    a.RefId = item.InstanceId;
                });
                foreach (var card in result.Cards)
                    _ctx.RecordCardAction(tx, GameActionType.CardAcquired, card, product.Id, item.InstanceId);

                tx.Commit();
                return OpResult<PackOpeningResult>.Success(result);
            }
        }

        private CardInstance PullCard(Transaction tx, ProductDef product, PackSlotDef slot)
        {
            var weights = slot.Options.Select(o => o.Weight).ToList();
            int optionIndex = _ctx.Rng.PickWeightedIndex(weights);
            if (optionIndex < 0) return null;
            var option = slot.Options[optionIndex];

            var candidates = _catalog.Matching(product.SetId, option).ToList();
            if (candidates.Count == 0)
                candidates = _ctx.Content.CardsInSet(product.SetId).ToList();
            if (candidates.Count == 0) return null;

            string parallelId = option.ParallelId ?? ContentDatabase.BaseParallelId;
            var template = _ctx.Rng.Pick(candidates);

            // Numbered parallels are finite. If the rolled card's run is exhausted, try another
            // card in the same treatment, then fall back to the base version.
            if (!_inventory.HasFreeSerial(template.Id, parallelId))
            {
                var withStock = candidates.Where(c => _inventory.HasFreeSerial(c.Id, parallelId)).ToList();
                if (withStock.Count > 0) template = _ctx.Rng.Pick(withStock);
                else parallelId = ContentDatabase.BaseParallelId;
            }

            double condition = _ctx.Rng.Range(product.MinCondition, product.MaxCondition);
            var card = _inventory.Create(tx, new CardCreationRequest
            {
                TemplateId = template.Id,
                ParallelId = parallelId,
                OwnerId = Owners.Player,
                Condition = condition,
                OriginRef = product.Id,
                ProductId = product.Id,
            });
            if (card == null) return null;

            _history.Append(tx, card.InstanceId, HistoryEventType.PackOpened, e =>
            {
                e.PreviousOwnerId = Owners.Unallocated;
                e.NewOwnerId = Owners.Player;
                e.NewCondition = card.Condition;
                e.ProductId = product.Id;
                e.SetId = template.SetId;
            });
            return card;
        }
    }
}
