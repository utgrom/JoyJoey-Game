using System.Collections.Generic;
using UnityEngine;

namespace JoyJoey.Sandbox
{
    /// <summary>Scene geometry and spawn policy consumed by the shared 60 Hz model.</summary>
    public sealed class CombatWorld
    {
        public readonly List<ArenaPlatform> Platforms = new List<ArenaPlatform>();
        public Vector2 PlayerSpawn = new Vector2(-3, 0);
        public Vector2 DummySpawn = Vector2.zero;
        public float RespawnBelowY = -7;
        public float LeftBoundary = -20, RightBoundary = 20;
        public bool TargetEnabled = true;

        public bool IsOutside(Vector2 position) => position.y < RespawnBelowY ||
            position.x < LeftBoundary || position.x > RightBoundary;

        /// <summary>Legacy arena remains the default for the practice scene and existing tools.</summary>
        public static CombatWorld CreateSandbox()
        {
            var world = new CombatWorld();
            world.Platforms.Add(new ArenaPlatform(-14, -1, 28, 1));
            world.Platforms.Add(new ArenaPlatform(-9, 2.4f, 5, .3f, true));
            world.Platforms.Add(new ArenaPlatform(4, 2.4f, 5, .3f, true));
            world.Platforms.Add(new ArenaPlatform(-2.5f, 5, 5, .3f, true));
            world.Platforms.Add(new ArenaPlatform(-14.7f, -1, .7f, 7));
            world.Platforms.Add(new ArenaPlatform(14, -1, .7f, 7));
            return world;
        }
    }
}
