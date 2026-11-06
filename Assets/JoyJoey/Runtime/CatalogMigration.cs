using System.Collections.Generic;
using UnityEngine;

namespace JoyJoey.Sandbox
{
    /// <summary>Idempotent, conservative migration. Customized and unknown v1 attacks retain their authored values.</summary>
    public static class CatalogMigration
    {
        public const int CurrentVersion=2;
        public static bool Upgrade(CombatCatalog catalog)
        {
            if(catalog==null || catalog.SchemaVersion>=CurrentVersion)return false;
            var defaults=CombatDefaults.CreateCatalog();
            var legacy=LegacyCatalogSnapshot.CreateCatalog();
            try
            {
                if(catalog.Moves==null)catalog.Moves=new List<MoveDefinition>();
                for(int i=0;i<catalog.Moves.Count;i++)
                {
                    var current=catalog.Moves[i];if(current==null)continue;
                    var old=legacy.Find(current.Id);var replacement=defaults.Find(current.Id);
                    if(old!=null && replacement!=null && MatchesLegacy(current,old))
                        catalog.Moves[i]=Clone(replacement);
                    else ConvertLegacyHitbox(current);
                }
                foreach(var move in defaults.Moves)
                    if(catalog.Find(move.Id)==null)catalog.Moves.Add(Clone(move));
                if(catalog.Bindings==null)catalog.Bindings=new List<MoveBinding>();
                if(catalog.Bindings.Count==0)
                    foreach(var binding in defaults.Bindings)
                        catalog.Bindings.Add(JsonUtility.FromJson<MoveBinding>(JsonUtility.ToJson(binding)));
                catalog.SchemaVersion=CurrentVersion;
                return true;
            }
            finally
            {
                if(Application.isPlaying){Object.Destroy(defaults);Object.Destroy(legacy);}
                else {Object.DestroyImmediate(defaults);Object.DestroyImmediate(legacy);}
            }
        }
        static MoveDefinition Clone(MoveDefinition m)=>JsonUtility.FromJson<MoveDefinition>(JsonUtility.ToJson(m));
        static bool MatchesLegacy(MoveDefinition a,MoveDefinition b)
        {
            // Compare the v1 fields only: added fields can deserialize differently across Editor versions.
            // Any already-authored timeline/animation is treated as a user's customized move.
            if(a.AnimationClip!=null || a.PreviewPrefab!=null || (a.Hitboxes!=null && a.Hitboxes.Count>0) ||
                (a.Poses!=null && a.Poses.Count>0))return false;
            return a.Id==b.Id && a.Title==b.Title && a.Action==b.Action && a.Startup==b.Startup &&
                a.Active==b.Active && a.Recovery==b.Recovery && a.Damage.Equals(b.Damage) && a.Launch.Equals(b.Launch) &&
                a.Hitstun==b.Hitstun && a.Hitstop==b.Hitstop && a.Rehit==b.Rehit &&
                a.CancelOpen==b.CancelOpen && a.CancelClose==b.CancelClose && a.CancelRequiresHit==b.CancelRequiresHit &&
                a.Cancels==b.Cancels && a.Impulse.Equals(b.Impulse) && a.HitboxOffset.Equals(b.HitboxOffset) &&
                a.HitboxSize.Equals(b.HitboxSize) && a.MomentumRetention.Equals(b.MomentumRetention);
        }
        public static void ConvertLegacyHitbox(MoveDefinition move)
        {
            if(move.Hitboxes==null)move.Hitboxes=new List<HitboxTrack>();
            if(move.Hitboxes.Count>0)return;
            if(move.GravityScale<=0)move.GravityScale=1;
            if(move.AnimationSpeed<=0)move.AnimationSpeed=1;
            if(move.AnimationClip==null && move.PreviewPrefab==null && (move.Poses==null || move.Poses.Count==0))move.VerticalMomentumRetention=1;
            if(move.Active<=0 || move.Damage<=0 && move.Launch==Vector2.zero && move.Hitstun<=0)return;
            move.Hitboxes.Add(new HitboxTrack{Name="Impacto migrado",StartFrame=move.Startup,EndFrame=move.Startup+move.Active,HitGroup=0,
                Hit=new HitProperties{Damage=move.Damage,EnemyImpulse=move.Launch,EnemyVelocityMode=VelocityMode.Override,
                    Hitstun=move.Hitstun,Hitstop=move.Hitstop,Rehit=move.Rehit,JugglePower=Mathf.Max(8,move.Damage*1.5f),
                    ArmorDamage=Mathf.Max(6,move.Damage*1.4f)},
                Keys=new List<HitboxKeyframe>{new HitboxKeyframe{Frame=move.Startup,Offset=move.HitboxOffset,Size=move.HitboxSize}}});
        }
    }
}

