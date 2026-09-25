using System;
using System.Collections.Generic;
using UnityEngine;

namespace BombermanIndieStudio
{
    /// <summary>Serializable, scene-independent rules for the five-arena cooperative campaign.</summary>
    public sealed class Simulation
    {
        public SessionState State { get; private set; }
        public List<GameEvent> Events { get; } = new List<GameEvent>();
        public int AliveBots
        {
            get { int count = 0; foreach (var a in State.Actors) if (a.Alive && a.HumanIndex < 0) count++; return count; }
        }

        const float Fuse = 2.25f;
        const float FlameLife = .55f;
        const float CollisionRadius = .64f;
        static readonly Vector2Int[] Directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

        Simulation(SessionState state) { State = state; }

        public static Simulation CreateCampaign(int playerCount, int seed)
        {
            var state = new SessionState
            {
                PlayerCount = Mathf.Clamp(playerCount, 1, 2), Seed = seed,
                RngState = unchecked((uint)seed ^ 0xA3C59AC3u)
            };
            if (state.RngState == 0) state.RngState = 0x9E3779B9u;
            var sim = new Simulation(state);
            sim.GenerateStage();
            return sim;
        }

        public static Simulation FromState(SessionState state)
        {
            ValidateState(state);
            // A loaded simulation owns its data, including every bomb's walk-out permission.
            return new Simulation(JsonUtility.FromJson<SessionState>(JsonUtility.ToJson(state)));
        }

        public TileKind GetTile(int x, int y)
        {
            if (!Inside(x, y)) return TileKind.Pillar;
            return (TileKind)State.Tiles[Index(x, y)];
        }

        public bool IsWalkable(int x, int y, ActorState actor)
        {
            if (actor == null || !Inside(x, y)) return false;
            var tile = GetTile(x, y);
            if (tile == TileKind.Pillar || (tile == TileKind.Crate && !actor.WallPass)) return false;
            foreach (var bomb in State.Bombs)
                if (bomb.X == x && bomb.Y == y && !bomb.PassActors.Contains(actor.Id)) return false;
            return true;
        }

        public bool PlaceBomb(ActorState actor)
        {
            if (actor == null || !actor.Alive || State.Outcome != MatchOutcome.None || actor.BombCooldown > 0) return false;
            var cell = OccupiedCell(actor);
            if (GetTile(cell.x, cell.y) != TileKind.Floor) return false;
            int active = 0;
            foreach (var b in State.Bombs)
            {
                if (b.X == cell.x && b.Y == cell.y) return false;
                if (b.OwnerId == actor.Id) active++;
            }
            if (active >= actor.BombCapacity) return false;
            var bomb = new BombState
            {
                Id = State.NextBombId++, OwnerId = actor.Id, X = cell.x, Y = cell.y,
                Range = Mathf.Clamp(actor.FireRange, 1, 8), Timer = Fuse
            };
            foreach (var a in State.Actors)
            {
                // A character already traversing this square may finish its movement.
                if (a.Alive && ((a.X == cell.x && a.Y == cell.y) ||
                    (Mathf.Abs(a.Position.x - cell.x) < .8f && Mathf.Abs(a.Position.y - cell.y) < .8f)))
                    bomb.PassActors.Add(a.Id);
            }
            State.Bombs.Add(bomb);
            actor.BombCooldown = actor.HumanIndex >= 0 ? .18f : .65f;
            Events.Add(new GameEvent(GameEventKind.BombPlaced, cell.x, cell.y, actor.Id));
            return true;
        }

        public void Tick(float dt, PlayerCommand[] commands)
        {
            Events.Clear();
            if (State.Outcome != MatchOutcome.None || !Finite(dt) || dt <= 0) return;
            // Small steps preserve collision and fuse ordering even when a caller advances a large interval.
            bool first = true;
            while (dt > .000001f && State.Outcome == MatchOutcome.None)
            {
                float step = Mathf.Min(dt, 1f / 60f);
                Step(step, commands, first);
                first = false;
                dt -= step;
            }
        }

