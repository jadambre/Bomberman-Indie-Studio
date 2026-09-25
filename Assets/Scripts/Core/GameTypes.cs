using System;
using System.Collections.Generic;
using UnityEngine;

namespace BombermanIndieStudio
{
    public enum TileKind { Floor, Pillar, Crate }
    public enum PickupKind { BombUp, SpeedUp, FireUp, WallPass }
    public enum MatchOutcome { None, Won, Lost }
    public enum GameScreen { MainMenu, Playing, Paused, StageClear, GameOver, Victory }
    public enum GameEventKind { BombPlaced, Exploded, CrateDestroyed, PickupTaken, ActorDied, Footstep, StageWon, StageLost }

    [Serializable] public class ActorState
    {
        public int Id, HumanIndex = -1, BotKind, X, Y, FromX, FromY;
        public float MoveProgress = 1, MoveDuration = .25f;
        public bool Alive = true;
        public int BombCapacity = 1, FireRange = 2;
        public float Speed = 3.4f, Shield = 2.5f;
        public bool WallPass;
        public float ThinkTimer, BombCooldown;
        public int FacingX, FacingY = -1;
        public Vector2 Position => Vector2.Lerp(new Vector2(FromX, FromY), new Vector2(X, Y), MoveProgress);
    }
    [Serializable] public class BombState
    {
        public int Id, OwnerId, X, Y, Range;
        public float Timer = 2.25f;
        public List<int> PassActors = new List<int>();
    }
    [Serializable] public class FlameState { public int X, Y; public float Timer = .55f; }
    [Serializable] public class PickupState { public int X, Y; public PickupKind Kind; }
    [Serializable] public class SessionState
    {
        public int Version = 1, Seed, Stage, PlayerCount = 1, Width = 15, Height = 11, NextBombId, Score;
        public uint RngState;
        public float Elapsed, TimeRemaining = 180;
        public MatchOutcome Outcome;
        public int[] Tiles, HiddenDrops;
        public List<ActorState> Actors = new List<ActorState>();
        public List<BombState> Bombs = new List<BombState>();
        public List<FlameState> Flames = new List<FlameState>();
        public List<PickupState> Pickups = new List<PickupState>();
    }
    public struct PlayerCommand
    {
        public Vector2Int Direction;
        public bool Bomb;
        public PlayerCommand(Vector2Int direction, bool bomb) { Direction = direction; Bomb = bomb; }
    }
    public struct GameEvent
    {
        public GameEventKind Kind;
        public int X, Y, ActorId;
        public PickupKind Pickup;
        public GameEvent(GameEventKind kind, int x, int y, int actorId = -1, PickupKind pickup = PickupKind.BombUp)
        { Kind = kind; X = x; Y = y; ActorId = actorId; Pickup = pickup; }
    }
    public static class Campaign
    {
        public static readonly string[] Names = { "MOSSBOUND", "SUNSTONE", "FROSTLINE", "AFTERGLOW", "THE CORE" };
        public static readonly string[] Subtitles = { "The overgrown outpost", "The amber excavation", "The frozen relay", "The midnight foundry", "The final detonation" };
        public static readonly string[] Briefings = {
            "Clear a path. Watch the fuse. Find your rhythm.",
            "Hunters have entered the arena. Keep an escape route.",
            "Faster rivals. Longer blasts. Think one move ahead.",
            "The tacticians are waiting. Turn their bombs against them.",
            "One final arena. Use everything you have learned."
        };
    }
}
