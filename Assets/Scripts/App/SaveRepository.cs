using System;
using System.IO;
using UnityEngine;

namespace BombermanIndieStudio
{
    [Serializable] public class CampaignSave
    {
        public int Version = 1;
        public string SavedAt;
        public SessionState Session;
        public SessionState Checkpoint;
    }

    /// <summary>Versioned, atomic local saves. A previous valid save survives interrupted writes.</summary>
    public static class SaveRepository
    {
        #if UNITY_EDITOR
        public static string TestDirectory;
        #endif
        static string SaveDirectory
        {
            get
            {
                #if UNITY_EDITOR
                if(!string.IsNullOrEmpty(TestDirectory)) return TestDirectory;
                #endif
                // Keep the original Windows save identity when the displayed product title changes.
                #if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                return Path.Combine(Path.GetDirectoryName(Application.persistentDataPath), "EMBERGRID");
                #else
                return Application.persistentDataPath;
                #endif
            }
        }
        public static string PathName => Path.Combine(SaveDirectory, "campaign-v1.json");
        public static SessionState Clone(SessionState state) => JsonUtility.FromJson<SessionState>(JsonUtility.ToJson(state));

        public static bool Write(SessionState state, SessionState checkpoint, out string error)
        {
            error = null;
            try
            {
                Validate(state);
                var save = new CampaignSave { SavedAt = DateTime.UtcNow.ToString("o"), Session = state, Checkpoint = checkpoint ?? state };
                Directory.CreateDirectory(SaveDirectory);
                var temporary = PathName + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(save, true));
                if (File.Exists(PathName)) File.Replace(temporary, PathName, PathName + ".bak");
                else File.Move(temporary, PathName);
                return true;
            }
            catch (Exception ex) { error = "Could not save. Please check available disk space."; Debug.LogWarning("Save failed: " + ex.Message); return false; }
        }

        public static CampaignSave Read(out string notice)
        {
            notice = null;
            if (!File.Exists(PathName) && !File.Exists(PathName + ".bak")) return null;
            try { return ReadFile(PathName); }
            catch (Exception ex)
            {
                Debug.LogWarning("Save could not be read: " + ex.Message);
                try { var recovered = ReadFile(PathName + ".bak"); notice = "Recovered your previous save."; return recovered; }
                catch { notice = "Your save could not be loaded. You can start a new campaign."; return null; }
            }
        }

        static CampaignSave ReadFile(string path)
        {
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Save is too large.");
            var save = JsonUtility.FromJson<CampaignSave>(File.ReadAllText(path));
            if (save == null || save.Version != 1) throw new InvalidDataException("Unsupported save version.");
            Validate(save.Session);
            if (save.Checkpoint != null) Validate(save.Checkpoint);
            else save.Checkpoint = Clone(save.Session);
            return save;
        }

        public static void Validate(SessionState s)
        {
            if (s == null || s.Version != 1 || s.Width != 15 || s.Height != 11 || s.Stage < 0 || s.Stage > 4 || s.PlayerCount < 1 || s.PlayerCount > 2)
                throw new InvalidDataException("Invalid campaign metadata.");
            if (s.Tiles == null || s.Tiles.Length != s.Width * s.Height || s.HiddenDrops == null || s.HiddenDrops.Length != s.Tiles.Length)
                throw new InvalidDataException("Invalid arena.");
            if (s.Actors == null || s.Actors.Count < s.PlayerCount || s.Actors.Count > 20 || s.Bombs == null || s.Bombs.Count > 100 || s.Flames == null || s.Flames.Count > 3000 || s.Pickups == null || s.Pickups.Count > 165)
                throw new InvalidDataException("Invalid entity list.");
            if (float.IsNaN(s.TimeRemaining) || float.IsInfinity(s.TimeRemaining) || s.TimeRemaining < -1 || s.TimeRemaining > 3600)
                throw new InvalidDataException("Invalid timer.");
            var ids = new System.Collections.Generic.HashSet<int>();
            foreach (var a in s.Actors)
                if (a == null || !ids.Add(a.Id) || !InBounds(s, a.X, a.Y) || !InBounds(s, a.FromX, a.FromY) || a.Speed < .1f || a.Speed > 20 || float.IsNaN(a.Speed) || a.MoveProgress < 0 || a.MoveProgress > 1 || float.IsNaN(a.MoveProgress))
                    throw new InvalidDataException("Invalid actor.");
            foreach (var b in s.Bombs)
                if (b == null || !InBounds(s,b.X,b.Y) || b.Range < 1 || b.Range > 20 || float.IsNaN(b.Timer) || b.Timer > 30 || b.PassActors == null)
                    throw new InvalidDataException("Invalid bomb.");
            foreach (var f in s.Flames) if (f == null || !InBounds(s,f.X,f.Y) || float.IsNaN(f.Timer) || f.Timer > 30) throw new InvalidDataException("Invalid fire.");
            foreach (var p in s.Pickups) if (p == null || !InBounds(s,p.X,p.Y) || !Enum.IsDefined(typeof(PickupKind),p.Kind)) throw new InvalidDataException("Invalid pickup.");
            foreach (var t in s.Tiles) if (t < 0 || t > 2) throw new InvalidDataException("Invalid tile.");
            // Keep file validation aligned with the simulation's own stricter invariants.
            Simulation.FromState(s);
        }
        static bool InBounds(SessionState s,int x,int y) => x>=0 && y>=0 && x<s.Width && y<s.Height;
    }
}