        void Step(float dt, PlayerCommand[] commands, bool acceptBombInput)
        {
            State.Elapsed += dt;
            State.TimeRemaining = Mathf.Max(0, State.TimeRemaining - dt);
            for (int i = State.Flames.Count - 1; i >= 0; i--)
            {
                State.Flames[i].Timer -= dt;
                if (State.Flames[i].Timer <= 0) State.Flames.RemoveAt(i);
            }
            foreach (var b in State.Bombs) b.Timer -= dt;
            foreach (var actor in State.Actors)
            {
                if (!actor.Alive) continue;
                actor.Shield = Mathf.Max(0, actor.Shield - dt);
                actor.BombCooldown = Mathf.Max(0, actor.BombCooldown - dt);
                actor.ThinkTimer = Mathf.Max(0, actor.ThinkTimer - dt);
                var command = actor.HumanIndex >= 0 && commands != null && actor.HumanIndex < commands.Length
                    ? commands[actor.HumanIndex] : default(PlayerCommand);
                if (actor.HumanIndex >= 0 && acceptBombInput && command.Bomb) PlaceBomb(actor);
                float remaining = dt;
                // At most two segments are needed at supported speeds and a 1/60-second step.
                for (int segment = 0; segment < 2 && remaining > .000001f; segment++)
                {
                    if (actor.MoveProgress >= 1)
                    {
                        Vector2Int direction = actor.HumanIndex >= 0 ? Normalize(command.Direction) : ChooseBotDirection(actor);
                        if (direction == Vector2Int.zero || !BeginMove(actor, direction)) break;
                    }
                    float needed = (1 - actor.MoveProgress) * actor.MoveDuration;
                    float used = Mathf.Min(needed, remaining);
                    actor.MoveProgress = Mathf.Min(1, actor.MoveProgress + used / actor.MoveDuration);
                    remaining -= used;
                    if (actor.MoveProgress >= .999999f)
                    {
                        actor.MoveProgress = 1;
                        actor.FromX = actor.X; actor.FromY = actor.Y;
                        if (actor.HumanIndex >= 0)
                            Events.Add(new GameEvent(GameEventKind.Footstep, actor.X, actor.Y, actor.Id));
                    }
                }
            }
            RemoveBombGrace();
            ResolveExplosions();
            ResolveActorsAndPickups();
            CheckOutcome();
        }

        bool BeginMove(ActorState actor, Vector2Int direction)
        {
            int x = actor.X + direction.x, y = actor.Y + direction.y;
            actor.FacingX = direction.x; actor.FacingY = direction.y;
            if (!IsWalkable(x, y, actor)) return false;
            actor.FromX = actor.X; actor.FromY = actor.Y;
            actor.X = x; actor.Y = y;
            actor.MoveProgress = 0;
            actor.MoveDuration = 1 / Mathf.Clamp(actor.Speed, 1, 7);
            return true;
        }

        void RemoveBombGrace()
        {
            foreach (var b in State.Bombs)
            {
                for (int i = b.PassActors.Count - 1; i >= 0; i--)
                {
                    var actor = FindActor(b.PassActors[i]);
                    if (actor == null || !actor.Alive || Mathf.Abs(actor.Position.x - b.X) >= .8f || Mathf.Abs(actor.Position.y - b.Y) >= .8f)
                        b.PassActors.RemoveAt(i);
                }
            }
        }

        void ResolveExplosions()
        {
            var pending = new Queue<BombState>();
            var queued = new HashSet<int>();
            var revealed = new List<PickupState>();
            foreach (var b in State.Bombs)
            {
                if (b.Timer <= .00001f || HasFlame(b.X, b.Y)) { pending.Enqueue(b); queued.Add(b.Id); }
            }
            while (pending.Count > 0)
            {
                var bomb = pending.Dequeue();
                if (!State.Bombs.Remove(bomb)) continue;
                Events.Add(new GameEvent(GameEventKind.Exploded, bomb.X, bomb.Y, bomb.OwnerId));
                Ignite(bomb.X, bomb.Y);
                foreach (var dir in Directions)
                {
                    for (int distance = 1; distance <= bomb.Range; distance++)
                    {
                        int x = bomb.X + dir.x * distance, y = bomb.Y + dir.y * distance;
                        var tile = GetTile(x, y);
                        if (tile == TileKind.Pillar) break;
                        Ignite(x, y);
                        if (tile == TileKind.Crate)
                        {
                            int index = Index(x, y);
                            State.Tiles[index] = (int)TileKind.Floor;
                            State.Score += 10;
                            Events.Add(new GameEvent(GameEventKind.CrateDestroyed, x, y, bomb.OwnerId));
                            if (State.HiddenDrops[index] >= 0)
                                revealed.Add(new PickupState { X = x, Y = y, Kind = (PickupKind)State.HiddenDrops[index] });
                            State.HiddenDrops[index] = -1;
                            break;
                        }
                        var hit = BombAt(x, y);
                        if (hit != null)
                        {
                            if (queued.Add(hit.Id)) pending.Enqueue(hit);
                            break;
                        }
                    }
                }
            }
            // Drops appear beneath the dying blast; a subsequent bomb can burn them away.
            State.Pickups.AddRange(revealed);
        }

