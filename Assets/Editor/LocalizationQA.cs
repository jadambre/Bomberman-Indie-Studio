using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EmberGrid.Editor
{
    public static class LocalizationQA
    {
        public static string Run()
        {
            var app = GameApp.Instance;
            if (!Application.isPlaying || app == null) throw new Exception("Enter Play mode first.");
            bool hadPreference = PlayerPrefs.HasKey(GameText.PreferenceKey);
            string preference = PlayerPrefs.GetString(GameText.PreferenceKey);
            var language = GameText.Current;
            string saveDirectory = SaveRepository.TestDirectory;
            bool enabled = app.enabled;
            var report = new StringBuilder();
            try
            {
                app.enabled = false;
                SaveRepository.TestDirectory = Path.GetFullPath("QA/LocalizationSaves");
                GameText.SetLanguage(GameLanguage.English, false);
                app.GoToMenu(); app.UI.Refresh();
                Click("SETTINGS"); Click("Français");
                Require(app.Language == GameLanguage.French, "French settings button");
                Require(PlayerPrefs.GetString(GameText.PreferenceKey) == "fr", "French preference persisted");
                Require(HasText("OPTIONS") && HasText("LANGUE"), "settings translates immediately");
                Require(EventSystem.current.currentSelectedGameObject.name == "Français", "keyboard focus retained");
                Click("English");
                Require(app.Language == GameLanguage.English && HasText("LANGUAGE"), "switch back without closing settings");
                Click("DONE");

                app.NewCampaign(2); app.Pause(); app.UI.Refresh();
                var human = app.Sim.State.Actors.First(a => a.HumanIndex == 0);
                human.BombCapacity = 3; human.WallPass = true;
                app.Sim.PlaceBomb(human); app.SaveNow(false);
                string state = JsonUtility.ToJson(app.Sim.State);
                app.Notice("Campaign saved.");
                Click("SETTINGS"); Click("Français");
                Require(app.Screen == GameScreen.Paused, "language change keeps the game paused");
                Require(JsonUtility.ToJson(app.Sim.State) == state, "bomb timers, upgrades and campaign unchanged");
                Require(app.LastNotice == "Partie sauvegardée.", "existing notice changes language");
                Require(app.SaveSummary.StartsWith("NIVEAU"), "existing save summary changes language");
                GameText.SetLanguage(GameLanguage.English, false); GameText.LoadPreference();
                Require(GameText.Current == GameLanguage.French, "stored choice reloads");
                Click("DONE");
                report.AppendLine("PASS: real settings buttons switch EN/FR in menu and pause; preference reload; focus retention; live campaign and active bomb unchanged; notices and save summary translate.");

                foreach (var current in new[] { GameLanguage.English, GameLanguage.French })
                {
                    GameText.SetLanguage(current, false);
                    app.NewCampaign(2); app.GoToMenu(); Check(app, report, current + " main menu");
                    Click("SETTINGS"); Check(app, report, current + " settings"); Click("DONE");
                    Click("HOW TO PLAY"); Check(app, report, current + " controls"); Click("GOT IT");
                    Click("START NEW ADVENTURE     >"); Check(app, report, current + " overwrite"); Click("KEEP MY SAVE");
                    for (int stage = 0; stage < 5; stage++)
                    {
                        app.LoadPreviewStage(stage, 2); Check(app, report, current + " pause stage " + stage);
                        app.Resume(); Check(app, report, current + " HUD stage " + stage);
                    }
                    app.NewCampaign(2); Check(app, report, current + " intro");
                    app.Sim.State.Outcome = MatchOutcome.Won; app.SendMessage("EndRound"); Check(app, report, current + " stage clear");
                    app.LoadPreviewStage(4, 2); app.Sim.State.Outcome = MatchOutcome.Won;
                    app.SendMessage("EndRound"); Check(app, report, current + " victory");
                    app.LoadPreviewStage(1, 2); app.Sim.State.TimeRemaining = 0; app.Sim.State.Outcome = MatchOutcome.Lost;
                    app.SendMessage("EndRound"); Check(app, report, current + " defeat");
                }
                Directory.CreateDirectory("QA"); File.WriteAllText("QA/localization-integration.txt", report.ToString());
                return report.ToString();
            }
            finally
            {
                if (hadPreference) PlayerPrefs.SetString(GameText.PreferenceKey, preference);
                else PlayerPrefs.DeleteKey(GameText.PreferenceKey);
                PlayerPrefs.Save(); GameText.SetLanguage(language, false);
                SaveRepository.TestDirectory = saveDirectory;
                app.GoToMenu(); app.UI.Refresh(); app.enabled = enabled;
            }
        }

        static void Check(GameApp app, StringBuilder report, string label)
        {
            app.UI.Refresh(); Canvas.ForceUpdateCanvases();
            report.AppendLine(label + ": " + QATools.LayoutAudit().Trim());
        }
        static void Click(string name)
        {
            var button = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                .FirstOrDefault(b => b.gameObject.activeInHierarchy && b.name == name && b.IsInteractable());
            Require(button != null, "button " + name);
            EventSystem.current.SetSelectedGameObject(button.gameObject);
            button.onClick.Invoke(); GameApp.Instance.UI.Refresh(); Canvas.ForceUpdateCanvases();
        }
        static bool HasText(string value) => UnityEngine.Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t => t.gameObject.activeInHierarchy && t.text == value);
        static void Require(bool value, string name) { if (!value) throw new Exception("Localization QA failed: " + name); }
    }
}
