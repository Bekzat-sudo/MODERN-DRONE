using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ModernDrone.Core
{
    /// <summary>Per-mission completion and best score, persisted as JSON next to the control settings.</summary>
    [Serializable]
    public sealed class MissionProgress
    {
        [Serializable]
        public sealed class Entry
        {
            public string mission;
            public bool completed;
            public int bestScore;
            public int attempts;
        }

        const string FileName = "progress.json";

        public List<Entry> entries = new List<Entry>();

        static MissionProgress _current;
        public static MissionProgress Current => _current ??= Load();

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public Entry Get(string mission)
        {
            var e = entries.Find(x => x.mission == mission);
            if (e == null)
            {
                e = new Entry { mission = mission };
                entries.Add(e);
            }
            return e;
        }

        /// <summary>Records one finished attempt; keeps the best score and sticky completion.</summary>
        public void Record(string mission, bool success, int score)
        {
            var e = Get(mission);
            e.attempts++;
            e.completed |= success;
            e.bestScore = Mathf.Max(e.bestScore, score);
        }

        public static void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true)); }
            catch (Exception e) { Debug.LogWarning($"[ModernDrone] Could not save progress: {e.Message}"); }
        }

        static MissionProgress Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonUtility.FromJson<MissionProgress>(File.ReadAllText(FilePath)) ?? new MissionProgress();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ModernDrone] Could not read progress: {e.Message}");
            }
            return new MissionProgress();
        }
    }
}
