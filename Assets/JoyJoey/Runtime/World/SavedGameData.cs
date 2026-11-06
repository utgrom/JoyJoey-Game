using System;
using UnityEngine;

namespace JoyJoey.Game
{
    [Serializable]
    public sealed class SavedGameData
    {
        public const int CurrentVersion = 1;
        public int Version;
        public string SceneName;
        public Vector2 Position;
        public int Facing;
        public bool HasCheckpoint;
        public string SavedUtc;
        public string[] ProgressFlags;

        public SavedGameData Copy() => new SavedGameData
        {
            Version = Version, SceneName = SceneName, Position = Position, Facing = Facing,
            HasCheckpoint = HasCheckpoint, SavedUtc = SavedUtc,
            ProgressFlags = ProgressFlags != null ? (string[])ProgressFlags.Clone() : Array.Empty<string>()
        };
    }
}
