using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EmberGrid.Tests
{
    public sealed class SaveRepositoryTests
    {
        string previousDirectory;
        string generatedDirectory;
        string testRoot;

        [SetUp]
        public void CreateIsolatedSaveDirectory()
        {
            previousDirectory = SaveRepository.TestDirectory;
            testRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "QA", "TestSaves"));
            generatedDirectory = Path.GetFullPath(Path.Combine(testRoot, Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(generatedDirectory);
            SaveRepository.TestDirectory = generatedDirectory;
        }

        [TearDown]
        public void RemoveOnlyThisTestsGeneratedFiles()
        {
            SaveRepository.TestDirectory = previousDirectory;
            if (string.IsNullOrEmpty(generatedDirectory) || !Directory.Exists(generatedDirectory)) return;
            string resolved = Path.GetFullPath(generatedDirectory);
            Assert.That(Path.GetDirectoryName(resolved), Is.EqualTo(testRoot).IgnoreCase,
                "Cleanup must remain inside the dedicated test save root.");
            Assert.That(Guid.TryParseExact(Path.GetFileName(resolved), "N", out _), Is.True,
                "Cleanup is allowed only for this test's generated GUID directory.");
            foreach (string name in new[] { "campaign-v1.json", "campaign-v1.json.bak", "campaign-v1.json.tmp" })
            {
                string file = Path.Combine(resolved, name);
                if (File.Exists(file)) File.Delete(file);
            }
            // Non-recursive deletion deliberately preserves anything unexpected.
            Directory.Delete(resolved, false);
        }

        [Test]
        public void WriteAndReadPreserveFullLiveSessionAndIndependentRetryCheckpoint()
        {
            Simulation simulation = Simulation.CreateCampaign(2, 81947);
            SessionState checkpoint = SaveRepository.Clone(simulation.State);
            simulation.Tick(.1f, new[] {
                new PlayerCommand(Vector2Int.right, true),
                new PlayerCommand(Vector2Int.up, false)
            });
            SessionState live = simulation.State;
            live.Score = 1735;
            live.Elapsed = 12.375f;
            live.TimeRemaining = 167.625f;
            live.Actors[0].BombCapacity = 3;
            live.Actors[0].FireRange = 5;
            live.Actors[0].Speed = 4.6f;
            live.Actors[0].WallPass = true;
            live.Actors[1].Alive = false;
            live.Flames.Add(new FlameState { X = 7, Y = 7, Timer = .3125f });
            live.Pickups.Add(new PickupState { X = 5, Y = 5, Kind = PickupKind.SpeedUp });
            Assert.That(live.Actors[0].MoveProgress, Is.InRange(.001f, .999f), "Exercise a save between cells.");
            Assert.That(live.Bombs.Count, Is.GreaterThan(0), "Exercise an armed fuse and walk-out grace.");
            string expectedLive = JsonUtility.ToJson(live);
            string expectedCheckpoint = JsonUtility.ToJson(checkpoint);

            Assert.That(SaveRepository.Write(live, checkpoint, out string error), Is.True, error);
            Assert.That(error, Is.Null);
            Assert.That(File.Exists(SaveRepository.PathName), Is.True);
            Assert.That(File.Exists(SaveRepository.PathName + ".tmp"), Is.False, "Completed saves leave no temporary file.");
            // A later in-memory mutation must never alter data already saved to disk.
            live.Actors[0].FireRange = 1;
            checkpoint.Score = 9999;

            CampaignSave loaded = SaveRepository.Read(out string notice);
            Assert.That(loaded, Is.Not.Null);
            Assert.That(notice, Is.Null);
            Assert.That(JsonUtility.ToJson(loaded.Session), Is.EqualTo(expectedLive));
            Assert.That(JsonUtility.ToJson(loaded.Checkpoint), Is.EqualTo(expectedCheckpoint));
            Assert.That(ReferenceEquals(loaded.Session, loaded.Checkpoint), Is.False);
            Assert.That(DateTime.TryParse(loaded.SavedAt, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTime savedAt), Is.True);
            Assert.That(savedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
        }

        [Test]
        public void CorruptPrimaryRecoversPreviousCompleteSaveAndItsCheckpoint()
        {
            SessionState first = Simulation.CreateCampaign(2, 3087).State;
            SessionState firstCheckpoint = SaveRepository.Clone(first);
            first.Score = 275;
            string previousSession = JsonUtility.ToJson(first);
            string previousCheckpoint = JsonUtility.ToJson(firstCheckpoint);
            Assert.That(SaveRepository.Write(first, firstCheckpoint, out string firstError), Is.True, firstError);

            SessionState next = SaveRepository.Clone(first);
            next.Score = 850;
            next.TimeRemaining = 92;
            Assert.That(SaveRepository.Write(next, next, out string secondError), Is.True, secondError);
            Assert.That(File.Exists(SaveRepository.PathName + ".bak"), Is.True);
            File.WriteAllText(SaveRepository.PathName, "{interrupted save");
            ExpectUnreadableSaveWarning();

            CampaignSave recovered = SaveRepository.Read(out string notice);
            Assert.That(recovered, Is.Not.Null);
            Assert.That(notice, Does.Contain("Recovered"));
            Assert.That(JsonUtility.ToJson(recovered.Session), Is.EqualTo(previousSession));
            Assert.That(JsonUtility.ToJson(recovered.Checkpoint), Is.EqualTo(previousCheckpoint));
        }

        [Test]
        public void MissingSaveReturnsNoCampaignAndNoErrorNotice()
        {
            Assert.That(SaveRepository.Read(out string notice), Is.Null);
            Assert.That(notice, Is.Null);
            Assert.That(Directory.GetFiles(generatedDirectory), Is.Empty, "Reading must not create a save.");
        }

        [Test]
        public void InvalidPrimaryAndBackupReturnNoCampaignWithHelpfulNotice()
        {
            File.WriteAllText(SaveRepository.PathName, "invalid primary");
            File.WriteAllText(SaveRepository.PathName + ".bak", "invalid backup");
            ExpectUnreadableSaveWarning();

            Assert.That(SaveRepository.Read(out string notice), Is.Null);
            Assert.That(notice, Does.Contain("could not be loaded"));
            Assert.That(notice, Does.Contain("new campaign"));
        }

        [TestCase(0, TestName = "ReadRejectsActorOutsideArena")]
        [TestCase(1, TestName = "ReadRejectsDuplicateActorIdentifiers")]
        [TestCase(2, TestName = "ReadRejectsInvalidActorSpeed")]
        [TestCase(3, TestName = "ReadRejectsInvalidMovementProgress")]
        public void CorruptActorDataCannotBecomeAPlayableSave(int corruption)
        {
            SessionState state = Simulation.CreateCampaign(2, 903).State;
            switch (corruption)
            {
                case 0: state.Actors[0].X = state.Width; break;
                case 1: state.Actors[1].Id = state.Actors[0].Id; break;
                case 2: state.Actors[0].Speed = -2; break;
                case 3: state.Actors[0].MoveProgress = 1.25f; break;
            }
            var malformedSave = new CampaignSave { Session = state, Checkpoint = null, SavedAt = DateTime.UtcNow.ToString("o") };
            File.WriteAllText(SaveRepository.PathName, JsonUtility.ToJson(malformedSave));
            LogAssert.Expect(LogType.Warning, new Regex("^Save could not be read: Invalid actor\\."));

            Assert.That(SaveRepository.Read(out string notice), Is.Null);
            Assert.That(notice, Does.Contain("could not be loaded"));
        }

        [Test]
        public void RejectedWriteLeavesTheLastValidCampaignUntouched()
        {
            SessionState good = Simulation.CreateCampaign(1, 601).State;
            Assert.That(SaveRepository.Write(good, good, out string firstError), Is.True, firstError);
            string originalFile = File.ReadAllText(SaveRepository.PathName);
            SessionState corrupt = SaveRepository.Clone(good);
            corrupt.Actors[0].FromY = -1;
            LogAssert.Expect(LogType.Warning, new Regex("^Save failed: Invalid actor\\."));

            Assert.That(SaveRepository.Write(corrupt, good, out string error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
            Assert.That(File.ReadAllText(SaveRepository.PathName), Is.EqualTo(originalFile));
            Assert.That(File.Exists(SaveRepository.PathName + ".tmp"), Is.False);
            Assert.That(SaveRepository.Read(out string notice), Is.Not.Null);
            Assert.That(notice, Is.Null);
        }

        static void ExpectUnreadableSaveWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("^Save could not be read:"));
        }
    }
}