        void Ignite(int x, int y)
        {
            for (int i = State.Pickups.Count - 1; i >= 0; i--)
                if (State.Pickups[i].X == x && State.Pickups[i].Y == y) State.Pickups.RemoveAt(i);
            foreach (var flame in State.Flames)
                if (flame.X == x && flame.Y == y) { flame.Timer = FlameLife; return; }
            State.Flames.Add(new FlameState { X = x, Y = y, Timer = FlameLife });
        }

        void ResolveActorsAndPickups()
        {
            foreach (var actor in State.Actors)
            {
                if (!actor.Alive) continue;
                var position = actor.Position;
                if (actor.Shield <= 0)
                {
                    foreach (var flame in State.Flames)
                    {
                        if (Mathf.Abs(position.x - flame.X) < CollisionRadius && Mathf.Abs(position.y - flame.Y) < CollisionRadius)
                        {
                            actor.Alive = false;
                            var cell = OccupiedCell(actor);
                            Events.Add(new GameEvent(GameEventKind.ActorDied, cell.x, cell.y, actor.Id));
                            if (actor.HumanIndex < 0) State.Score += 150 + State.Stage * 50;
                            break;
                        }
                    }
                }
                if (!actor.Alive) continue;
                for (int i = State.Pickups.Count - 1; i >= 0; i--)
                {
                    var pickup = State.Pickups[i];
                    if (Mathf.Abs(position.x - pickup.X) > .38f || Mathf.Abs(position.y - pickup.Y) > .38f || HasFlame(pickup.X, pickup.Y)) continue;
                    switch (pickup.Kind)
                    {
                        case PickupKind.BombUp: actor.BombCapacity = Mathf.Min(5, actor.BombCapacity + 1); break;
                        case PickupKind.SpeedUp: actor.Speed = Mathf.Min(5.8f, actor.Speed + .4f); break;
                        case PickupKind.FireUp: actor.FireRange = Mathf.Min(7, actor.FireRange + 1); break;
                        case PickupKind.WallPass: actor.WallPass = true; break;
                    }
                    State.Pickups.RemoveAt(i);
                    if (actor.HumanIndex >= 0) State.Score += 50;
                    Events.Add(new GameEvent(GameEventKind.PickupTaken, pickup.X, pickup.Y, actor.Id, pickup.Kind));
                }
            }
        }

        void CheckOutcome()
        {
            bool humanAlive = false;
            foreach (var a in State.Actors) if (a.Alive && a.HumanIndex >= 0) humanAlive = true;
            if (!humanAlive || State.TimeRemaining <= 0)
            {
                State.Outcome = MatchOutcome.Lost;
                Events.Add(new GameEvent(GameEventKind.StageLost, 0, 0));
            }
            else if (AliveBots == 0)
            {
                State.Outcome = MatchOutcome.Won;
                State.Score += 500 * (State.Stage + 1) + Mathf.FloorToInt(State.TimeRemaining) * 2;
                Events.Add(new GameEvent(GameEventKind.StageWon, 0, 0));
            }
        }

        public void StartNextStage()
        {
            if (State.Outcome != MatchOutcome.Won || State.Stage >= 4) return;
            State.Stage++;
            Events.Clear();
            GenerateStage();
        }

