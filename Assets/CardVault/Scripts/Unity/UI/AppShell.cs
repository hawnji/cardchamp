using System;
using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Common;
using CardVault.Core.Game;
using CardVault.UnityLayer.UI.Screens;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardVault.UnityLayer.UI
{
    /// <summary>A full-screen page. Screens render from game state every time they are shown or refreshed.</summary>
    public abstract class GameScreen
    {
        protected AppShell Shell { get; private set; }
        protected CardVaultGame Game => Shell.Game;

        public abstract string Title { get; }

        internal void Attach(AppShell shell) => Shell = shell;

        /// <summary>Builds the screen's content. Called again after every state change.</summary>
        public abstract void Build(VisualElement root);

        /// <summary>Called before the screen is rebuilt or closed (return pooled views here).</summary>
        public virtual void Teardown() { }
    }

    /// <summary>
    /// App frame: top bar (wallet, notifications), scrollable content, five-tab bottom
    /// navigation, a back stack for secondary screens, modal sheets and toasts. UI never holds
    /// ownership or reward state; every screen reads from CardVaultGame.
    /// </summary>
    public sealed class AppShell
    {
        public static readonly string[] TabNames = { "Home", "Shop", "Collection", "Market", "Profile" };

        public CardVaultGame Game { get; }

        private readonly VisualElement _root;
        private readonly Action _requestSave;
        private readonly VisualElement _topBar;
        private readonly Label _wallet;
        private readonly Button _bell;
        private readonly Label _screenTitle;
        private readonly Button _back;
        private readonly ScrollView _content;
        private readonly VisualElement _nav;
        private readonly VisualElement _modalLayer;
        private readonly Label _toast;
        private IVisualElementScheduledItem _toastHide;

        private readonly GameScreen[] _tabs;
        private readonly Stack<GameScreen> _stack = new Stack<GameScreen>();
        private int _tab;

        public AppShell(CardVaultGame game, VisualElement root, Action requestSave)
        {
            Game = game;
            _root = root;
            _requestSave = requestSave;

            _root.style.flexGrow = 1;
            _root.style.backgroundColor = Theme.Background;
            ApplyFont(_root);
            ApplySafeArea();

            _topBar = Ui.Row();
            _topBar.style.backgroundColor = Theme.Surface;
            Theme.Padding(_topBar, 16, Theme.Gutter);
            _topBar.style.minHeight = Theme.TouchTarget + 20;
            _back = Ui.Button("Back", Back, Ui.ButtonKind.Ghost);
            _back.style.marginRight = 16;
            _screenTitle = Ui.Text("", Theme.FontTitle, bold: true, wrap: false);
            _screenTitle.style.flexGrow = 1;
            _screenTitle.style.flexShrink = 1;
            _screenTitle.style.overflow = Overflow.Hidden;
            _wallet = Ui.Text("", Theme.FontSmall, Theme.Accent, bold: true, wrap: false);
            _wallet.style.marginRight = 16;
            _bell = Ui.Button("Inbox", () => Push(new NotificationsScreen()), Ui.ButtonKind.Secondary);
            _topBar.Add(_back);
            _topBar.Add(_screenTitle);
            _topBar.Add(_wallet);
            _topBar.Add(_bell);
            _root.Add(_topBar);

            _content = Ui.Scroll();
            Theme.Padding(_content.contentContainer, Theme.Gutter, Theme.Gutter);
            _root.Add(_content);

            _nav = Ui.Row();
            _nav.style.backgroundColor = Theme.Surface;
            _nav.style.borderTopColor = Theme.Border;
            _nav.style.borderTopWidth = 2;
            Theme.Padding(_nav, 8, 8);
            _root.Add(_nav);

            _modalLayer = new VisualElement();
            _modalLayer.style.position = Position.Absolute;
            _modalLayer.style.left = 0;
            _modalLayer.style.right = 0;
            _modalLayer.style.top = 0;
            _modalLayer.style.bottom = 0;
            _modalLayer.style.backgroundColor = Theme.Overlay;
            _modalLayer.style.justifyContent = Justify.FlexEnd;
            _modalLayer.style.display = DisplayStyle.None;
            _modalLayer.RegisterCallback<ClickEvent>(e => { if (e.target == _modalLayer) CloseModal(); });
            _root.Add(_modalLayer);

            _toast = Ui.Text("", Theme.FontBody, Theme.Text, bold: true);
            _toast.style.position = Position.Absolute;
            _toast.style.left = Theme.Gutter;
            _toast.style.right = Theme.Gutter;
            _toast.style.bottom = 260;
            _toast.style.backgroundColor = Theme.SurfaceRaised;
            Theme.BorderColor(_toast, Theme.Accent, 3);
            Theme.Round(_toast, Theme.Radius);
            Theme.Padding(_toast, 24, 32);
            _toast.style.display = DisplayStyle.None;
            _toast.pickingMode = PickingMode.Ignore;
            _root.Add(_toast);

            _tabs = new GameScreen[] { new HomeScreen(), new ShopScreen(), new CollectionScreen(), new MarketScreen(), new ProfileScreen() };
            foreach (var t in _tabs) t.Attach(this);
            BuildNav();
        }

        private static void ApplyFont(VisualElement root)
        {
#if UNITY_2022_2_OR_NEWER
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
            var font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
            if (font != null) root.style.unityFont = font;
        }

        private void ApplySafeArea()
        {
            var safe = Screen.safeArea;
            if (Screen.height <= 0 || Screen.width <= 0) return;
            float scale = 1080f / Screen.width;
            _root.style.paddingTop = (Screen.height - safe.yMax) * scale;
            _root.style.paddingBottom = safe.yMin * scale;
        }

        public void Show()
        {
            SwitchTab(0);
            var celebration = Game.SetRewards.PendingCelebrations.FirstOrDefault();
            if (celebration != null) SetCompletionCelebration.Show(this, celebration);
        }

        private void BuildNav()
        {
            _nav.Clear();
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                var b = Ui.Button(TabNames[i], () => SwitchTab(index), i == _tab && _stack.Count == 0 ? Ui.ButtonKind.Primary : Ui.ButtonKind.Secondary);
                b.style.flexGrow = 1;
                b.style.flexBasis = 0;
                b.style.fontSize = 28;
                Theme.Padding(b, 4, 4);
                Theme.Margin(b, 4, 4);
                _nav.Add(b);
            }
        }

        public void SwitchTab(int index)
        {
            Current?.Teardown();
            _stack.Clear();
            _tab = index;
            Render(scrollToTop: true);
        }

        public void Push(GameScreen screen)
        {
            Current?.Teardown();
            screen.Attach(this);
            _stack.Push(screen);
            Render(scrollToTop: true);
        }

        public void Back()
        {
            if (_modalLayer.style.display == DisplayStyle.Flex)
            {
                CloseModal();
                return;
            }
            if (_stack.Count == 0) return;
            _stack.Pop().Teardown();
            Render(scrollToTop: false);
        }

        /// <summary>Android back button support.</summary>
        public void HandleBackButton()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Back();
        }

        private GameScreen Current => _stack.Count > 0 ? _stack.Peek() : _tabs?[_tab];

        /// <summary>Re-renders the current screen from game state.</summary>
        public void Refresh() => Render(scrollToTop: false);

        private void Render(bool scrollToTop)
        {
            var screen = Current;
            screen.Teardown();
            _content.Clear();
            _screenTitle.text = screen.Title;
            _back.style.display = _stack.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            var container = Ui.Column();
            _content.Add(container);
            screen.Build(container);
            container.Add(Ui.Spacer(80));
            if (scrollToTop) _content.scrollOffset = Vector2.zero;
            BuildNav();
            UpdateTopBar();
        }

        public void UpdateTopBar()
        {
            _wallet.text = Game.Economy.Credits.ToString("N0") + " cr · " + Game.Economy.EventTickets + " tix\n" +
                           Game.Clock.CalendarNow.ToString("MMM d, yyyy");
            int unread = Game.UnreadNotifications.Count();
            _bell.text = unread > 0 ? "Inbox " + unread : "Inbox";
        }

        // ------------------------------------------------------------- actions

        /// <summary>Runs a game operation, shows its outcome, refreshes and schedules a save.</summary>
        public bool Run(Func<OpResult> operation, string successMessage = null)
        {
            var result = operation();
            if (result.Ok)
            {
                if (successMessage != null) Toast(successMessage);
                _requestSave();
                Refresh();
                ShowPendingCelebration();
                return true;
            }
            Toast(result.Message ?? result.Error.ToString());
            return false;
        }

        public void AfterChange()
        {
            _requestSave();
            Refresh();
            ShowPendingCelebration();
        }

        private void ShowPendingCelebration()
        {
            var setId = Game.SetRewards.PendingCelebrations.FirstOrDefault();
            if (setId != null) SetCompletionCelebration.Show(this, setId);
        }

        public void Toast(string message)
        {
            _toast.text = message;
            _toast.style.display = DisplayStyle.Flex;
            _toastHide?.Pause();
            _toastHide = _toast.schedule.Execute(() => _toast.style.display = DisplayStyle.None).StartingIn(2600);
        }

        /// <summary>Shows a bottom sheet. Tapping outside or Back closes it.</summary>
        public void ShowModal(string title, VisualElement body)
        {
            _modalLayer.Clear();
            var sheet = Ui.Column();
            sheet.style.backgroundColor = Theme.Surface;
            sheet.style.borderTopLeftRadius = 36;
            sheet.style.borderTopRightRadius = 36;
            Theme.Padding(sheet, Theme.Gutter, Theme.Gutter);
            sheet.style.maxHeight = Length.Percent(85);
            var header = Ui.Row(Ui.Title(title), Ui.Spacer(), Ui.Button("Close", CloseModal, Ui.ButtonKind.Secondary));
            sheet.Add(header);
            var scroll = Ui.Scroll();
            scroll.Add(body);
            sheet.Add(scroll);
            _modalLayer.Add(sheet);
            _modalLayer.style.display = DisplayStyle.Flex;
        }

        public void CloseModal()
        {
            _modalLayer.style.display = DisplayStyle.None;
            _modalLayer.Clear();
        }

        public bool ReducedEffects => Game.Showcase.Settings.ReducedEffects;
    }
}
