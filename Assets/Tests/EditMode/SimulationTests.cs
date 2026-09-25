using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BombermanIndieStudio.Tests
{
    public sealed class SimulationTests
    {
        static Simulation EmptyArena(int humans = 1)
        {
            var sim = Simulation.CreateCampaign(humans, 123456);
            for (int y = 1; y < 10; y++)
                for (int x = 1; x < 14; x++)
                { sim.State.Tiles[y * 15 + x] = (int)TileKind.Floor; sim.State.HiddenDrops[y * 15 + x] = -1; }
            foreach (var a in sim.State.Actors) { a.Shield = 0; a.ThinkTimer = 1000; }
            return sim;
        }

        static void Teleport(ActorState actor, int x, int y)
        { actor.X = actor.FromX = x; actor.Y = actor.FromY = y; actor.MoveProgress = 1; }

        static void Bomb(Simulation sim, int id, int x, int y, float fuse = .01f, int range = 3)
        { sim.State.Bombs.Add(new BombState { Id = id, X = x, Y = y, Timer = fuse, Range = range, OwnerId = 99 }); }

        static bool Flame(Simulation sim, int x, int y) => sim.State.Flames.Any(f => f.X == x && f.Y == y);

        [Test]
        public void ExplosionsStopAtPillarsAndFirstCrateAndExposeDrop()
        {
            var sim = EmptyArena();
            sim.State.Tiles[5 * 15 + 7] = (int)TileKind.Pillar;
            sim.State.Tiles[5 * 15 + 3] = (int)TileKind.Crate;
            sim.State.HiddenDrops[5 * 15 + 3] = (int)PickupKind.FireUp;
            Bomb(sim, 10, 5, 5, range: 5);
            sim.Tick(1f / 60, null);
            Assert.That(Flame(sim, 6, 5), Is.True);
            Assert.That(Flame(sim, 7, 5), Is.False);
            Assert.That(Flame(sim, 8, 5), Is.False);
            Assert.That(Flame(sim, 3, 5), Is.True);
            Assert.That(Flame(sim, 2, 5), Is.False);
            Assert.That(sim.GetTile(3, 5), Is.EqualTo(TileKind.Floor));
            Assert.That(sim.State.Pickups.Single().Kind, Is.EqualTo(PickupKind.FireUp));
            Assert.That(sim.Events.Count(e => e.Kind == GameEventKind.CrateDestroyed), Is.EqualTo(1));
        }

        [Test]
        public void ChainReactionTriggersWholeChainAndBombStopsOriginalRay()
        {
            var sim = EmptyArena();
            Bomb(sim, 1, 3, 5, .01f, 6);
            Bomb(sim, 2, 5, 5, 2, 2);
            Bomb(sim, 3, 5, 7, 2, 2);
            sim.Tick(1f / 60, null);
            Assert.That(sim.State.Bombs, Is.Empty);
            Assert.That(sim.Events.Count(e => e.Kind == GameEventKind.Exploded), Is.EqualTo(3));
            Assert.That(Flame(sim, 5, 9), Is.True);
            Assert.That(Flame(sim, 9, 5), Is.False, "The first bomb's longer ray must stop at the intervening bomb.");
        }

        [Test]
        public void MovementInterpolatesAndCannotEnterSolidTile()
        {
            var sim = EmptyArena(); var player = sim.State.Actors[0];
            player.Speed = 4;
            sim.Tick(.125f, new[] { new PlayerCommand(Vector2Int.right, false) });
            Assert.That(player.Position.x, Is.EqualTo(1.5f).Within(.015f));
            sim.Tick(.125f, new[] { new PlayerCommand(Vector2Int.right, false) });
            Assert.That(player.Position.x, Is.EqualTo(2).Within(.015f));
            sim.State.Tiles[1 * 15 + 3] = (int)TileKind.Pillar;
            sim.Tick(1, new[] { new PlayerCommand(Vector2Int.right, false) });
            Assert.That(player.Position.x, Is.EqualTo(2).Within(.015f));
        }

        [Test]
        public void PlacerCanWalkOutButCannotReenterBomb()
        {
            var sim = EmptyArena(); var player = sim.State.Actors[0]; player.Speed = 4;
            Assert.That(sim.PlaceBomb(player), Is.True);
            Assert.That(sim.IsWalkable(1, 1, player), Is.True);
            sim.Tick(.25f, new[] { new PlayerCommand(Vector2Int.right, false) });
            Assert.That(sim.State.Bombs[0].PassActors.Contains(player.Id), Is.False);
            sim.Tick(.25f, new[] { new PlayerCommand(Vector2Int.left, false) });
            Assert.That(player.Position.x, Is.EqualTo(2).Within(.015f));
            Assert.That(sim.IsWalkable(1, 1, player), Is.False);
        }

        [Test]
        public void MovingBombPlacementUsesVisiblePositionAndDoesNotTrapPlacer()
        {
            var sim = EmptyArena(); var player = sim.State.Actors[0]; player.Speed = 4;
            sim.Tick(.10f, new[] { new PlayerCommand(Vector2Int.right, false) });
            Assert.That(sim.PlaceBomb(player), Is.True);
            Assert.That(sim.State.Bombs[0].X, Is.EqualTo(1));
            sim.Tick(.20f, new[] { new PlayerCommand(Vector2Int.right, false) });
            Assert.That(player.Position.x, Is.GreaterThan(2));
        }

        [Test]
        public void CapacityReturnsAfterOwnedBombExplodes()
        {
            var sim = EmptyArena(); var player = sim.State.Actors[0];
            Assert.That(sim.PlaceBomb(player), Is.True);
            Teleport(player, 7, 7); player.BombCooldown = 0;
            Assert.That(sim.PlaceBomb(player), Is.False);
            sim.State.Bombs[0].Timer = .001f;
            sim.Tick(.02f, null);
            Assert.That(sim.PlaceBomb(player), Is.True);
        }

        [Test]
        public void AllUpgradesApplyWithCapsAndWallPassDoesNotPassPillarsOrBombs()
        {
            var sim = EmptyArena(); var player = sim.State.Actors[0];
            foreach (PickupKind kind in Enum.GetValues(typeof(PickupKind)))
                for (int i = 0; i < 20; i++)
                {
                    sim.State.Pickups.Add(new PickupState { X = 1, Y = 1, Kind = kind });
                    sim.Tick(1f / 60, null);
                }
            Assert.That(player.BombCapacity, Is.EqualTo(5));
            Assert.That(player.FireRange, Is.EqualTo(7));
            Assert.That(player.Speed, Is.EqualTo(5.8f));
            Assert.That(player.WallPass, Is.True);
            sim.State.Tiles[1 * 15 + 2] = (int)TileKind.Crate;
            sim.State.Tiles[1 * 15 + 3] = (int)TileKind.Pillar;
            Bomb(sim, 50, 4, 1, 10);
            Assert.That(sim.IsWalkable(2, 1, player), Is.True);
            Assert.That(sim.IsWalkable(3, 1, player), Is.False);
            Assert.That(sim.IsWalkable(4, 1, player), Is.False);
        }

        [Test]
        public void FriendlyFireOnlyLosesWhenLastHumanDies()
        {
            var sim = EmptyArena(2);
            Bomb(sim, 30, 1, 1, range: 1);
            sim.Tick(.02f, null);
            Assert.That(sim.State.Actors[0].Alive, Is.False);
            Assert.That(sim.State.Actors[1].Alive, Is.True);
            Assert.That(sim.State.Outcome, Is.EqualTo(MatchOutcome.None));
            Bomb(sim, 31, 1, 9, range: 1);
            sim.Tick(.02f, null);
            Assert.That(sim.State.Outcome, Is.EqualTo(MatchOutcome.Lost));
        }

        [Test]
        public void MidMovementSavePreservesFusesFlamesGraceAndDeterministicContinuation()
        {
            var original = Simulation.CreateCampaign(2, 97361);
            original.Tick(.1f, new[] { new PlayerCommand(Vector2Int.right, true), new PlayerCommand(Vector2Int.up, false) });
            original.State.Flames.Add(new FlameState { X = 7, Y = 7, Timer = .31f });
            var loaded = Simulation.FromState(JsonUtility.FromJson<SessionState>(JsonUtility.ToJson(original.State)));
            Assert.That(ReferenceEquals(original.State.Actors[0], loaded.State.Actors[0]), Is.False);
            for (int i = 0; i < 480; i++)
            {
                var cmd = new[] { new PlayerCommand(i < 30 ? Vector2Int.right : Vector2Int.zero, i == 120),
                    new PlayerCommand(i < 45 ? Vector2Int.up : Vector2Int.zero, i == 180) };
                original.Tick(1f / 60, cmd); loaded.Tick(1f / 60, cmd);
                Assert.That(JsonUtility.ToJson(loaded.State), Is.EqualTo(JsonUtility.ToJson(original.State)), "Diverged on frame " + i);
            }
        }

        [Test]
        public void InvalidSavedMovementIsRejected()
        {
            var sim = Simulation.CreateCampaign(1, 4);
            sim.State.Actors[0].MoveProgress = float.NaN;
            Assert.Throws<ArgumentException>(() => Simulation.FromState(sim.State));
        }

        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        public void CampaignResetsAllPlayerUpgradesAtEachNewStage(int players, bool reloadBeforeAdvancing)
        {
            var sim = Simulation.CreateCampaign(players, 736);
            for (int stage = 0; stage < 5; stage++)
            {
                Assert.That(sim.State.Stage, Is.EqualTo(stage));
                Assert.That(sim.AliveBots, Is.EqualTo(2 + stage));
                foreach (var human in sim.State.Actors.Where(a => a.HumanIndex >= 0))
                {
                    human.BombCapacity = 3; human.FireRange = 5; human.Speed = 5; human.WallPass = true;
                    if (human.HumanIndex == 1) human.Alive = false;
                }
                foreach (var a in sim.State.Actors) if (a.HumanIndex < 0) a.Alive = false;
                sim.Tick(.02f, null);
                Assert.That(sim.State.Outcome, Is.EqualTo(MatchOutcome.Won));
                if (reloadBeforeAdvancing)
                {
                    sim = Simulation.FromState(JsonUtility.FromJson<SessionState>(JsonUtility.ToJson(sim.State)));
                    foreach (var human in sim.State.Actors.Where(a => a.HumanIndex >= 0))
                    {
                        Assert.That(human.BombCapacity, Is.EqualTo(3), "Loading the same arena preserves its upgrades.");
                        Assert.That(human.FireRange, Is.EqualTo(5));
                        Assert.That(human.Speed, Is.EqualTo(5));
                        Assert.That(human.WallPass, Is.True);
                    }
                }
                int score = sim.State.Score;
                sim.StartNextStage();
                if (stage < 4)
                {
                    foreach (var human in sim.State.Actors.Where(a => a.HumanIndex >= 0))
                    {
                        Assert.That(human.Alive, Is.True);
                        Assert.That(human.BombCapacity, Is.EqualTo(1));
                        Assert.That(human.FireRange, Is.EqualTo(2));
                        Assert.That(human.Speed, Is.EqualTo(3.4f).Within(.0001f));
                        Assert.That(human.WallPass, Is.False);
                    }
                    Assert.That(sim.State.Score, Is.EqualTo(score));
                    Assert.That(sim.State.Outcome, Is.EqualTo(MatchOutcome.None));
                }
            }
            Assert.That(sim.State.Stage, Is.EqualTo(4));
            Assert.That(sim.State.Outcome, Is.EqualTo(MatchOutcome.Won));
        }

        [Test]
        public void DeadlineLosesAnUnfinishedStage()
        {
            var sim = EmptyArena(); sim.State.TimeRemaining = .01f;
            sim.Tick(.02f, null);
            Assert.That(sim.State.TimeRemaining, Is.EqualTo(0));
            Assert.That(sim.State.Outcome, Is.EqualTo(MatchOutcome.Lost));
        }

        [Test]
        public void ProceduralMapsHaveSafeSpawnsAllUpgradesAndConnectedDestructibleSpace()
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                var sim = Simulation.CreateCampaign(2, seed);
                for (int stage = 0; stage < 5; stage++)
                {
                    foreach (var actor in sim.State.Actors)
                    {
                        Assert.That(sim.GetTile(actor.X, actor.Y), Is.EqualTo(TileKind.Floor));
                        int reachableFloor = Reachable(sim, actor.X, actor.Y, false).Count;
                        Assert.That(reachableFloor, Is.GreaterThanOrEqualTo(8), "Spawn must have an escape circuit: " + seed + "/" + stage);
                    }
                    foreach (PickupKind kind in Enum.GetValues(typeof(PickupKind)))
                        Assert.That(sim.State.HiddenDrops, Does.Contain((int)kind));
                    var connected = Reachable(sim, 1, 1, true);
                    for (int i = 0; i < sim.State.Tiles.Length; i++)
                        if (sim.State.Tiles[i] != (int)TileKind.Pillar) Assert.That(connected.Contains(i), Is.True);
                    sim.State.Outcome = MatchOutcome.Won; sim.StartNextStage();
                }
            }
        }

        [Test]
        public void SameSeedRecreatesMapAndDifferentSeedsVaryIt()
        {
            var a = Simulation.CreateCampaign(1, 171);
            var b = Simulation.CreateCampaign(1, 171);
            var c = Simulation.CreateCampaign(1, 172);
            Assert.That(JsonUtility.ToJson(a.State), Is.EqualTo(JsonUtility.ToJson(b.State)));
            Assert.That(a.State.Tiles.SequenceEqual(c.State.Tiles), Is.False);
        }

        [Test]
        public void BotPlantsToOpenCrateAndEscapesItsOwnBlast()
        {
            var sim = EmptyArena();
            var bot = sim.State.Actors.First(a => a.HumanIndex < 0);
            Teleport(bot, 9, 5); bot.ThinkTimer = 0; bot.Speed = 3; bot.FireRange = 2;
            sim.State.Tiles[5 * 15 + 11] = (int)TileKind.Crate;
            sim.Tick(1f / 60, null);
            Assert.That(sim.State.Bombs.Any(b => b.OwnerId == bot.Id), Is.True);
            for (int frame = 0; frame < 180; frame++) sim.Tick(1f / 60, null);
            Assert.That(bot.Alive, Is.True);
            Assert.That(sim.GetTile(11, 5), Is.EqualTo(TileKind.Floor));
        }

        [Test]
        public void BotRefusesSuicidalBombInSealedCorridor()
        {
            var sim = EmptyArena(); var bot = sim.State.Actors.First(a => a.HumanIndex < 0);
            Teleport(bot, 9, 5); bot.ThinkTimer = 0;
            sim.State.Tiles[5 * 15 + 8] = (int)TileKind.Pillar;
            sim.State.Tiles[4 * 15 + 9] = (int)TileKind.Pillar;
            sim.State.Tiles[6 * 15 + 9] = (int)TileKind.Pillar;
            sim.State.Tiles[5 * 15 + 10] = (int)TileKind.Crate;
            sim.Tick(.1f, null);
            Assert.That(sim.State.Bombs.Any(b => b.OwnerId == bot.Id), Is.False);
        }

        static HashSet<int> Reachable(Simulation sim, int x, int y, bool throughCrates)
        {
            var visited = new HashSet<int>(); var queue = new Queue<Vector2Int>();
            queue.Enqueue(new Vector2Int(x, y)); visited.Add(y * 15 + x);
            var directions = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                foreach (var direction in directions)
                {
                    var next = cell + direction; var tile = sim.GetTile(next.x, next.y);
                    if (tile == TileKind.Pillar || (!throughCrates && tile == TileKind.Crate)) continue;
                    if (visited.Add(next.y * 15 + next.x)) queue.Enqueue(next);
                }
            }
            return visited;
        }
    }
}