        void GenerateStage()
        {
            State.Width = 15; State.Height = 11;
            State.Elapsed = 0; State.TimeRemaining = 210 + State.Stage * 15;
            State.Outcome = MatchOutcome.None;
            State.Tiles = new int[State.Width * State.Height];
            State.HiddenDrops = new int[State.Tiles.Length];
            State.Actors.Clear(); State.Bombs.Clear(); State.Flames.Clear(); State.Pickups.Clear();
            for (int y = 0; y < State.Height; y++)
                for (int x = 0; x < State.Width; x++)
                {
                    int index = Index(x, y);
                    State.HiddenDrops[index] = -1;
                    if (x == 0 || y == 0 || x == State.Width - 1 || y == State.Height - 1 || (x % 2 == 0 && y % 2 == 0))
                        State.Tiles[index] = (int)TileKind.Pillar;
                    else if (Random01() < .52f + State.Stage * .025f)
                    {
                        State.Tiles[index] = (int)TileKind.Crate;
                        if (Random01() < .38f) State.HiddenDrops[index] = RandomDrop();
                    }
                }
            for (int p = 0; p < State.PlayerCount; p++)
            {
                int y = p == 0 ? 1 : 9;
                ClearSpawn(1, y, true);
                // Every arena starts with fresh players: collected power-ups belong to that arena only.
                State.Actors.Add(NewActor(p, p, 1, y));
            }
            var botSpawns = new[] { new Vector2Int(13, 9), new Vector2Int(13, 1), new Vector2Int(9, 5),
                new Vector2Int(7, 9), new Vector2Int(7, 1), new Vector2Int(13, 5) };
            for (int i = 0; i < 2 + State.Stage; i++)
            {
                var point = botSpawns[i];
                ClearSpawn(point.x, point.y, false);
                var bot = NewActor(10 + i, -1, point.x, point.y);
                bot.BotKind = State.Stage == 0 ? 0 : (i + State.Stage - 1) % 4;
                bot.Speed = 2.35f + State.Stage * .23f + (bot.BotKind == 3 ? .4f : 0);
                bot.FireRange = 2 + (State.Stage >= 2 ? 1 : 0) + (bot.BotKind == 2 ? 1 : 0);
                bot.BombCapacity = State.Stage >= 3 && bot.BotKind == 2 ? 2 : 1;
                bot.ThinkTimer = 1.4f + Random01() * 1.2f;
                State.Actors.Add(bot);
            }
            // Every upgrade is obtainable. The first two are close to the human side.
            var crates = new List<int>();
            for (int i = 0; i < State.Tiles.Length; i++) if (State.Tiles[i] == (int)TileKind.Crate) crates.Add(i);
            crates.Sort((a, b) => (a % State.Width).CompareTo(b % State.Width));
            for (int kind = 0; kind < 4 && kind < crates.Count; kind++)
                State.HiddenDrops[crates[kind * Mathf.Max(1, crates.Count / 6) % crates.Count]] = kind;
        }

        ActorState NewActor(int id, int humanIndex, int x, int y)
        {
            return new ActorState { Id = id, HumanIndex = humanIndex, X = x, Y = y, FromX = x, FromY = y,
                MoveProgress = 1, Shield = 1.2f, FacingX = humanIndex >= 0 ? 1 : -1, FacingY = 0 };
        }

        void ClearSpawn(int x, int y, bool human)
        {
            // A small circuit around a pillar gives every spawn a genuine route around its first blast.
            int minX = Mathf.Clamp(x - 1, 1, State.Width - 4);
            int minY = Mathf.Clamp(y - 1, 1, State.Height - 4);
            if (minX % 2 == 0) minX--;
            if (minY % 2 == 0) minY--;
            for (int yy = minY; yy <= minY + 2; yy++)
                for (int xx = minX; xx <= minX + 2; xx++)
                    if (GetTile(xx, yy) != TileKind.Pillar)
                    { State.Tiles[Index(xx, yy)] = (int)TileKind.Floor; State.HiddenDrops[Index(xx, yy)] = -1; }
            State.Tiles[Index(x, y)] = (int)TileKind.Floor;
            State.HiddenDrops[Index(x, y)] = -1;
            // Human spawn exits always lead to a destructible frontier, never a sealed pocket.
            if (human)
            {
                int frontier = Index(4, y);
                State.Tiles[frontier] = (int)TileKind.Crate;
                State.HiddenDrops[frontier] = (int)(y == 1 ? PickupKind.BombUp : PickupKind.FireUp);
            }
        }

        int RandomDrop()
        {
            float roll = Random01();
            return roll < .30f ? (int)PickupKind.BombUp : roll < .59f ? (int)PickupKind.FireUp :
                roll < .88f ? (int)PickupKind.SpeedUp : (int)PickupKind.WallPass;
        }

        float Random01()
        {
            uint x = State.RngState;
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            State.RngState = x;
            return (x & 0x00FFFFFF) / 16777216f;
        }

