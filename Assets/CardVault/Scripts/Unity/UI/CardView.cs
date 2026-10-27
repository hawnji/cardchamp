using System.Collections.Generic;
using CardVault.Core.Cards;
using CardVault.Core.Content;
using CardVault.Core.Game;
using CardVault.Core.State;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardVault.UnityLayer.UI
{
    /// <summary>
    /// Loads card/product artwork on demand from Resources/Art/{artKey}. Only artwork for cards
    /// actually on screen is requested; the cache is bounded so browsing a large collection
    /// does not keep every texture alive.
    /// </summary>
    public static class ArtCache
    {
        private const int Capacity = 96;
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        private static readonly LinkedList<string> Order = new LinkedList<string>();
        private static readonly HashSet<string> Missing = new HashSet<string>();

        public static Texture2D Get(string artKey)
        {
            if (string.IsNullOrEmpty(artKey) || Missing.Contains(artKey)) return null;
            if (Cache.TryGetValue(artKey, out var tex))
            {
                Order.Remove(artKey);
                Order.AddFirst(artKey);
                return tex;
            }
            tex = Resources.Load<Texture2D>("Art/" + artKey);
            if (tex == null)
            {
                Missing.Add(artKey);
                return null;
            }
            Cache[artKey] = tex;
            Order.AddFirst(artKey);
            while (Order.Count > Capacity)
            {
                var evict = Order.Last.Value;
                Order.RemoveLast();
                if (Cache.TryGetValue(evict, out var old)) Resources.UnloadAsset(old);
                Cache.Remove(evict);
            }
            return tex;
        }
    }

    /// <summary>
    /// Procedural premium card face: team/element colours, parallel foil border, serial stamp,
    /// rookie/autograph marks and a graded slab treatment. Real artwork is layered in when an
    /// art asset exists for the card's art key.
    /// </summary>
    public sealed class CardView : VisualElement
    {
        private readonly VisualElement _frame;
        private readonly VisualElement _art;
        private readonly VisualElement _artShade;
        private readonly VisualElement _shine;
        private readonly Label _name;
        private readonly Label _meta;
        private readonly Label _serial;
        private readonly Label _slab;
        private readonly VisualElement _badges;
        private IVisualElementScheduledItem _shimmer;
        private float _shinePos;

        public string CardInstanceId { get; private set; }

        public CardView()
        {
            style.flexDirection = FlexDirection.Column;
            _slab = Ui.Text("", 24, Theme.Background, bold: true, wrap: false);
            _slab.style.unityTextAlign = TextAnchor.MiddleCenter;
            _slab.style.backgroundColor = new Color(0.92f, 0.94f, 0.97f);
            Theme.Padding(_slab, 6, 6);
            _slab.style.borderTopLeftRadius = 12;
            _slab.style.borderTopRightRadius = 12;
            Add(_slab);

            _frame = new VisualElement();
            _frame.style.flexGrow = 1;
            _frame.style.overflow = Overflow.Hidden;
            Theme.Round(_frame, 18);
            Theme.Padding(_frame, 10, 10);
            Add(_frame);

            _art = new VisualElement();
            _art.style.flexGrow = 1;
            Theme.Round(_art, 10);
            _art.style.overflow = Overflow.Hidden;
            _art.style.justifyContent = Justify.FlexEnd;
#if UNITY_2022_1_OR_NEWER
            _art.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
#endif
            _frame.Add(_art);

            _artShade = new VisualElement();
            _artShade.style.position = Position.Absolute;
            _artShade.style.left = 0;
            _artShade.style.right = 0;
            _artShade.style.bottom = 0;
            _artShade.style.height = Length.Percent(45);
            _art.Add(_artShade);

            _shine = new VisualElement();
            _shine.style.position = Position.Absolute;
            _shine.style.top = 0;
            _shine.style.bottom = 0;
            _shine.style.width = Length.Percent(18);
            _shine.style.backgroundColor = new Color(1, 1, 1, 0.16f);
            _shine.style.rotate = new Rotate(new Angle(18, AngleUnit.Degree));
            _shine.pickingMode = PickingMode.Ignore;
            _art.Add(_shine);

            _badges = Ui.Row();
            _badges.style.position = Position.Absolute;
            _badges.style.top = 10;
            _badges.style.left = 10;
            _art.Add(_badges);

            _serial = Ui.Text("", 24, Color.white, bold: true, wrap: false);
            _serial.style.position = Position.Absolute;
            _serial.style.top = 10;
            _serial.style.right = 10;
            _serial.style.backgroundColor = new Color(0, 0, 0, 0.55f);
            Theme.Round(_serial, 8);
            Theme.Padding(_serial, 2, 8);
            _art.Add(_serial);

            var plate = Ui.Column();
            plate.style.backgroundColor = new Color(0, 0, 0, 0.55f);
            Theme.Padding(plate, 8, 12);
            _name = Ui.Text("", 28, Color.white, bold: true);
            _meta = Ui.Text("", 22, new Color(0.85f, 0.85f, 0.85f));
            plate.Add(_name);
            plate.Add(_meta);
            _art.Add(plate);

            pickingMode = PickingMode.Position;
        }

        public void SetSize(float width)
        {
            style.width = width;
            style.height = width * 1.4f + (_slab.style.display.value == DisplayStyle.Flex ? 40 : 0);
        }

        public void Bind(CardVaultGame game, CardInstance card, float width, bool reducedEffects)
        {
            CardInstanceId = card.InstanceId;
            var t = game.Content.Cards[card.TemplateId];
            var set = game.Content.Sets[t.SetId];
            var parallel = game.Content.Parallel(card.ParallelId);
            var subject = game.Catalog.SubjectOf(card.TemplateId);

            Color primary = Theme.Hex("#3A3F4B"), secondary = Theme.Hex("#1B1D22");
            if (subject?.TeamId != null && game.Content.Teams.TryGetValue(subject.TeamId, out var team))
            {
                primary = Theme.Hex(team.PrimaryColor, primary);
                secondary = Theme.Hex(team.SecondaryColor, secondary);
            }
            else if (subject?.Element != null)
            {
                primary = ElementColor(subject.Element);
                secondary = Color.Lerp(primary, Color.black, 0.6f);
            }
            if (set.IsVintage) primary = Color.Lerp(primary, Theme.Hex("#C9B38A"), 0.45f);

            _art.style.backgroundColor = primary;
            _artShade.style.backgroundColor = new Color(secondary.r, secondary.g, secondary.b, 0.55f);
            var tex = ArtCache.Get(t.ArtKey);
            _art.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground(StyleKeyword.None);

            Color frame = parallel.IsBase ? Theme.Hex("#E6E6E6") : Theme.Hex(parallel.ColorHex, Theme.Accent);
            _frame.style.backgroundColor = frame;

            _name.text = t.Name;
            _meta.text = "#" + t.Number + " · " + set.Name + (parallel.IsBase ? "" : " · " + parallel.Name);
            string serial = CardCatalog.SerialLabel(card);
            _serial.text = serial ?? "";
            _serial.style.display = serial != null ? DisplayStyle.Flex : DisplayStyle.None;

            _badges.Clear();
            if (t.IsRookie) _badges.Add(Ui.Badge("RC", Theme.Accent));
            if (t.IsAutograph) _badges.Add(Ui.Badge("AUTO", Theme.Info));
            if (t.IsAlternateArt) _badges.Add(Ui.Badge("ALT", Theme.Hex("#B57EDC")));
            if (!card.Tradable) _badges.Add(Ui.Badge("PROMO", Theme.Positive));

            if (card.IsGraded)
            {
                _slab.text = "GRADE " + card.Grade.Value.ToString("0.#");
                _slab.style.display = DisplayStyle.Flex;
                Theme.BorderColor(this, new Color(0.92f, 0.94f, 0.97f), 6);
                Theme.Round(this, 18);
            }
            else
            {
                _slab.style.display = DisplayStyle.None;
                Theme.BorderColor(this, Color.clear, 0);
            }
            SetSize(width);

            bool foil = !parallel.IsBase || t.IsAlternateArt || t.Rarity >= Rarity.SuperRare;
            _shine.style.display = foil ? DisplayStyle.Flex : DisplayStyle.None;
            _shimmer?.Pause();
            if (foil && !reducedEffects)
            {
                _shinePos = -20;
                if (_shimmer == null) _shimmer = schedule.Execute(StepShine).Every(40);
                else _shimmer.Resume();
            }
            else
            {
                _shine.style.left = Length.Percent(40);
            }
        }

        private void StepShine()
        {
            _shinePos += 1.6f;
            if (_shinePos > 140) _shinePos = -40;
            _shine.style.left = Length.Percent(_shinePos);
        }

        public void Unbind()
        {
            _shimmer?.Pause();
            CardInstanceId = null;
            _art.style.backgroundImage = new StyleBackground(StyleKeyword.None);
        }

        private static Color ElementColor(string element)
        {
            switch (element)
            {
                case "Fire": return Theme.Hex("#C2410C");
                case "Water": return Theme.Hex("#0E7490");
                case "Storm": return Theme.Hex("#4338CA");
                case "Rune": return Theme.Hex("#7C3AED");
                case "Nature": return Theme.Hex("#15803D");
                case "Shadow": return Theme.Hex("#312E81");
                case "Stone": return Theme.Hex("#57534E");
                case "Ice": return Theme.Hex("#0369A1");
                case "Command": return Theme.Hex("#1D4ED8");
                case "Speed": return Theme.Hex("#DB2777");
                case "Psionic": return Theme.Hex("#9333EA");
                case "Tech": return Theme.Hex("#0F766E");
                default: return Theme.Hex("#475569");
            }
        }
    }

    /// <summary>Reuses card views so long lists and the showcase never allocate per card.</summary>
    public sealed class CardViewPool
    {
        private readonly Stack<CardView> _free = new Stack<CardView>();
        private readonly List<CardView> _inUse = new List<CardView>();

        public CardView Get()
        {
            var view = _free.Count > 0 ? _free.Pop() : new CardView();
            _inUse.Add(view);
            return view;
        }

        public void ReleaseAll()
        {
            foreach (var v in _inUse)
            {
                v.Unbind();
                v.RemoveFromHierarchy();
                v.UnregisterAllClickHandlers();
                _free.Push(v);
            }
            _inUse.Clear();
        }
    }

    internal static class CardViewExtensions
    {
        private sealed class Handler
        {
            public EventCallback<ClickEvent> Callback;
        }

        public static void OnTap(this CardView view, System.Action action)
        {
            view.UnregisterAllClickHandlers();
            var h = new Handler { Callback = _ => action() };
            view.userData = h;
            view.RegisterCallback(h.Callback);
        }

        public static void UnregisterAllClickHandlers(this CardView view)
        {
            if (view.userData is Handler h) view.UnregisterCallback(h.Callback);
            view.userData = null;
        }
    }
}
