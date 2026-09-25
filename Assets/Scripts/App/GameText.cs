using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace EmberGrid
{
    public enum GameLanguage { English, French }

    /// <summary>Display language is a local preference, independent of campaign saves.</summary>
    public static partial class GameText
    {
        public const string PreferenceKey = "language";
        public static GameLanguage Current { get; private set; } = GameLanguage.English;
        static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");
        static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");
        static readonly Dictionary<string, string> French = CreateFrench();

        public static CultureInfo Culture => Current == GameLanguage.French ? FrenchCulture : EnglishCulture;

        public static void LoadPreference()
        {
            SetLanguage(PlayerPrefs.GetString(PreferenceKey, "en") == "fr" ? GameLanguage.French : GameLanguage.English, false);
        }

        public static void SetLanguage(GameLanguage language, bool persist = true)
        {
            Current = language == GameLanguage.French ? GameLanguage.French : GameLanguage.English;
            if (!persist) return;
            PlayerPrefs.SetString(PreferenceKey, Current == GameLanguage.French ? "fr" : "en");
            PlayerPrefs.Save();
        }

        public static string T(string english)
        {
            if (string.IsNullOrEmpty(english) || Current == GameLanguage.English) return english;
            return French.TryGetValue(english, out string translated) ? translated : english;
        }

        public static string Format(string englishTemplate, params object[] args) => string.Format(Culture, T(englishTemplate), args);
        public static string StageName(int stage) => T(Campaign.Names[Mathf.Clamp(stage, 0, 4)]);
        public static string StageSubtitle(int stage) => T(Campaign.Subtitles[Mathf.Clamp(stage, 0, 4)]);
        public static string StageBriefing(int stage) => T(Campaign.Briefings[Mathf.Clamp(stage, 0, 4)]);

        static Dictionary<string, string> CreateFrench()
        {
            var translations = new Dictionary<string, string>
            {
                { "No saved campaign", "Aucune partie sauvegardée" },
                { "STAGE {0:00} / 05  ·  {1}  ·  {2}", "NIVEAU {0:00} / 05  ·  {1}  ·  {2}" },
                { "Campaign saved.", "Partie sauvegardée." },
                { "No valid campaign to continue.", "Aucune partie valide à reprendre." },
                { "Could not save. Please check available disk space.", "Sauvegarde impossible. Vérifiez l'espace disque disponible." },
                { "Recovered your previous save.", "Votre sauvegarde précédente a été récupérée." },
                { "Your save could not be loaded. You can start a new campaign.", "Impossible de charger la sauvegarde. Vous pouvez commencer une nouvelle partie." },
                { "MOSSBOUND", "BOSQUET" },
                { "SUNSTONE", "PIERRE SOLAIRE" },
                { "FROSTLINE", "LIGNE DE GIVRE" },
                { "AFTERGLOW", "LUEUR NOCTURNE" },
                { "THE CORE", "LE CŒUR" },
                { "The overgrown outpost", "Le poste envahi par la mousse" },
                { "The amber excavation", "Les fouilles d'ambre" },
                { "The frozen relay", "Le relais gelé" },
                { "The midnight foundry", "La fonderie de minuit" },
                { "The final detonation", "La détonation finale" },
                { "Clear a path. Watch the fuse. Find your rhythm.", "Ouvrez un passage. Surveillez la mèche. Trouvez votre rythme." },
                { "Hunters have entered the arena. Keep an escape route.", "Les chasseurs entrent en scène. Gardez une issue de secours." },
                { "Faster rivals. Longer blasts. Think one move ahead.", "Des rivaux plus rapides, des explosions plus longues. Anticipez." },
                { "The tacticians are waiting. Turn their bombs against them.", "Les tacticiens vous attendent. Retournez leurs bombes contre eux." },
                { "One final arena. Use everything you have learned.", "Une dernière arène. Mettez à profit tout ce que vous avez appris." }
            };
            AddInterfaceTranslations(translations);
            return translations;
        }

        static partial void AddInterfaceTranslations(Dictionary<string, string> translations);
    }
}