        sealed class DangerMap
        {
            public readonly float[] Start, End;
            public DangerMap(int size)
            {
                Start = new float[size]; End = new float[size];
                for (int i = 0; i < size; i++) Start[i] = float.PositiveInfinity;
            }
            public void Add(int index, float start, float end)
            { Start[index] = Mathf.Min(Start[index], start); End[index] = Mathf.Max(End[index], end); }
            public bool Safe(int index, float enter, float leave)
            { return leave < Start[index] - .12f || enter > End[index] + .12f; }
        }

        DangerMap PredictDanger(BombState extra = null)
        {
            var map = new DangerMap(State.Tiles.Length);
            foreach (var flame in State.Flames) map.Add(Index(flame.X, flame.Y), 0, flame.Timer);
            var bombs = new List<BombState>(State.Bombs);
            if (extra != null) bombs.Add(extra);
            var times = new float[bombs.Count];
            for (int i = 0; i < bombs.Count; i++) times[i] = Mathf.Max(0, bombs[i].Timer);
            // Propagate earliest fuse time through the entire chain, including transitive links.
            for (int pass = 0; pass < bombs.Count; pass++)
            {
                bool changed = false;
                for (int i = 0; i < bombs.Count; i++)
                    for (int j = 0; j < bombs.Count; j++)
                        if (times[j] > times[i] && BombHits(bombs[i], bombs[j].X, bombs[j].Y))
                        { times[j] = times[i]; changed = true; }
                if (!changed) break;
            }
            for (int i = 0; i < bombs.Count; i++)
            {
                var bomb = bombs[i];
                map.Add(Index(bomb.X, bomb.Y), times[i], times[i] + FlameLife);
                foreach (var direction in Directions)
                    for (int distance = 1; distance <= bomb.Range; distance++)
                    {
                        int x = bomb.X + direction.x * distance, y = bomb.Y + direction.y * distance;
                        var tile = GetTile(x, y);
                        if (tile == TileKind.Pillar) break;
                        map.Add(Index(x, y), times[i], times[i] + FlameLife);
                        if (tile == TileKind.Crate) break;
                        bool blocked = false;
                        for (int j = 0; j < bombs.Count; j++)
                            if (j != i && bombs[j].X == x && bombs[j].Y == y) { blocked = true; break; }
                        if (blocked) break;
                    }
            }
            return map;
        }

        bool BombHits(BombState bomb, int x, int y)
        {
            if (bomb.X != x && bomb.Y != y) return false;
            int distance = Mathf.Abs(bomb.X - x) + Mathf.Abs(bomb.Y - y);
            if (distance > bomb.Range) return false;
            int dx = Math.Sign(x - bomb.X), dy = Math.Sign(y - bomb.Y);
            for (int n = 1; n < distance; n++)
                if (GetTile(bomb.X + dx * n, bomb.Y + dy * n) != TileKind.Floor) return false;
            return GetTile(x, y) != TileKind.Pillar;
        }

        Vector2Int ChooseBotDirection(ActorState bot)
        {
            if (bot.ThinkTimer > 0) return Vector2Int.zero;
            var danger = PredictDanger();
            int current = Index(bot.X, bot.Y);
            if (danger.Start[current] < float.PositiveInfinity)
            {
                var escape = FindEscape(bot, danger, null);
                if (escape != Vector2Int.zero) return escape;
                bot.ThinkTimer = .08f;
                return Vector2Int.zero;
            }
            bool humanInRange = false;
            foreach (var a in State.Actors)
                if (a.Alive && a.HumanIndex >= 0 && BombHits(new BombState { X = bot.X, Y = bot.Y, Range = bot.FireRange }, a.X, a.Y)) humanInRange = true;
            int crates = CountBlastCrates(bot.X, bot.Y, bot.FireRange);
            if (bot.BombCooldown <= 0 && (humanInRange || crates > 0))
            {
                var candidate = new BombState { X = bot.X, Y = bot.Y, Range = bot.FireRange, Timer = Fuse, OwnerId = bot.Id, Id = -1 };
                var future = PredictDanger(candidate);
                var escape = FindEscape(bot, future, candidate);
                if (escape != Vector2Int.zero && PlaceBomb(bot)) return escape;
            }
            var direction = FindHuntDirection(bot, danger);
            if (direction == Vector2Int.zero) bot.ThinkTimer = .10f + Random01() * .12f;
            return direction;
        }

