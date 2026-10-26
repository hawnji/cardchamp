using System;
using CardVault.Core.Content;
using CardVault.Core.Game;
using CardVault.Core.Save;
using CardVault.UnityLayer.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardVault.UnityLayer
{
    /// <summary>
    /// Runtime entry point. Creates itself after the first scene loads, so the project runs
    /// from an empty scene with no prefabs or scene wiring. Owns the game instance, the
    /// offline clock tick, and saving on pause/quit.
    /// </summary>
    public sealed class CardVaultApp : MonoBehaviour
    {
        private const string ContentResource = "Content/cardvault_content";
        private const string ThemeResource = "UI/CardVaultTheme";
        private const float TickIntervalSeconds = 15f;
        private const float AutosaveDelaySeconds = 1.5f;

        public static CardVaultApp Instance { get; private set; }

        public CardVaultGame Game { get; private set; }

        private SaveService _saves;
        private AppShell _shell;
        private float _nextTick;
        private float _saveAt = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null || FindObjectOfType<CardVaultApp>() != null) return;
            var go = new GameObject("CardVault");
            DontDestroyOnLoad(go);
            go.AddComponent<CardVaultApp>();
        }

        private void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.Portrait;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;

            var contentAsset = Resources.Load<TextAsset>(ContentResource);
            if (contentAsset == null)
            {
                Debug.LogError("Card Vault content is missing (Resources/" + ContentResource + ".json).");
                enabled = false;
                return;
            }
            var content = ContentDatabase.FromJson(contentAsset.text);

            _saves = new SaveService(new FileSaveStorage(Application.persistentDataPath));
            ulong seed = (ulong)DateTime.UtcNow.Ticks ^ (ulong)UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            Game = CardVaultGame.LoadOrCreate(content, _saves, seed, DateTime.UtcNow);
            foreach (var w in Game.LoadWarnings) Debug.LogWarning("[CardVault] " + w);

            CreateUi();
            RequestSave();
        }

        private void CreateUi()
        {
            var uiObject = new GameObject("CardVault UI");
            uiObject.SetActive(false);
            uiObject.transform.SetParent(transform, false);

            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1080, 1920);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0f; // match width: portrait phones of any height
            var theme = Resources.Load<ThemeStyleSheet>(ThemeResource);
            if (theme != null) panel.themeStyleSheet = theme;

            var document = uiObject.AddComponent<UIDocument>();
            document.panelSettings = panel;
            uiObject.SetActive(true);

            _shell = new AppShell(Game, document.rootVisualElement, RequestSave);
            _shell.Show();
        }

        /// <summary>Debounced save so rapid actions (opening several packs) write once.</summary>
        public void RequestSave() => _saveAt = Time.unscaledTime + AutosaveDelaySeconds;

        private void SaveNow()
        {
            if (Game == null) return;
            try
            {
                Game.Save(_saves);
                _saveAt = -1;
            }
            catch (Exception ex)
            {
                // The previous save and its backup are untouched; try again shortly.
                Debug.LogError("[CardVault] Save failed: " + ex.Message);
                _saveAt = Time.unscaledTime + 5f;
            }
        }

        private void Update()
        {
            if (Game == null) return;
            if (Time.unscaledTime >= _nextTick)
            {
                _nextTick = Time.unscaledTime + TickIntervalSeconds;
                long before = Game.Clock.Day;
                Game.Tick(DateTime.UtcNow);
                if (Game.Clock.Day != before) _shell?.Refresh();
                _shell?.UpdateTopBar();
            }
            if (_saveAt > 0 && Time.unscaledTime >= _saveAt) SaveNow();
            _shell?.HandleBackButton();
        }

        private void OnApplicationPause(bool paused)
        {
            if (Game == null) return;
            if (paused) SaveNow();
            else
            {
                Game.Tick(DateTime.UtcNow);
                _shell?.Refresh();
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) SaveNow();
        }

        private void OnApplicationQuit() => SaveNow();
    }
}
