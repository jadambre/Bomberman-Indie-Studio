using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BombermanIndieStudio
{
    /// <summary>The complete, resolution-independent interface. Game rules remain in Simulation.</summary>
    public sealed class GameUI : MonoBehaviour
    {
        static readonly Color Navy = Hex("101D2A"), Deep = Hex("0B1520"), Card = Hex("1B2C3C");
        static readonly Color Ivory = Hex("F7F0DF"), Muted = Hex("A7B8C6"), Amber = Hex("FFC566");
        static readonly Color Mint = Hex("92E0CA"), Coral = Hex("FF8C83"), Line = Hex("344859");
        enum Subpage { None, Settings, Controls, Overwrite }
        GameApp app;
        Canvas canvas;
        CanvasScaler scaler;
        RectTransform root, content;
        Font font;
        Sprite rounded;
        Texture2D roundedTexture;
        GameScreen shownScreen = (GameScreen)(-1);
        GameLanguage shownLanguage = (GameLanguage)(-1);
        Subpage page, shownPage = (Subpage)(-1);
        int playerCount = 1, shownPlayers, shownStage = -1;
        bool shownSave;
        readonly List<Selectable> selectable = new List<Selectable>();
        Text clockText, botsText, scoreText, noticeText, introNumber, introHeading, introBriefing;
        RectTransform introPanel;
        readonly Text[] playerStatus = new Text[2], playerStats = new Text[2];
        readonly Image[] playerStripe = new Image[2];
        float lastRebuildTime;

        public void Initialize(GameApp gameApp)
        {
            app = gameApp;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            rounded = MakeRoundedSprite();
            var go = new GameObject("Bomberman Indie Studio Interface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = Screen.width / (float)Mathf.Max(1, Screen.height) >= 16f / 9f ? 1 : 0;
            root = go.GetComponent<RectTransform>();
            if (EventSystem.current == null)
                new GameObject("Interface Event System", typeof(EventSystem), typeof(StandaloneInputModule));
            Refresh();
        }

        void Update()
        {
            if (app == null) return;
            scaler.matchWidthOrHeight = Screen.width / (float)Mathf.Max(1, Screen.height) >= 16f / 9f ? 1 : 0;
            // StandaloneInputModule covers arrows/Enter. Tab also traverses settings sliders and buttons.
            if (app.Screen != GameScreen.Playing && Input.GetKeyDown(KeyCode.Tab) && selectable.Count > 0 && EventSystem.current != null)
            {
                int current = selectable.FindIndex(s => s != null && s.gameObject == EventSystem.current.currentSelectedGameObject);
                int direction = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1;
                for (int offset = 1; offset <= selectable.Count; offset++)
                {
                    int next = (current + offset * direction + selectable.Count * 2) % selectable.Count;
                    if (selectable[next] != null && selectable[next].IsInteractable() && selectable[next].gameObject.activeInHierarchy)
                    { EventSystem.current.SetSelectedGameObject(selectable[next].gameObject); break; }
                }
            }
        }

        public bool CloseSubpage()
        {
            if (page == Subpage.None) return false;
            page = Subpage.None;
            app.PlayUISound(false);
            Rebuild();
            return true;
        }

        public void Refresh()
        {
            if (app == null || root == null) return;
            int stage = app.Sim == null ? -1 : app.Sim.State.Stage;
            if (shownScreen != app.Screen)
            {
                page = Subpage.None;
                Rebuild();
            }
            else if (shownPage != page || shownPlayers != playerCount || shownSave != app.HasSave || shownStage != stage || shownLanguage != app.Language)
                Rebuild();
            SessionState state = app.Sim == null ? null : app.Sim.State;
            if (state != null)
            {
                int seconds = Mathf.Max(0, Mathf.CeilToInt(state.TimeRemaining));
                if (clockText != null)
                {
                    clockText.text = (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
                    clockText.color = seconds <= 30 ? Coral : Ivory;
                }
                if (botsText != null) botsText.text = app.Sim.AliveBots.ToString("00");
                if (scoreText != null) scoreText.text = state.Score.ToString("N0", GameText.Culture);
                for (int index = 0; index < 2; index++)
                {
                    if (playerStatus[index] == null) continue;
                    ActorState actor = state.Actors.Find(a => a.HumanIndex == index);
                    if (actor == null) continue;
                    playerStatus[index].text = GameText.T(actor.Alive ? "IN THE ARENA" : "OUT · RETURNS NEXT STAGE");
                    playerStatus[index].color = actor.Alive ? (index == 0 ? Mint : Amber) : Coral;
                    playerStripe[index].color = actor.Alive ? (index == 0 ? Mint : Amber) : Coral;
                    int activeBombs = 0;
                    for (int bombIndex = 0; bombIndex < state.Bombs.Count; bombIndex++)
                        if (state.Bombs[bombIndex].OwnerId == actor.Id) activeBombs++;
                    playerStats[index].text = actor.Alive
                        ? GameText.Format("BOMBS  <color=#F7F0DF>{0}/{1}</color>     BLAST  <color=#F7F0DF>{2}</color>     SPEED  <color=#F7F0DF>{3}</color>     PASS  <color={4}>{5}</color>",
                            Mathf.Max(0, actor.BombCapacity - activeBombs), actor.BombCapacity, actor.FireRange,
                            actor.Speed.ToString("0.0", GameText.Culture), actor.WallPass ? "#92E0CA" : "#F7F0DF", GameText.T(actor.WallPass ? "ON" : "OFF"))
                        : GameText.T("Your partner can finish the stage. Both players return together.");
                }
                if (introPanel != null)
                {
                    bool showIntro = app.Screen == GameScreen.Playing && app.IntroRemaining > 0 && page == Subpage.None;
                    introPanel.gameObject.SetActive(showIntro);
                    if (showIntro) introNumber.text = Mathf.CeilToInt(app.IntroRemaining).ToString();
                }
            }
            if (noticeText != null)
            {
                noticeText.text = app.LastNotice ?? "";
                noticeText.transform.parent.gameObject.SetActive(page == Subpage.None && !string.IsNullOrEmpty(app.LastNotice));
            }
        }

        void Rebuild()
        {
            if (root == null) return;
            string previousSelection = shownLanguage != app.Language && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null
                ? EventSystem.current.currentSelectedGameObject.name : null;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            if (content != null) { content.gameObject.SetActive(false); Destroy(content.gameObject); }
            content = Stretch(root, "Current screen");
            selectable.Clear();
            clockText = botsText = scoreText = noticeText = introNumber = null;
            introPanel = null;
            for (int i = 0; i < 2; i++) { playerStatus[i] = null; playerStats[i] = null; playerStripe[i] = null; }
            shownScreen = app.Screen;
            shownLanguage = app.Language;
            shownPage = page;
            shownPlayers = playerCount;
            shownSave = app.HasSave;
            shownStage = app.Sim == null ? -1 : app.Sim.State.Stage;
            lastRebuildTime = Time.unscaledTime;
            RectTransform primary = Stretch(content, "Screen content");
            switch (app.Screen)
            {
                case GameScreen.MainMenu: BuildMainMenu(primary); break;
                case GameScreen.Playing: BuildHud(primary); break;
                case GameScreen.Paused:
                    RectTransform pausedHud = Stretch(primary, "Paused HUD");
                    BuildHud(pausedHud);
                    CanvasGroup hudGroup = pausedHud.gameObject.AddComponent<CanvasGroup>();
                    hudGroup.interactable = false; hudGroup.blocksRaycasts = false;
                    selectable.Clear();
                    BuildPause(primary);
                    break;
                case GameScreen.StageClear: BuildResult(primary, false, false); break;
                case GameScreen.GameOver: BuildResult(primary, true, false); break;
                case GameScreen.Victory: BuildResult(primary, false, true); break;
            }
            if (page != Subpage.None)
            {
                var group = primary.gameObject.AddComponent<CanvasGroup>();
                group.interactable = false;
                group.blocksRaycasts = false;
                selectable.Clear();
                if (page == Subpage.Settings) BuildSettings();
                else if (page == Subpage.Controls) BuildControls();
                else BuildOverwrite();
            }
            BuildNotice();
            if (EventSystem.current != null) EventSystem.current.sendNavigationEvents = app.Screen != GameScreen.Playing || page != Subpage.None;
            if (EventSystem.current != null && app.Screen != GameScreen.Playing)
            {
                Selectable previous = selectable.Find(s => s != null && s.name == previousSelection && s.IsInteractable());
                if (previous != null) { EventSystem.current.SetSelectedGameObject(previous.gameObject); return; }
                foreach (Selectable control in selectable)
                    if (control.IsInteractable()) { EventSystem.current.SetSelectedGameObject(control.gameObject); break; }
            }
        }

        void BuildMainMenu(RectTransform parent)
        {
            RectTransform left = Anchor(parent, "Menu shade", new Vector2(0, .5f), new Vector2(0, .5f), 0, 0, 685, 1080);
            left.anchorMin = new Vector2(0, 0); left.anchorMax = new Vector2(0, 1); left.sizeDelta = new Vector2(685, 0);
            Fill(left, new Color(Deep.r, Deep.g, Deep.b, .97f), false);
            RectTransform edge = Anchor(left, "Amber edge", new Vector2(1, .5f), new Vector2(1, .5f), 0, 0, 2, 1080);
            edge.anchorMin = new Vector2(1, 0); edge.anchorMax = new Vector2(1, 1); edge.sizeDelta = new Vector2(2, 0);
            Fill(edge, new Color(Amber.r, Amber.g, Amber.b, .26f), false);
            TextAt(left, "AN ARCADE CO-OP ADVENTURE", 66, 64, 550, 30, 18, Mint, FontStyle.Bold);
            TextAt(left, "BOMBERMAN", 62, 142, 585, 76, 62, Ivory, FontStyle.Bold);
            TextAt(left, "INDIE STUDIO", 68, 214, 547, 34, 28, Mint, FontStyle.Bold);
            ImageAt(left, "Wordmark underline", 68, 252, 66, 5, Amber, false);
            TextAt(left, "A tiny fuse.\nA big adventure.", 66, 286, 535, 118, 42, Ivory, FontStyle.Bold);
            TextAt(left, "Five arenas. Clever rivals. One way forward.", 68, 419, 535, 34, 21, Muted);
            TextAt(left, "CHOOSE YOUR CREW", 68, 481, 530, 28, 16, Muted, FontStyle.Bold);
            ButtonAt(left, "1 PLAYER", 68, 522, 256, 60, () => SelectPlayers(1), playerCount == 1 ? Mint : Card, playerCount == 1 ? Navy : Ivory);
            ButtonAt(left, "2 PLAYERS · CO-OP", 338, 522, 277, 60, () => SelectPlayers(2), playerCount == 2 ? Mint : Card, playerCount == 2 ? Navy : Ivory);
            Button start = ButtonAt(left, "START NEW ADVENTURE     >", 68, 603, 547, 76, RequestNewCampaign, Amber, Navy, 24);
            // Place the primary action first in keyboard traversal, without changing its visual position.
            selectable.Remove(start); selectable.Insert(0, start);
            Button continueButton = ButtonAt(left, "CONTINUE", 68, 696, 547, 86, () => app.ContinueCampaign(), Card, Ivory, 24, TextAnchor.UpperLeft);
            continueButton.interactable = app.HasSave;
            TextAt(continueButton.GetComponent<RectTransform>(), app.HasSave ? app.SaveSummary : "Your adventure will be saved here.", 24, 47, 500, 28, 16, app.HasSave ? Mint : Muted);
            ButtonAt(left, "HOW TO PLAY", 68, 806, 266, 57, () => Open(Subpage.Controls), Card, Ivory, 19);
            ButtonAt(left, "SETTINGS", 348, 806, 267, 57, () => Open(Subpage.Settings), Card, Ivory, 19);
            ButtonAt(left, "QUIT GAME", 68, 881, 547, 45, () => app.Quit(), new Color(0, 0, 0, 0), Muted, 17);
            RectTransform foot = Anchor(left, "Menu footnote", new Vector2(0, 0), new Vector2(0, 0), 68, 35, 560, 72);
            TextAt(foot, "ARROWS  Navigate      ENTER  Select      ESC  Back", 0, 0, 560, 26, 15, Muted);
            TextAt(foot, "LOCAL PLAY  /  FRIENDLY FIRE ON", 0, 40, 560, 25, 14, Mint, FontStyle.Bold);

            RectTransform heading = Anchor(parent, "Diorama heading", new Vector2(1, 1), new Vector2(1, 1), -62, -64, 1080, 85);
            TextAt(heading, "THE CAMPAIGN", 0, 0, 700, 27, 17, Mint, FontStyle.Bold);
            TextAt(heading, "SMALL ARENAS. BIG POSSIBILITIES.", 0, 34, 1080, 43, 31, Ivory, FontStyle.Bold);
            RectTransform route = Anchor(parent, "Campaign route", new Vector2(1, 0), new Vector2(1, 0), -58, 55, 1114, 172);
            TextAt(route, "YOUR ROUTE TO THE CORE", 0, 0, 790, 29, 17, Ivory, FontStyle.Bold);
            TextAt(route, "01 / 05", 963, 0, 150, 29, 16, Mint, FontStyle.Bold, TextAnchor.MiddleRight);
            for (int i = 0; i < 5; i++)
            {
                RectTransform card = Panel(route, "Stage " + (i + 1), i * 225, 49, 214, 114, i == 0 ? Card : new Color(Navy.r, Navy.g, Navy.b, .88f));
                TextAt(card, "0" + (i + 1), 18, 16, 165, 31, 25, i == 0 ? Amber : Muted, FontStyle.Bold);
                TextAt(card, GameText.StageName(i), 18, 64, 181, 28, 17, Ivory, FontStyle.Bold);
                if (i == 0) ImageAt(card, "Current stage", 18, 101, 35, 3, Amber, false);
            }
        }

        void BuildHud(RectTransform parent)
        {
            if (app.Sim == null) return;
            SessionState state = app.Sim.State;
            RectTransform top = Anchor(parent, "Top HUD", new Vector2(.5f, 1), new Vector2(.5f, 1), 0, 0, 1920, 112);
            top.anchorMin = new Vector2(0, 1); top.anchorMax = new Vector2(1, 1); top.sizeDelta = new Vector2(0, 112);
            Fill(top, new Color(Deep.r, Deep.g, Deep.b, .96f), false);
            ImageAt(top, "Stage accent", 38, 29, 4, 58, Amber, false);
            TextAt(top, GameText.Format("STAGE {0:00} / 05", state.Stage + 1), 59, 20, 440, 26, 15, Amber, FontStyle.Bold);
            TextAt(top, StageName(state.Stage), 57, 48, 460, 43, 31, Ivory, FontStyle.Bold);
            RectTransform progress = Anchor(top, "Journey progress", new Vector2(.5f, .5f), new Vector2(.5f, .5f), -60, 0, 450, 54);
            for (int i = 0; i < 5; i++)
            {
                if (i < 4) ImageAt(progress, "Connector", 40 + i * 90, 25, 61, 3, i < state.Stage ? Mint : Line, false);
                RectTransform node = Panel(progress, "Stage node", i * 90, 4, 43, 43, i == state.Stage ? Amber : i < state.Stage ? Mint : Card);
                TextAt(node, (i + 1).ToString(), 0, 0, 43, 43, 18, i <= state.Stage ? Navy : Muted, FontStyle.Bold, TextAnchor.MiddleCenter);
            }
            RectTransform numbers = Anchor(top, "Run metrics", new Vector2(1, .5f), new Vector2(1, .5f), -144, 0, 405, 86);
            TextAt(numbers, "RIVALS", 0, 12, 110, 23, 13, Muted, FontStyle.Bold);
            botsText = TextAt(numbers, "00", 0, 37, 105, 43, 29, Ivory, FontStyle.Bold);
            TextAt(numbers, "SCORE", 120, 12, 125, 23, 13, Muted, FontStyle.Bold);
            scoreText = TextAt(numbers, "0", 120, 39, 140, 40, 26, Ivory, FontStyle.Bold);
            TextAt(numbers, "TIME LEFT", 280, 12, 125, 23, 13, Muted, FontStyle.Bold);
            clockText = TextAt(numbers, "03:00", 280, 36, 125, 44, 32, Ivory, FontStyle.Bold);
            RectTransform pauseAnchor = Anchor(top, "Pause anchor", new Vector2(1, .5f), new Vector2(1, .5f), -28, 0, 91, 62);
            ButtonAt(pauseAnchor, "II", 0, 0, 91, 62, () => app.Pause(), Card, Ivory, 28);

            RectTransform bottom = Anchor(parent, "Player HUD", new Vector2(.5f, 0), new Vector2(.5f, 0), 0, 0, 1920, 123);
            bottom.anchorMin = new Vector2(0, 0); bottom.anchorMax = new Vector2(1, 0); bottom.sizeDelta = new Vector2(0, 123);
            Fill(bottom, new Color(Deep.r, Deep.g, Deep.b, .97f), false);
            BuildPlayerCard(bottom, 0, new Vector2(0, .5f), new Vector2(0, .5f), 32, 0);
            if (state.PlayerCount > 1) BuildPlayerCard(bottom, 1, new Vector2(1, .5f), new Vector2(1, .5f), -32, 0);
            else
            {
                RectTransform tip = Anchor(bottom, "Solo reminder", new Vector2(1, .5f), new Vector2(1, .5f), -40, 0, 612, 75);
                TextAt(tip, "MAKE A PATH. LEAVE AN EXIT.", 0, 4, 612, 30, 18, Amber, FontStyle.Bold, TextAnchor.MiddleRight);
                TextAt(tip, "Break crates for upgrades. Every explosion is dangerous.", 0, 41, 612, 29, 17, Muted, FontStyle.Normal, TextAnchor.MiddleRight);
            }
            RectTransform controls = Anchor(bottom, "Quick controls", new Vector2(.5f, .5f), new Vector2(.5f, .5f), 0, 0, 480, 67);
            TextAt(controls, "ESC  Pause     F5  Save", 0, 1, 480, 28, 16, Ivory, FontStyle.Bold, TextAnchor.MiddleCenter);
            TextAt(controls, state.PlayerCount > 1 ? "COOPERATE · WATCH YOUR BLASTS" : "ONE PLAYER · FIVE ARENAS", 0, 37, 480, 23, 13, Muted, FontStyle.Bold, TextAnchor.MiddleCenter);

            introPanel = Anchor(parent, "Stage countdown", new Vector2(.5f, .5f), new Vector2(.5f, .5f), 0, 14, 690, 310);
            Fill(introPanel, new Color(Deep.r, Deep.g, Deep.b, .95f));
            introHeading = TextAt(introPanel, GameText.Format("STAGE {0:00}  /  {1}", state.Stage + 1, StageName(state.Stage)), 35, 25, 620, 40, 25, Amber, FontStyle.Bold, TextAnchor.MiddleCenter);
            introNumber = TextAt(introPanel, "3", 100, 71, 490, 134, 102, Ivory, FontStyle.Bold, TextAnchor.MiddleCenter);
            introBriefing = TextAt(introPanel, GameText.StageBriefing(state.Stage), 35, 224, 620, 56, 21, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
            introPanel.gameObject.SetActive(app.Screen == GameScreen.Playing && app.IntroRemaining > 0);
        }

        void BuildPlayerCard(RectTransform parent, int index, Vector2 anchor, Vector2 pivot, float x, float y)
        {
            RectTransform card = Anchor(parent, "Player " + (index + 1), anchor, pivot, x, y, 650, 96);
            Fill(card, Card);
            playerStripe[index] = ImageAt(card, "Player stripe", 0, 16, 4, 64, index == 0 ? Mint : Amber, false);
            TextAt(card, GameText.Format("P{0}", index + 1), 18, 12, 65, 37, 27, index == 0 ? Mint : Amber, FontStyle.Bold);
            playerStatus[index] = TextAt(card, "IN THE ARENA", 84, 15, 340, 26, 15, Mint, FontStyle.Bold);
            TextAt(card, index == 0 ? "ARROWS + SPACE" : "ZQSD + F", 405, 14, 226, 27, 14, Muted, FontStyle.Bold, TextAnchor.MiddleRight);
            playerStats[index] = TextAt(card, "", 20, 57, 613, 25, 15, Muted);
        }

        void BuildPause(RectTransform parent)
        {
            Dim(parent, .68f);
            RectTransform panel = CenterPanel(parent, "Pause menu", 674, 753);
            TextAt(panel, "TAKE A BREATHER", 46, 34, 580, 27, 16, Mint, FontStyle.Bold);
            TextAt(panel, "PAUSED", 43, 75, 580, 69, 52, Ivory, FontStyle.Bold);
            TextAt(panel, "Your arena is right where you left it.", 46, 151, 580, 36, 21, Muted);
            ButtonAt(panel, "RESUME ADVENTURE", 46, 217, 582, 73, () => app.Resume(), Amber, Navy, 23);
            ButtonAt(panel, "SAVE & MAIN MENU", 46, 307, 582, 68, () => app.SaveAndMenu(), Card, Ivory, 21);
            ButtonAt(panel, "RETRY STAGE", 46, 392, 582, 68, () => app.RestartStage(), Card, Ivory, 21);
            TextAt(panel, "Retry restores your upgrades from the start of this stage.", 46, 469, 582, 31, 15, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
            ButtonAt(panel, "SETTINGS", 46, 534, 284, 60, () => Open(Subpage.Settings), Card, Ivory, 19);
            ButtonAt(panel, "HOW TO PLAY", 344, 534, 284, 60, () => Open(Subpage.Controls), Card, Ivory, 19);
            ImageAt(panel, "Pause divider", 46, 624, 582, 1, Line, false);
            TextAt(panel, "ESC  Resume      F5  Quick save", 46, 650, 582, 35, 17, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
            TextAt(panel, "Progress is also saved at the start of each stage.", 46, 697, 582, 27, 15, Mint, FontStyle.Normal, TextAnchor.MiddleCenter);
        }

        void BuildResult(RectTransform parent, bool lost, bool victory)
        {
            if (app.Sim == null) return;
            SessionState state = app.Sim.State;
            Dim(parent, .79f);
            RectTransform panel = CenterPanel(parent, "Stage result", 870, 872);
            Color accent = lost ? Coral : victory ? Amber : Mint;
            TextAt(panel, lost ? "ANOTHER SPARK. ANOTHER CHANCE." : victory ? "FIVE ARENAS. ONE BRILLIANT RUN." : "THE WAY FORWARD IS OPEN", 48, 34, 774, 29, 16, accent, FontStyle.Bold, TextAnchor.MiddleCenter);
            TextAt(panel, lost ? "RUN INTERRUPTED" : victory ? "CORE CONQUERED" : "STAGE CLEAR", 36, 88, 798, 75, lost ? 48 : 55, Ivory, FontStyle.Bold, TextAnchor.MiddleCenter);
            TextAt(panel, lost ? (state.TimeRemaining <= 0 ? "The clock ran out. Find your rhythm and try again." : "Every fuse teaches you something. You have another shot.") : victory ? "From the overgrowth to the Core — you made it." : GameText.Format("{0} is clear. Upgrades reset in the next arena.", StageName(state.Stage)), 56, 172, 758, 67, 21, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
            BuildResultProgress(panel, state.Stage, lost);
            RectTransform stats = Panel(panel, "Run recap", 48, 355, 774, 119, Card);
            ResultStat(stats, "SCORE", state.Score.ToString("N0", GameText.Culture), 0);
            ResultStat(stats, "TIME LEFT", FormatTime(state.TimeRemaining), 258);
            ResultStat(stats, "CREW", state.PlayerCount == 2 ? "CO-OP" : "SOLO", 516);
            TextAt(panel, lost ? "Retry starts this arena again with your stage-start upgrades." : victory ? "Start a fresh adventure for a new set of arenas and surprises." : GameText.Format("NEXT  /  {0}", StageName(state.Stage + 1)), 54, 501, 762, 42, 20, accent, FontStyle.Bold, TextAnchor.MiddleCenter);
            if (!lost && !victory)
                TextAt(panel, GameText.StageSubtitle(state.Stage + 1), 54, 544, 762, 30, 18, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
            ButtonAt(panel, lost ? "TRY STAGE AGAIN" : victory ? "BACK TO MAIN MENU" : "CONTINUE TO NEXT STAGE     >", 80, 600, 710, 78,
                () => { if (lost) app.RestartStage(); else if (victory) app.GoToMenu(); else app.NextStage(); }, lost ? Coral : Amber, Navy, 24);
            if (!victory)
                ButtonAt(panel, lost ? "MAIN MENU" : "SAVE & MAIN MENU", 80, 698, 710, 62, () => { if (lost) app.GoToMenu(); else app.SaveAndMenu(); }, Card, Ivory, 20);
            else
                TextAt(panel, "THANK YOU FOR PLAYING BOMBERMAN INDIE STUDIO", 80, 714, 710, 35, 19, Mint, FontStyle.Bold, TextAnchor.MiddleCenter);
            TextAt(panel, lost ? "Your stage checkpoint is safe." : victory ? "A tiny fuse. A big adventure." : "Both players return for the next stage.", 60, 800, 750, 29, 16, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
        }

        void BuildResultProgress(RectTransform panel, int stage, bool lost)
        {
            for (int i = 0; i < 5; i++)
            {
                bool done = i < stage || (i == stage && !lost);
                RectTransform item = Panel(panel, "Stage progress", 52 + i * 155, 271, 145, 59, done ? new Color(Mint.r, Mint.g, Mint.b, .12f) : Card);
                TextAt(item, (i + 1).ToString("00") + "  " + GameText.T(done ? "CLEAR" : i == stage ? "RETRY" : "AHEAD"), 4, 0, 137, 59, 15, done ? Mint : i == stage ? Coral : Muted, FontStyle.Bold, TextAnchor.MiddleCenter);
            }
        }

        void ResultStat(RectTransform parent, string label, string value, float x)
        {
            TextAt(parent, label, x, 19, 258, 24, 14, Muted, FontStyle.Bold, TextAnchor.MiddleCenter);
            TextAt(parent, value, x, 54, 258, 45, 31, Ivory, FontStyle.Bold, TextAnchor.MiddleCenter);
        }

        void BuildSettings()
        {
            Dim(content, .72f);
            RectTransform panel = CenterPanel(content, "Settings", 830, 800);
            TextAt(panel, "SETTINGS", 43, 32, 744, 73, 51, Ivory, FontStyle.Bold);
            TextAt(panel, "LANGUAGE", 47, 131, 260, 31, 20, Ivory, FontStyle.Bold);
            ButtonAt(panel, "English", 340, 116, 211, 66, () => SelectLanguage(GameLanguage.English),
                app.Language == GameLanguage.English ? Mint : Card, app.Language == GameLanguage.English ? Navy : Ivory, 22);
            ButtonAt(panel, "Français", 565, 116, 216, 66, () => SelectLanguage(GameLanguage.French),
                app.Language == GameLanguage.French ? Mint : Card, app.Language == GameLanguage.French ? Navy : Ivory, 22);
            SliderAt(panel, "MUSIC", "The soundtrack to your next great escape.", 240, app.MusicVolume, value => app.SetMusicVolume(value));
            SliderAt(panel, "SOUND EFFECTS", "Bombs, footsteps, pickups and interface sounds.", 375, app.SfxVolume, value => app.SetSfxVolume(value));
            TextAt(panel, "CAMERA SHAKE", 47, 528, 470, 31, 20, Ivory, FontStyle.Bold);
            TextAt(panel, "A little impact when things get explosive.", 47, 570, 540, 32, 17, Muted);
            Button shakeButton = null;
            shakeButton = ButtonAt(panel, app.ShakeEnabled ? "ON" : "OFF", 611, 533, 170, 66, () =>
            {
                app.PlayUISound();
                app.SetShake(!app.ShakeEnabled);
                Text text = shakeButton.GetComponentInChildren<Text>();
                text.text = GameText.T(app.ShakeEnabled ? "ON" : "OFF");
                text.color = app.ShakeEnabled ? Mint : Muted;
            }, Card, app.ShakeEnabled ? Mint : Muted, 22);
            ImageAt(panel, "Settings divider", 47, 635, 734, 1, Line, false);
            ButtonAt(panel, "DONE", 47, 667, 734, 65, () => CloseSubpage(), Amber, Navy, 22);
            TextAt(panel, "Changes are saved automatically.    ESC  Back", 47, 745, 734, 26, 15, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
        }

        void SliderAt(RectTransform parent, string title, string subtitle, float y, float value, Action<float> changed)
        {
            TextAt(parent, title, 47, y, 580, 30, 20, Ivory, FontStyle.Bold);
            TextAt(parent, subtitle, 47, y + 38, 650, 27, 17, Muted);
            Text percentage = TextAt(parent, Mathf.RoundToInt(value * 100) + "%", 675, y, 108, 33, 22, Amber, FontStyle.Bold, TextAnchor.MiddleRight);
            RectTransform rect = At(parent, title + " slider", 47, y + 80, 734, 31);
            Fill(rect, Color.clear, false);
            Image background = ImageAt(rect, "Track", 0, 10, 734, 10, Line);
            RectTransform fillArea = Stretch(rect, "Fill area");
            fillArea.offsetMin = new Vector2(12, 10); fillArea.offsetMax = new Vector2(-12, -10);
            RectTransform fill = Stretch(fillArea, "Level"); Fill(fill, Amber);
            RectTransform handleArea = Stretch(rect, "Handle area");
            handleArea.offsetMin = new Vector2(12, 0); handleArea.offsetMax = new Vector2(-12, 0);
            RectTransform handle = Anchor(handleArea, "Handle", new Vector2(0, .5f), new Vector2(.5f, .5f), 0, 0, 25, 31);
            Image handleImage = Fill(handle, Ivory);
            Slider slider = rect.gameObject.AddComponent<Slider>();
            slider.fillRect = fill; slider.handleRect = handle; slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0; slider.maxValue = 1; slider.SetValueWithoutNotify(value);
            ColorBlock colors = slider.colors; colors.normalColor = Ivory; colors.highlightedColor = Mint; colors.selectedColor = Mint; colors.pressedColor = Amber; slider.colors = colors;
            slider.onValueChanged.AddListener(v => { percentage.text = Mathf.RoundToInt(v * 100) + "%"; changed(v); });
            selectable.Add(slider);
            var focus = rect.gameObject.AddComponent<EmberSliderFocus>();
            focus.Initialize(slider, percentage, Amber, Mint);
        }

        void BuildControls()
        {
            Dim(content, .78f);
            RectTransform panel = CenterPanel(content, "How to play", 1230, 954);
            TextAt(panel, "A FEW THINGS BEFORE THE FIRST FUSE", 44, 29, 1142, 30, 16, Mint, FontStyle.Bold);
            TextAt(panel, "MAKE EVERY BOMB COUNT.", 41, 75, 1148, 64, 46, Ivory, FontStyle.Bold);
            TextAt(panel, "Clear every rival to advance through five arenas. Stay alive, and stay ahead of the clock.", 44, 147, 1142, 58, 22, Muted);
            RectTransform p1 = Panel(panel, "Player one controls", 44, 227, 552, 155, Card);
            TextAt(p1, "PLAYER 1", 23, 19, 495, 28, 18, Mint, FontStyle.Bold);
            Keycap(p1, "ARROW KEYS", 24, 70, 242, 54); TextAt(p1, "MOVE", 24, 128, 240, 19, 12, Muted, FontStyle.Bold, TextAnchor.MiddleCenter);
            Keycap(p1, "SPACE", 287, 70, 240, 54); TextAt(p1, "PLACE BOMB", 287, 128, 240, 19, 12, Muted, FontStyle.Bold, TextAnchor.MiddleCenter);
            RectTransform p2 = Panel(panel, "Player two controls", 616, 227, 570, 155, Card);
            TextAt(p2, "PLAYER 2  /  LOCAL CO-OP", 23, 19, 521, 28, 18, Amber, FontStyle.Bold);
            Keycap(p2, "Z  Q  S  D", 24, 70, 251, 54); TextAt(p2, "MOVE", 24, 128, 251, 19, 12, Muted, FontStyle.Bold, TextAnchor.MiddleCenter);
            Keycap(p2, "F", 295, 70, 251, 54); TextAt(p2, "PLACE BOMB", 295, 128, 251, 19, 12, Muted, FontStyle.Bold, TextAnchor.MiddleCenter);
            TextAt(panel, "THE RULES OF THE ARENA", 44, 414, 1142, 32, 19, Ivory, FontStyle.Bold);
            Rule(panel, "01", "FIND YOUR EXIT", "Bombs blast in a cross. Solid pillars stop fire; crates break and reveal upgrades. Leave yourself a way out.", 44, 465);
            Rule(panel, "02", "SHARE THE WIN", "All blasts can hurt everyone, including you and your partner. One surviving player can finish the stage for both.", 430, 465);
            Rule(panel, "03", "GROW TOGETHER", "Upgrades last for this stage only. Both players return next stage. If everyone falls or time runs out, retry.", 816, 465);
            TextAt(panel, "PICK UP AN ADVANTAGE", 44, 660, 1142, 30, 19, Ivory, FontStyle.Bold);
            UpgradeCard(panel, "B", "BOMB UP", "Place more bombs\nat the same time.", 44, Amber);
            UpgradeCard(panel, "+", "FIRE UP", "Reach farther\nwith every explosion.", 335, Coral);
            UpgradeCard(panel, ">", "SPEED UP", "Move faster.\nEscape smarter.", 626, Mint);
            UpgradeCard(panel, "P", "WALL PASS", "Walk through crates.\nPillars still block you.", 917, Hex("B8ADF4"));
            ButtonAt(panel, "GOT IT", 826, 855, 360, 63, () => CloseSubpage(), Amber, Navy, 22);
            TextAt(panel, "ESC  Pause / back      F5  Quick save", 45, 851, 741, 30, 17, Ivory, FontStyle.Bold);
            TextAt(panel, "Wall Pass never protects against bombs or flames.", 45, 893, 743, 26, 15, Muted);
        }

        void Rule(RectTransform panel, string number, string title, string description, float x, float y)
        {
            TextAt(panel, number, x, y, 355, 34, 27, Amber, FontStyle.Bold);
            TextAt(panel, title, x, y + 48, 355, 28, 18, Ivory, FontStyle.Bold);
            TextAt(panel, description, x, y + 88, 351, 90, 17, Muted);
        }

        void UpgradeCard(RectTransform panel, string symbol, string title, string description, float x, Color color)
        {
            RectTransform card = Panel(panel, title, x, 712, 269, 112, Card);
            RectTransform icon = Panel(card, "Icon", 15, 16, 37, 37, color);
            TextAt(icon, symbol, 0, 0, 37, 37, 24, Navy, FontStyle.Bold, TextAnchor.MiddleCenter);
            TextAt(card, title, 64, 17, 193, 27, 15, color, FontStyle.Bold);
            TextAt(card, description, 15, 59, 241, 44, 16, Muted);
        }

        void Keycap(RectTransform parent, string title, float x, float y, float width, float height)
        {
            RectTransform key = Panel(parent, title, x, y, width, height, Line);
            ImageAt(key, "Key underline", 8, height - 5, width - 16, 2, Muted, false);
            TextAt(key, title, 0, 0, width, height - 2, 22, Ivory, FontStyle.Bold, TextAnchor.MiddleCenter);
        }

        void BuildOverwrite()
        {
            Dim(content, .8f);
            RectTransform panel = CenterPanel(content, "New adventure confirmation", 778, 540);
            TextAt(panel, "A FRESH START", 43, 34, 690, 28, 16, Amber, FontStyle.Bold);
            TextAt(panel, "START A NEW ADVENTURE?", 41, 90, 696, 100, 38, Ivory, FontStyle.Bold);
            TextAt(panel, playerCount == 2 ? "This replaces your saved campaign with a new two-player co-op run." : "This replaces your saved campaign with a new solo run.", 44, 205, 690, 69, 23, Muted);
            RectTransform save = Panel(panel, "Current save", 44, 292, 690, 64, Card);
            TextAt(save, app.SaveSummary, 19, 0, 651, 64, 19, Mint, FontStyle.Normal, TextAnchor.MiddleLeft);
            Button keep = ButtonAt(panel, "KEEP MY SAVE", 44, 396, 329, 73, () => CloseSubpage(), Card, Ivory, 20);
            ButtonAt(panel, "START FRESH", 389, 396, 345, 73, () => { page = Subpage.None; app.NewCampaign(playerCount); }, Amber, Navy, 21);
            selectable.Remove(keep); selectable.Insert(0, keep);
            TextAt(panel, "ESC  Keep my saved adventure", 44, 486, 690, 29, 15, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
        }

        void BuildNotice()
        {
            RectTransform notice;
            if (app.Screen == GameScreen.MainMenu)
                notice = Anchor(content, "Status notice", new Vector2(1, 1), new Vector2(1, 1), -60, -165, 1108, 53);
            else
                notice = Anchor(content, "Status notice", new Vector2(.5f, 1), new Vector2(.5f, 1), 0, -126, 1070, 50);
            Fill(notice, new Color(Deep.r, Deep.g, Deep.b, .94f));
            noticeText = TextAt(notice, app.LastNotice ?? "", 18, 4, notice.sizeDelta.x - 36, notice.sizeDelta.y - 8, 17, Mint, FontStyle.Normal, TextAnchor.MiddleCenter);
            notice.gameObject.SetActive(page == Subpage.None && !string.IsNullOrEmpty(app.LastNotice));
        }

        void RequestNewCampaign()
        {
            if (app.HasSave) Open(Subpage.Overwrite);
            else app.NewCampaign(playerCount);
        }
        void SelectPlayers(int count)
        {
            app.PlayUISound();
            playerCount = count; Rebuild();
            string target = count == 1 ? "1 PLAYER" : "2 PLAYERS · CO-OP";
            Selectable tab = selectable.Find(s => s.name == target);
            if (tab != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(tab.gameObject);
        }
        void Open(Subpage subpage) { app.PlayUISound(); page = subpage; Rebuild(); }
        void SelectLanguage(GameLanguage language) { app.PlayUISound(); app.SetLanguage(language); Refresh(); }
        static string StageName(int stage) { return GameText.StageName(stage); }
        static string FormatTime(float time) { int s = Mathf.Max(0, Mathf.CeilToInt(time)); return (s / 60).ToString("00") + ":" + (s % 60).ToString("00"); }

        RectTransform CenterPanel(RectTransform parent, string name, float width, float height)
        {
            RectTransform shadow = Anchor(parent, name + " shadow", new Vector2(.5f, .5f), new Vector2(.5f, .5f), 0, -13, width + 12, height + 14);
            Fill(shadow, new Color(0, 0, 0, .28f));
            RectTransform panel = Anchor(parent, name, new Vector2(.5f, .5f), new Vector2(.5f, .5f), 0, 0, width, height);
            Fill(panel, Navy);
            var outline = panel.gameObject.AddComponent<Outline>(); outline.effectColor = Line; outline.effectDistance = new Vector2(1, -1); outline.useGraphicAlpha = false;
            return panel;
        }
        void Dim(RectTransform parent, float alpha)
        {
            RectTransform dim = Stretch(parent, "Backdrop");
            Fill(dim, new Color(Deep.r, Deep.g, Deep.b, alpha), false);
        }
        RectTransform Panel(RectTransform parent, string name, float x, float y, float width, float height, Color color)
        { RectTransform rect = At(parent, name, x, y, width, height); Fill(rect, color); return rect; }
        Image ImageAt(RectTransform parent, string name, float x, float y, float width, float height, Color color, bool round = true)
        { return Fill(At(parent, name, x, y, width, height), color, round); }
        Image Fill(RectTransform rect, Color color, bool round = true)
        {
            Image image = rect.gameObject.AddComponent<Image>(); image.color = color;
            if (round) { image.sprite = rounded; image.type = Image.Type.Sliced; }
            return image;
        }
        Text TextAt(RectTransform parent, string value, float x, float y, float width, float height, int size, Color color,
            FontStyle style = FontStyle.Normal, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            RectTransform rect = At(parent, value.Length > 28 ? value.Substring(0, 28) : value, x, y, width, height);
            Text text = rect.gameObject.AddComponent<Text>(); text.font = font; text.fontSize = size; text.color = color;
            text.fontStyle = style; text.alignment = alignment; text.text = GameText.T(value); text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false; text.lineSpacing = 1.03f;
            return text;
        }
        Button ButtonAt(RectTransform parent, string label, float x, float y, float width, float height, Action action, Color color, Color textColor,
            int size = 21, TextAnchor align = TextAnchor.MiddleCenter)
        {
            RectTransform rect = At(parent, label, x, y, width, height);
            Image image = Fill(rect, color);
            Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.transition = Selectable.Transition.None;
            Navigation nav = button.navigation; nav.mode = Navigation.Mode.Automatic; button.navigation = nav;
            Text labelText = TextAt(rect, label, 23, align == TextAnchor.UpperLeft ? 15 : 0, width - 46, align == TextAnchor.UpperLeft ? 33 : height, size, textColor, FontStyle.Bold, align);
            Outline outline = rect.gameObject.AddComponent<Outline>(); outline.effectDistance = new Vector2(2, -2); outline.useGraphicAlpha = false;
            outline.effectColor = new Color(Mint.r, Mint.g, Mint.b, 0);
            EmberButtonVisual visual = rect.gameObject.AddComponent<EmberButtonVisual>();
            visual.Initialize(button, image, labelText, outline, color, textColor, Mint, () => { if (Time.unscaledTime - lastRebuildTime > .08f) app.PlayUISound(false); });
            button.onClick.AddListener(() => action());
            selectable.Add(button);
            return button;
        }
        static RectTransform At(RectTransform parent, string name, float x, float y, float width, float height)
        { return Anchor(parent, name, new Vector2(0, 1), new Vector2(0, 1), x, -y, width, height); }
        static RectTransform Anchor(RectTransform parent, string name, Vector2 anchor, Vector2 pivot, float x, float y, float width, float height)
        {
            var go = new GameObject(string.IsNullOrEmpty(name) ? "Text" : name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot; rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
            return rect;
        }
        static RectTransform Stretch(RectTransform parent, string name)
        {
            var rect = Anchor(parent, name, new Vector2(.5f, .5f), new Vector2(.5f, .5f), 0, 0, 0, 0);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }
        Sprite MakeRoundedSprite()
        {
            const int size = 128; const float radius = 18;
            roundedTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            roundedTexture.name = "Interface rounded corners"; roundedTexture.hideFlags = HideFlags.DontSave; roundedTexture.wrapMode = TextureWrapMode.Clamp;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x + .5f - size * .5f) - (size * .5f - radius), 0);
                float dy = Mathf.Max(Mathf.Abs(y + .5f - size * .5f) - (size * .5f - radius), 0);
                float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + .5f);
                pixels[y * size + x] = new Color(1, 1, 1, alpha);
            }
            roundedTexture.SetPixels(pixels); roundedTexture.Apply(false, true);
            Sprite sprite = Sprite.Create(roundedTexture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(20, 20, 20, 20));
            sprite.name = "Interface rounded panel"; sprite.hideFlags = HideFlags.DontSave; return sprite;
        }
        void OnDestroy() { if (rounded != null) Destroy(rounded); if (roundedTexture != null) Destroy(roundedTexture); }
        static Color Hex(string value) { Color result; ColorUtility.TryParseHtmlString("#" + value, out result); return result; }
    }

    /// <summary>Shared visible focus for pointer, keyboard and controller navigation.</summary>
    internal sealed class EmberButtonVisual : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerExitHandler
    {
        Button button; Image image; Text label; Outline outline;
        Color baseColor, textColor, focusColor; Action selectedSound;
        bool selected, hovered;
        public void Initialize(Button owner, Image background, Text text, Outline ring, Color color, Color foreground, Color focus, Action sound)
        { button = owner; image = background; label = text; outline = ring; baseColor = color; textColor = foreground; focusColor = focus; selectedSound = sound; }
        void Update()
        {
            bool enabled = button != null && button.IsInteractable();
            bool focus = enabled && (selected || hovered);
            Color target = enabled ? (focus ? Color.Lerp(baseColor, Color.white, .13f) : baseColor) : Color.Lerp(baseColor, new Color(.08f, .12f, .17f, 1), .68f);
            image.color = Color.Lerp(image.color, target, 1 - Mathf.Exp(-20 * Time.unscaledDeltaTime));
            label.color = enabled ? textColor : new Color(.43f, .49f, .54f, 1);
            outline.effectColor = new Color(focusColor.r, focusColor.g, focusColor.b, focus ? 1 : 0);
        }
        public void OnSelect(BaseEventData eventData) { selected = true; selectedSound?.Invoke(); }
        public void OnDeselect(BaseEventData eventData) { selected = false; }
        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            if (button.IsInteractable() && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(gameObject);
        }
        public void OnPointerExit(PointerEventData eventData) { hovered = false; }
    }

    internal sealed class EmberSliderFocus : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler
    {
        Slider slider; Text label; Color regular, focus;
        public void Initialize(Slider owner, Text value, Color normal, Color selected) { slider = owner; label = value; regular = normal; focus = selected; }
        public void OnSelect(BaseEventData data) { label.color = focus; }
        public void OnDeselect(BaseEventData data) { label.color = regular; }
        public void OnPointerEnter(PointerEventData data) { if (slider.IsInteractable() && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(gameObject); }
    }
}