        Vector2Int FindEscape(ActorState actor, DangerMap danger, BombState hypothetical)
        {
            int size = State.Tiles.Length, start = Index(actor.X, actor.Y);
            var visited = new bool[size]; var first = new Vector2Int[size]; var distance = new int[size];
            var queue = new Queue<int>(); queue.Enqueue(start); visited[start] = true;
            float step = 1 / actor.Speed;
            int best = -1; float bestScore = float.NegativeInfinity;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int x = current % State.Width, y = current / State.Width;
                float arrival = distance[current] * step;
                if (current != start && danger.Safe(current, arrival - step * .65f, 3.25f))
                {
                    int exits = 0;
                    foreach (var d in Directions) if (IsWalkable(x + d.x, y + d.y, actor)) exits++;
                    float score = exits * .35f - distance[current];
                    if (score > bestScore) { bestScore = score; best = current; }
                    // Routes beyond the fuse cannot improve a reachable nearby refuge.
                    if (distance[current] >= 4) continue;
                }
                if (distance[current] >= 9) continue;
                foreach (var direction in Directions)
                {
                    int nx = x + direction.x, ny = y + direction.y;
                    if (!IsWalkable(nx, ny, actor)) continue;
                    if (hypothetical != null && nx == hypothetical.X && ny == hypothetical.Y) continue;
                    int next = Index(nx, ny);
                    if (visited[next]) continue;
                    float nextArrival = arrival + step;
                    if (!danger.Safe(next, arrival + step * .25f, nextArrival + step * .6f)) continue;
                    // A blast touching the departure tile while the character is still crossing is lethal too.
                    if (!danger.Safe(current, arrival, arrival + step * .65f)) continue;
                    visited[next] = true; distance[next] = distance[current] + 1;
                    first[next] = current == start ? direction : first[current];
                    queue.Enqueue(next);
                }
            }
            return best >= 0 ? first[best] : Vector2Int.zero;
        }