// Frozen v1 seed, used only to distinguish untouched defaults from user edits.


namespace JoyJoey.Sandbox
{
    /// <summary>
    /// Initial sandbox values. All timings are simulation ticks at 60 Hz.
    /// The editor persists these defaults as ordinary, editable ScriptableObjects.
    /// </summary>
    internal static class LegacyCatalogSnapshot
    {
        public static CombatCatalog CreateCatalog()
        {
            var catalog = ScriptableObject.CreateInstance<CombatCatalog>();
            catalog.name = "Sandbox Combat Catalog";

            // Joey's shared attacks: light links, a launcher and a downward aerial.
            Add(catalog, "jab1", "Balloon Cuts I", ActionId.Basic, 4, 3, 12,
                5, V(1.1f, .8f), 25, 3, 5, 16);
            Add(catalog, "jab2", "Balloon Cuts II", ActionId.Basic, 4, 3, 12,
                5, V(1.3f, 1.2f), 27, 3, 5, 16);
            Add(catalog, "jab3", "Balloon Cuts III", ActionId.Basic, 5, 4, 17,
                9, V(5, 4), 31, 5, 7, 22);

            var slash = Add(catalog, "slash", "Balloon Slash", ActionId.Basic,
                6, 5, 17, 8, V(3.2f, 2), 30, 4, 8, 23);
            slash.Impulse = V(6, 0);
            slash.HitboxSize = V(2.2f, 1.8f);

            var launcher = Add(catalog, "launcher", "Pop Launch", ActionId.Basic,
                6, 4, 17, 7, V(1.2f, 11), 42, 5, 8, 24);
            launcher.Impulse = V(.5f, 2);
            launcher.HitboxOffset = V(.9f, .9f);
            launcher.HitboxSize = V(2, 2.4f);

            Add(catalog, "air1", "Balloon Air-Raid I", ActionId.Basic, 4, 4, 12,
                4, V(1.2f, 3), 27, 3, 6, 17);
            Add(catalog, "air2", "Balloon Air-Raid II", ActionId.Basic, 4, 4, 12,
                4, V(1.5f, 3.5f), 29, 3, 6, 17);
            Add(catalog, "air3", "Balloon Air-Raid III", ActionId.Basic, 5, 4, 17,
                8, V(5, 5), 32, 5, 7, 23);

            var dive = Add(catalog, "dive", "Pop Drop", ActionId.Basic,
                5, 14, 13, 9, V(2, 12), 37, 5, 7, 28);
            dive.Impulse = V(0, -17);
            dive.HitboxOffset = V(.45f, .35f);
            dive.HitboxSize = V(1.7f, 1.9f);

            // Strongman: deliberate knockback and a charged vertical finisher.
            var strong = Add(catalog, "ragStrong", "Titanenklatsch", ActionId.Rag,
                11, 5, 22, 14, V(5, 6), 34, 6, 13, 31);
            strong.Impulse = V(2.5f, 0);
            strong.HitboxOffset = V(1.25f, 1);
            strong.HitboxSize = V(2.7f, 2);

            var strongCharged = Add(catalog, "ragStrongCharged", "Titanenklatsch cargado",
                ActionId.Rag, 22, 6, 27, 24, V(7, 13), 47, 8, 25, 46);
            strongCharged.Impulse = V(3.5f, 0);
            strongCharged.HitboxOffset = V(1.3f, 1.1f);
            strongCharged.HitboxSize = V(3.2f, 2.6f);
            strongCharged.Cancels = CancelActions.Jump | CancelActions.Dash |
                CancelActions.Trick | CancelActions.Transform;

            // Jester: repeat presses and the held version add low-knockback hits.
            var jester = Add(catalog, "ragJester", "Jester Rattle", ActionId.Rag,
                5, 16, 14, 2.5f, V(.3f, 2), 22, 2, 7, 29);
            jester.Rehit = 4;

            var jesterCharged = Add(catalog, "ragJesterCharged", "Jester Encore",
                ActionId.Rag, 10, 20, 17, 2.5f, V(.3f, 2), 24, 2, 12, 40);
            jesterCharged.Rehit = 4;
            jesterCharged.HitboxOffset = V(1.05f, 1.05f);
            jesterCharged.HitboxSize = V(2.3f, 2.2f);

            // Illusionist: a chase attack and a stronger held displacement attack.
            var illusion = Add(catalog, "ragIllusion", "Curtain Call", ActionId.Rag,
                7, 4, 15, 7, V(1.8f, 7), 34, 4, 9, 22);
            illusion.Impulse = V(4, 1);
            illusion.HitboxSize = V(2.3f, 2.1f);

            var illusionCharged = Add(catalog, "ragIllusionCharged", "Vanishing Act",
                ActionId.Rag, 16, 5, 19, 13, V(3, 10), 40, 6, 18, 34);
            illusionCharged.HitboxOffset = V(.95f, 1.1f);
            illusionCharged.HitboxSize = V(2.6f, 2.5f);

            // Costume attacks replace shared basics while FUN is being consumed.
            var suitStrong = Add(catalog, "suitStrong", "Gigatitanenklatsch", ActionId.Basic,
                10, 6, 23, 19, V(6.5f, 8), 39, 7, 13, 32);
            suitStrong.Impulse = V(2, 0);
            suitStrong.HitboxOffset = V(1.4f, 1);
            suitStrong.HitboxSize = V(3.4f, 2.1f);

            var suitJester = Add(catalog, "suitJester", "Jester Crescendo", ActionId.Basic,
                4, 16, 12, 3.5f, V(.5f, 3), 27, 2, 6, 27);
            suitJester.Rehit = 4;
            suitJester.HitboxSize = V(2.2f, 2.1f);

            var suitIllusion = Add(catalog, "suitIllusion", "Mirror Cut", ActionId.Basic,
                5, 4, 13, 9, V(2, 6), 31, 4, 7, 19);
            suitIllusion.Impulse = V(4.5f, 0);
            suitIllusion.HitboxSize = V(2.6f, 1.9f);

            // Tricks provide distinct movement. They can cancel on whiff, unlike attacks.
            var trickStrong = Add(catalog, "trickStrong", "Strongman Bound", ActionId.Trick,
                3, 5, 14, 4, V(1, 8), 30, 3, 5, 18);
            trickStrong.Impulse = V(2, 12);
            trickStrong.HitboxOffset = V(.65f, .8f);
            trickStrong.HitboxSize = V(1.8f, 2.1f);
            trickStrong.CancelRequiresHit = false;
            trickStrong.Cancels = CancelActions.Basic | CancelActions.Rag | CancelActions.Dash;

            var trickJester = Add(catalog, "trickJester", "Jester Step", ActionId.Trick,
                2, 4, 12, 3, V(1, 5), 25, 2, 4, 15);
            trickJester.Impulse = V(6, 8);
            trickJester.CancelRequiresHit = false;
            trickJester.Cancels = CancelActions.Basic | CancelActions.Rag | CancelActions.Jump;

            var trickIllusion = Add(catalog, "trickIllusion", "Slip Through", ActionId.Trick,
                2, 3, 12, 0, V(0, 0), 0, 0, 4, 14);
            trickIllusion.Impulse = V(5, 2);
            trickIllusion.CancelRequiresHit = false;
            trickIllusion.Cancels = CancelActions.Basic | CancelActions.Rag |
                CancelActions.Jump | CancelActions.Dash;

            return catalog;
        }

        public static MotorTuning CreateTuning()
        {
            var tuning = ScriptableObject.CreateInstance<MotorTuning>();
            tuning.name = "Sandbox Motor Tuning";
            return tuning;
        }

        private static MoveDefinition Add(CombatCatalog catalog, string id, string title,
            ActionId action, int startup, int active, int recovery, float damage,
            Vector2 launch, int hitstun, int hitstop, int cancelOpen, int cancelClose)
        {
            var move = new MoveDefinition
            {
                Id = id,
                Title = title,
                Action = action,
                Startup = startup,
                Active = active,
                Recovery = recovery,
                Damage = damage,
                Launch = launch,
                Hitstun = hitstun,
                Hitstop = hitstop,
                HitboxOffset = V(1, .9f),
                HitboxSize = V(2, 1.8f),
                CancelOpen = cancelOpen,
                CancelClose = cancelClose,
                CancelRequiresHit = true,
                Cancels = CancelActions.All,
                MomentumRetention = 1
            };
            catalog.Moves.Add(move);
            return move;
        }

        private static Vector2 V(float x, float y) => new Vector2(x, y);
    }
}
