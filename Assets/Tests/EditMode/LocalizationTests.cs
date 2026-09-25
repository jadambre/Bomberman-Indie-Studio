using NUnit.Framework;
using UnityEngine;

namespace EmberGrid.Tests
{
    public sealed class LocalizationTests
    {
        bool hadPreference;
        string previousPreference;
        GameLanguage previousLanguage;

        [SetUp]
        public void RememberUserLanguagePreference()
        {
            hadPreference = PlayerPrefs.HasKey(GameText.PreferenceKey);
            previousPreference = PlayerPrefs.GetString(GameText.PreferenceKey);
            previousLanguage = GameText.Current;
        }

        [TearDown]
        public void RestoreUserLanguagePreference()
        {
            if (hadPreference) PlayerPrefs.SetString(GameText.PreferenceKey, previousPreference);
            else PlayerPrefs.DeleteKey(GameText.PreferenceKey);
            GameText.SetLanguage(previousLanguage, false);
            PlayerPrefs.Save();
        }

        [Test]
        public void SwitchingLanguageImmediatelyChangesMenusAndNoticesInBothDirections()
        {
            GameText.SetLanguage(GameLanguage.English, false);
            Assert.That(GameText.T("SETTINGS"), Is.EqualTo("SETTINGS"));
            Assert.That(GameText.T("Campaign saved."), Is.EqualTo("Campaign saved."));

            GameText.SetLanguage(GameLanguage.French, false);
            Assert.That(GameText.T("SETTINGS"), Is.EqualTo("OPTIONS"));
            Assert.That(GameText.T("Campaign saved."), Is.EqualTo("Partie sauvegardée."));

            GameText.SetLanguage(GameLanguage.English, false);
            Assert.That(GameText.T("SETTINGS"), Is.EqualTo("SETTINGS"));
            Assert.That(GameText.T("Campaign saved."), Is.EqualTo("Campaign saved."));
        }

        [TestCase(GameLanguage.English, "en", GameLanguage.French)]
        [TestCase(GameLanguage.French, "fr", GameLanguage.English)]
        public void SavedChoiceIsRestoredAfterAnInMemoryLanguageChange(
            GameLanguage savedLanguage, string storedValue, GameLanguage temporaryLanguage)
        {
            GameText.SetLanguage(savedLanguage);
            Assert.That(PlayerPrefs.GetString(GameText.PreferenceKey), Is.EqualTo(storedValue));

            GameText.SetLanguage(temporaryLanguage, false);
            Assert.That(GameText.Current, Is.EqualTo(temporaryLanguage));
            Assert.That(PlayerPrefs.GetString(GameText.PreferenceKey), Is.EqualTo(storedValue),
                "A temporary language change must not overwrite the saved choice.");

            GameText.LoadPreference();
            Assert.That(GameText.Current, Is.EqualTo(savedLanguage));
        }

        [Test]
        public void MissingLanguagePreferenceDefaultsToEnglish()
        {
            PlayerPrefs.DeleteKey(GameText.PreferenceKey);
            GameText.SetLanguage(GameLanguage.French, false);

            GameText.LoadPreference();

            Assert.That(GameText.Current, Is.EqualTo(GameLanguage.English));
        }

        [TestCase("")]
        [TestCase("unsupported-language")]
        public void InvalidLanguagePreferenceFallsBackToEnglish(string invalidValue)
        {
            PlayerPrefs.SetString(GameText.PreferenceKey, invalidValue);
            GameText.SetLanguage(GameLanguage.French, false);

            GameText.LoadPreference();

            Assert.That(GameText.Current, Is.EqualTo(GameLanguage.English));
        }

        [Test]
        public void EveryStageHasLocalizedPresentationAndCanReturnToEnglish()
        {
            GameText.SetLanguage(GameLanguage.French, false);
            for (int stage = 0; stage < Campaign.Names.Length; stage++)
            {
                Assert.That(GameText.StageName(stage), Is.Not.Null.And.Not.Empty);
                Assert.That(GameText.StageName(stage), Is.Not.EqualTo(Campaign.Names[stage]),
                    "Stage " + stage + " needs a French name.");
                Assert.That(GameText.StageSubtitle(stage), Is.Not.Null.And.Not.Empty);
                Assert.That(GameText.StageSubtitle(stage), Is.Not.EqualTo(Campaign.Subtitles[stage]));
                Assert.That(GameText.StageBriefing(stage), Is.Not.Null.And.Not.Empty);
                Assert.That(GameText.StageBriefing(stage), Is.Not.EqualTo(Campaign.Briefings[stage]));
            }

            GameText.SetLanguage(GameLanguage.English, false);
            for (int stage = 0; stage < Campaign.Names.Length; stage++)
            {
                Assert.That(GameText.StageName(stage), Is.EqualTo(Campaign.Names[stage]));
                Assert.That(GameText.StageSubtitle(stage), Is.EqualTo(Campaign.Subtitles[stage]));
                Assert.That(GameText.StageBriefing(stage), Is.EqualTo(Campaign.Briefings[stage]));
            }
        }

        [Test]
        public void NumericFormattingUsesTheSelectedLanguage()
        {
            GameText.SetLanguage(GameLanguage.English, false);
            Assert.That(GameText.Culture.NumberFormat.NumberDecimalSeparator, Is.EqualTo("."));
            Assert.That(GameText.Format("{0:0.0} / {1:000}", 3.4f, 7), Is.EqualTo("3.4 / 007"));

            GameText.SetLanguage(GameLanguage.French, false);
            Assert.That(GameText.Culture.NumberFormat.NumberDecimalSeparator, Is.EqualTo(","));
            Assert.That(GameText.Format("{0:0.0} / {1:000}", 3.4f, 7), Is.EqualTo("3,4 / 007"));
        }

        [Test]
        public void MissingTranslationRemainsReadableAndEmptyTextIsSafe()
        {
            GameText.SetLanguage(GameLanguage.French, false);

            Assert.That(GameText.T("An unregistered diagnostic message."),
                Is.EqualTo("An unregistered diagnostic message."));
            Assert.That(GameText.T(string.Empty), Is.Empty);
            Assert.That(GameText.T(null), Is.Null);
        }

        [Test]
        public void SwitchingLanguageLeavesTheLiveCampaignUnchanged()
        {
            Simulation simulation = Simulation.CreateCampaign(2, 61423);
            simulation.Tick(.1f, new[] {
                new PlayerCommand(Vector2Int.right, true),
                new PlayerCommand(Vector2Int.up, false)
            });
            simulation.State.Score = 1235;
            simulation.State.Actors[0].WallPass = true;
            string before = JsonUtility.ToJson(simulation.State);

            GameText.SetLanguage(GameLanguage.French, false);
            GameText.SetLanguage(GameLanguage.English, false);

            Assert.That(JsonUtility.ToJson(simulation.State), Is.EqualTo(before),
                "Language belongs to preferences, not to the saved campaign or its simulation.");
        }
    }
}