        Vector2Int FindHuntDirection(ActorState bot, DangerMap danger)
        {
            int size = State.Tiles.Length, start = Index(bot.X, bot.Y);
            var distance = new int[size]; var first = new Vector2Int[size];
            for (int i = 0; i < size; i++) distance[i] = -1;
            var queue = new Queue<int>(); queue.Enqueue(start); distance[start] = 0;
            int best = -1; float bestScore = float.NegativeInfinity;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue(), x = current % State.Width, y = current / State.Width;
                if (current != start)
                {
                    int humanDistance = 100;
                    foreach (var human in State.Actors)
                        if (human.Alive && human.HumanIndex >= 0)
                            humanDistance = Mathf.Min(humanDistance, Mathf.Abs(human.X - x) + Mathf.Abs(human.Y - y));
                    int crates = CountBlastCrates(x, y, bot.FireRange);
                    float score = -humanDistance * (bot.BotKind == 1 ? 1.8f : 1.2f) - distance[current] * .35f + crates * .9f;
                    foreach (var pickup in State.Pickups)
                        if (pickup.X == x && pickup.Y == y) score += bot.BotKind == 0 ? 8 : 4;
                    // Slight saved variation keeps bots from marching in identical lockstep.
                    score += ((current * 17 + bot.Id * 11 + State.Stage * 3) % 9) * .025f;
                    if (score > bestScore) { bestScore = score; best = current; }
                }
                if (distance[current] >= 18) continue;
                foreach (var direction in Directions)
                {
                    int nx = x + direction.x, ny = y + direction.y;
                    if (!IsWalkable(nx, ny, bot)) continue;
                    int next = Index(nx, ny);
                    if (distance[next] >= 0) continue;
                    // Hunting never enters a currently threatened lane; evasion has its own timed pathfinder.
                    if (danger.Start[next] < float.PositiveInfinity) continue;
                    distance[next] = distance[current] + 1;
                    first[next] = current == start ? direction : first[current];
                    queue.Enqueue(next);
                }
            }
            return best >= 0 ? first[best] : Vector2Int.zero;
        }

        int CountBlastCrates(int x, int y, int range)
        {
            int count = 0;
            foreach (var direction in Directions)
                for (int n = 1; n <= range; n++)
                {
                    var tile = GetTile(x + direction.x * n, y + direction.y * n);
                    if (tile == TileKind.Pillar) break;
                    if (tile == TileKind.Crate) { count++; break; }
                }
            return count;
        }

        int Index(int x, int y) => y * State.Width + x;
        bool Inside(int x, int y) => x >= 0 && y >= 0 && x < State.Width && y < State.Height;
        static Vector2Int OccupiedCell(ActorState a) => new Vector2Int(Mathf.RoundToInt(a.Position.x), Mathf.RoundToInt(a.Position.y));
        static Vector2Int Normalize(Vector2Int direction) => direction.x != 0 ? new Vector2Int(Math.Sign(direction.x), 0) : new Vector2Int(0, Math.Sign(direction.y));
        ActorState FindActor(int id) => State.Actors.Find(a => a.Id == id);
        BombState BombAt(int x, int y) => State.Bombs.Find(b => b.X == x && b.Y == y);
        bool HasFlame(int x, int y) => State.Flames.Exists(f => f.X == x && f.Y == y && f.Timer > 0);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        static void ValidateState(SessionState s)
        {
            if (s == null || s.Version != 1 || s.Width != 15 || s.Height != 11 || s.Stage < 0 || s.Stage > 4 ||
                s.PlayerCount < 1 || s.PlayerCount > 2 || s.RngState == 0 || s.Tiles == null || s.Tiles.Length != 165 ||
                s.HiddenDrops == null || s.HiddenDrops.Length != 165 || s.Actors == null || s.Actors.Count > 32 ||
                s.Bombs == null || s.Bombs.Count > 128 || s.Flames == null || s.Flames.Count > 1024 ||
                s.Pickups == null || s.Pickups.Count > 165 || !Finite(s.Elapsed) || !Finite(s.TimeRemaining) ||
                s.Outcome < MatchOutcome.None || s.Outcome > MatchOutcome.Lost)
                throw new ArgumentException("The saved campaign is incomplete or incompatible.");
            for (int i = 0; i < 165; i++)
                if (s.Tiles[i] < 0 || s.Tiles[i] > 2 || s.HiddenDrops[i] < -1 || s.HiddenDrops[i] > 3)
                    throw new ArgumentException("The saved arena is invalid.");
            var ids = new HashSet<int>(); var humans = new HashSet<int>();
            foreach (var a in s.Actors)
            {
                if (a == null || !ids.Add(a.Id) || a.X < 0 || a.X >= 15 || a.Y < 0 || a.Y >= 11 ||
                    a.FromX < 0 || a.FromX >= 15 || a.FromY < 0 || a.FromY >= 11 ||
                    !Finite(a.MoveProgress) || a.MoveProgress < 0 || a.MoveProgress > 1 || !Finite(a.MoveDuration) || a.MoveDuration <= 0 ||
                    !Finite(a.Speed) || a.Speed < 1 || a.Speed > 7 || !Finite(a.Shield) || !Finite(a.ThinkTimer) || !Finite(a.BombCooldown) ||
                    a.FireRange < 1 || a.FireRange > 8 || a.BombCapacity < 1 || a.BombCapacity > 5 || a.HumanIndex < -1 || a.HumanIndex >= s.PlayerCount)
                    throw new ArgumentException("A saved character is invalid.");
                if (a.HumanIndex >= 0 && !humans.Add(a.HumanIndex)) throw new ArgumentException("Duplicate saved player.");
            }
            if (humans.Count != s.PlayerCount) throw new ArgumentException("A saved player is missing.");
            ids.Clear();
            foreach (var b in s.Bombs)
                if (b == null || !ids.Add(b.Id) || b.PassActors == null || b.X < 0 || b.X >= 15 || b.Y < 0 || b.Y >= 11 ||
                    !Finite(b.Timer) || b.Range < 1 || b.Range > 8) throw new ArgumentException("A saved bomb is invalid.");
            foreach (var f in s.Flames)
                if (f == null || f.X < 0 || f.X >= 15 || f.Y < 0 || f.Y >= 11 || !Finite(f.Timer)) throw new ArgumentException("A saved flame is invalid.");
            foreach (var p in s.Pickups)
                if (p == null || p.X < 0 || p.X >= 15 || p.Y < 0 || p.Y >= 11 || p.Kind < PickupKind.BombUp || p.Kind > PickupKind.WallPass)
                    throw new ArgumentException("A saved upgrade is invalid.");
        }
    }
}
