using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace JoyJoey.Sandbox.Tests
{
    public sealed class CatalogMigrationTests
    {
        readonly List<Object> owned=new List<Object>();
        [TearDown] public void TearDown(){foreach(var o in owned)if(o)Object.DestroyImmediate(o);owned.Clear();}
        CombatCatalog Legacy()
        {
            // Prefer the shipped serialized asset/back-up, while keeping the fixture repeatable after migration.
            var loaded=AssetDatabase.LoadAssetAtPath<CombatCatalog>("Assets/JoyJoey/Data/SandboxCombat.asset");
            if(loaded && loaded.SchemaVersion<2){var clone=Object.Instantiate(loaded);owned.Add(clone);return clone;}
            string[] paths=AssetDatabase.FindAssets("SandboxCombat_BeforeAuthoring t:CombatCatalog");
            foreach(string guid in paths)
            {
                var backup=AssetDatabase.LoadAssetAtPath<CombatCatalog>(AssetDatabase.GUIDToAssetPath(guid));
                if(backup && backup.SchemaVersion<2){var clone=Object.Instantiate(backup);owned.Add(clone);return clone;}
            }
            var frozen=typeof(CatalogMigration).Assembly.GetType("JoyJoey.Sandbox.LegacyCatalogSnapshot");
            var result=(CombatCatalog)frozen.GetMethod("CreateCatalog",BindingFlags.Public|BindingFlags.Static).Invoke(null,null);
            owned.Add(result);return result;
        }
        [Test] public void UnchangedLegacyMovesBecomeAuthoredLowRecoveryDefinitions()
        {
            var catalog=Legacy();Assert.That(catalog.Find("jab1").Recovery,Is.EqualTo(12));
            Assert.That(CatalogMigration.Upgrade(catalog),Is.True);
            Assert.That(catalog.SchemaVersion,Is.EqualTo(2));
            Assert.That(catalog.Find("jab1").Recovery,Is.Zero);
            Assert.That(catalog.Find("jab1").Cancels,Is.EqualTo(CancelActions.None));
            Assert.That(catalog.Find("jab1").Hitboxes.Count,Is.EqualTo(1));
            Assert.That(catalog.Find("launcher").FollowOnHold,Is.True);
            Assert.That(catalog.Find("launcher").Hitboxes[0].Hit.EnemyVelocityMode,Is.EqualTo(VelocityMode.Add));
            Assert.That(catalog.Find("airShot"),Is.Not.Null);
            Assert.That(catalog.Find("heavyDiagonal"),Is.Not.Null);
        }
        [Test] public void CustomizedLegacyAttackKeepsItsAuthoredValues()
        {
            var catalog=Legacy();var jab=catalog.Find("jab1");jab.Damage=17;jab.HitboxOffset=new Vector2(.7f,1.4f);
            jab.Impulse=new Vector2(4,2);int oldRecovery=jab.Recovery;
            CatalogMigration.Upgrade(catalog);jab=catalog.Find("jab1");
            Assert.That(jab.Damage,Is.EqualTo(17));Assert.That(jab.Recovery,Is.EqualTo(oldRecovery));
            Assert.That(jab.Impulse,Is.EqualTo(new Vector2(4,2)));
            Assert.That(jab.Hitboxes[0].Hit.Damage,Is.EqualTo(17));
            Assert.That(jab.Hitboxes[0].Keys[0].Offset,Is.EqualTo(new Vector2(.7f,1.4f)));
        }
        [Test] public void NewSerializedFieldDefaultsDoNotHideUntouchedLegacyMoves()
        {
            var catalog=Legacy();var jab=catalog.Find("jab1");
            jab.VerticalMomentumRetention=0;jab.GravityScale=0;jab.AnimationSpeed=0;jab.BlinkRange=0;
            CatalogMigration.Upgrade(catalog);jab=catalog.Find("jab1");
            Assert.That(jab.Recovery,Is.Zero);Assert.That(jab.GravityScale,Is.EqualTo(1));
            Assert.That(jab.MomentumRetention,Is.EqualTo(.2f).Within(.001f));
        }
        [Test] public void ExistingTimelineAndCustomBindingsArePreserved()
        {
            var catalog=Legacy();var jab=catalog.Find("jab1");
            jab.Hitboxes.Add(new HitboxTrack{Name="Custom exact track",StartFrame=1,EndFrame=4,
                Hit=new HitProperties{Damage=43},Keys=new List<HitboxKeyframe>{new HitboxKeyframe{Frame=1,Size=new Vector2(3,4)}}});
            var customBinding=new MoveBinding{Name="Custom binding",Action=ActionId.Rag,Sequence=new List<string>{"jab1"},Priority=70};
            catalog.Bindings.Add(customBinding);CatalogMigration.Upgrade(catalog);
            Assert.That(catalog.Find("jab1").Hitboxes[0].Hit.Damage,Is.EqualTo(43));
            Assert.That(catalog.Bindings.Count,Is.EqualTo(1));Assert.That(catalog.Bindings[0].Name,Is.EqualTo("Custom binding"));
        }
        [Test] public void UnknownMovesRemainAndUpgradeIsIdempotent()
        {
            var catalog=Legacy();catalog.Moves.Add(new MoveDefinition{Id="my_custom_attack",Damage=31,Startup=2,Active=7,Recovery=5});
            Assert.That(CatalogMigration.Upgrade(catalog),Is.True);int count=catalog.Moves.Count,bindings=catalog.Bindings.Count;
            string before=JsonUtility.ToJson(catalog);Assert.That(CatalogMigration.Upgrade(catalog),Is.False);
            Assert.That(catalog.Moves.Count,Is.EqualTo(count));Assert.That(catalog.Bindings.Count,Is.EqualTo(bindings));
            Assert.That(JsonUtility.ToJson(catalog),Is.EqualTo(before));
            Assert.That(catalog.Find("my_custom_attack").Hitboxes[0].Hit.Damage,Is.EqualTo(31));
        }
        [Test] public void StandaloneLegacyConversionNormalizesMissingNewFieldsWithoutReplacingTheCustomMove()
        {
            var move=new MoveDefinition{Id="unknown_legacy_tool",Title="User tool",Startup=2,Active=3,Recovery=4,Damage=9,
                GravityScale=0,AnimationSpeed=0,VerticalMomentumRetention=0,Impulse=new Vector2(3,2),MomentumRetention=.7f};
            CatalogMigration.ConvertLegacyHitbox(move);
            Assert.That(move.Id,Is.EqualTo("unknown_legacy_tool"));Assert.That(move.Recovery,Is.EqualTo(4));
            Assert.That(move.Impulse,Is.EqualTo(new Vector2(3,2)));Assert.That(move.MomentumRetention,Is.EqualTo(.7f));
            Assert.That(move.GravityScale,Is.EqualTo(1));Assert.That(move.AnimationSpeed,Is.EqualTo(1));
            Assert.That(move.VerticalMomentumRetention,Is.EqualTo(1));Assert.That(move.Hitboxes.Count,Is.EqualTo(1));
            Assert.That(move.Hitboxes[0].Hit.Damage,Is.EqualTo(9));
        }
    }
}
