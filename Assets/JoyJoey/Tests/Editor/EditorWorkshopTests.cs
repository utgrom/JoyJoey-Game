using System.Collections.Generic;
using System.IO;
using System.Linq;
using JoyJoey.Sandbox.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace JoyJoey.Sandbox.Tests
{
    public sealed class EditorWorkshopTests
    {
        const string Folder="Assets/JoyJoey/Tests/Temporary";
        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        readonly List<string> assets=new List<string>();
        T Own<T>(T value)where T:UnityEngine.Object{owned.Add(value);return value;}
        [TearDown] public void TearDown()
        {
            foreach(string path in assets)AssetDatabase.DeleteAsset(path);
            assets.Clear();
            foreach(var value in owned)if(value){Undo.ClearUndo(value);UnityEngine.Object.DestroyImmediate(value);}
            owned.Clear();
        }
        CombatCatalog Catalog()
        {
            var catalog=Own(ScriptableObject.CreateInstance<CombatCatalog>());catalog.name="Authored source";
            catalog.Moves.Add(new MoveDefinition{Id="tool",Title="Tool",Startup=3,Active=5,Recovery=2,
                Hitboxes=new List<HitboxTrack>{new HitboxTrack{Name="blade",StartFrame=4,EndFrame=7,
                    Keys=new List<HitboxKeyframe>{new HitboxKeyframe{Frame=4,Offset=new Vector2(1,.9f),Size=new Vector2(2,1)},
                        new HitboxKeyframe{Frame=6,Offset=new Vector2(2,1),Size=new Vector2(2,2)}}}}});
            catalog.Bindings.Add(new MoveBinding{Name="Basic tool",Action=ActionId.Basic,Sequence=new List<string>{"tool"}});
            return catalog;
        }
        CombatCatalog SavedCatalog()
        {
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/JoyJoey/Tests","Temporary");
            string path=AssetDatabase.GenerateUniqueAssetPath(Folder+"/WorkshopTest.asset");
            var catalog=Catalog();AssetDatabase.CreateAsset(catalog,path);assets.Add(path);AssetDatabase.SaveAssetIfDirty(catalog);
            return catalog;
        }

        [Test] public void ShippedCatalogCanBeOpenedAndSavedWithoutValidationErrors()
        {
            var catalog=Own(CombatDefaults.CreateCatalog());
            Assert.That(AttackWorkshop.Validate(catalog),Is.Null);
        }
        [Test] public void ValidationDoesNotMutateTheCatalog()
        {
            var catalog=Catalog();string before=JsonUtility.ToJson(catalog);
            Assert.That(AttackWorkshop.Validate(catalog),Is.Null);
            Assert.That(JsonUtility.ToJson(catalog),Is.EqualTo(before));
        }
        [Test] public void EditingADraftLeavesTheOriginalAssetPristine()
        {
            var source=SavedCatalog();string original=JsonUtility.ToJson(source);
            var draft=Own(AttackWorkshop.CreateDraft(source));
            draft.Moves[0].Title="Edited title";draft.Moves[0].Hitboxes[0].Hit.Damage=17;
            draft.Moves[0].Hitboxes[0].Keys[0].Offset=new Vector2(9,8);
            draft.Bindings[0].Sequence.Add("tool");
            Assert.That(JsonUtility.ToJson(source),Is.EqualTo(original));
            Assert.That(draft.hideFlags,Is.EqualTo(HideFlags.HideAndDontSave));
        }
        [Test] public void ExplicitCommitPersistsNestedChangesWithoutDraftNameOrHideFlags()
        {
            var source=SavedCatalog();string name=source.name;HideFlags flags=source.hideFlags;
            string path=AssetDatabase.GetAssetPath(source);
            var draft=Own(AttackWorkshop.CreateDraft(source));draft.Moves[0].Title="Saved attack";
            draft.Moves[0].Hitboxes[0].Hit.Damage=17;draft.Moves[0].Hitboxes[0].Keys[0].Offset=new Vector2(2.25f,1.75f);
            Assert.That(AttackWorkshop.CommitDraft(draft,source,out string error),Is.True,error);
            Undo.ClearUndo(source);Resources.UnloadAsset(source);
            var reloaded=AssetDatabase.LoadAssetAtPath<CombatCatalog>(path);
            Assert.That(reloaded,Is.Not.Null);Assert.That(reloaded.name,Is.EqualTo(name));Assert.That(reloaded.hideFlags,Is.EqualTo(flags));
            Assert.That(reloaded.Moves[0].Title,Is.EqualTo("Saved attack"));
            Assert.That(reloaded.Moves[0].Hitboxes[0].Hit.Damage,Is.EqualTo(17));
            Assert.That(reloaded.Moves[0].Hitboxes[0].Keys[0].Offset,Is.EqualTo(new Vector2(2.25f,1.75f)));
            Assert.That(File.ReadAllText(path),Does.Not.Contain("(borrador)"));
        }
        [Test] public void InvalidCommitDoesNotModifyTheSourceOrItsSavedFile()
        {
            var source=SavedCatalog();string original=JsonUtility.ToJson(source),path=AssetDatabase.GetAssetPath(source);
            string saved=File.ReadAllText(path);var draft=Own(AttackWorkshop.CreateDraft(source));
            draft.Moves[0].Recovery=-1;draft.Moves[0].Title="Must not be saved";
            Assert.That(AttackWorkshop.CommitDraft(draft,source,out string error),Is.False);
            Assert.That(error,Is.Not.Null.And.Not.Empty);
            Assert.That(JsonUtility.ToJson(source),Is.EqualTo(original));Assert.That(File.ReadAllText(path),Is.EqualTo(saved));
        }
        [Test] public void StartupEditShiftsWindowsAndKeysTogether()
        {
            var move=Catalog().Moves[0];int oldStartup=move.Startup,oldActive=move.Active;move.Startup=6;
            AttackWorkshop.RetimePhases(move,oldStartup,oldActive);var track=move.Hitboxes[0];
            Assert.That(track.StartFrame,Is.EqualTo(7));Assert.That(track.EndFrame,Is.EqualTo(10));
            Assert.That(track.Keys.Select(k=>k.Frame),Is.EqualTo(new[]{7,9}));
        }
        [Test] public void ShorteningActiveClampsWindowsAndKeepsUniqueKeys()
        {
            var catalog=Catalog();var move=catalog.Moves[0];int oldStartup=move.Startup,oldActive=move.Active;move.Active=2;move.Recovery=0;
            AttackWorkshop.RetimePhases(move,oldStartup,oldActive);var track=move.Hitboxes[0];
            Assert.That(track.StartFrame,Is.GreaterThanOrEqualTo(move.Startup));
            Assert.That(track.EndFrame,Is.LessThanOrEqualTo(move.Startup+move.Active));
            Assert.That(track.Keys.All(k=>k.Frame>=track.StartFrame&&k.Frame<track.EndFrame),Is.True);
            Assert.That(track.Keys.Select(k=>k.Frame).Distinct().Count(),Is.EqualTo(track.Keys.Count));
            Assert.That(AttackWorkshop.Validate(catalog),Is.Null);
        }
        [Test] public void ZeroActiveIsAllowedForUtilityMovesOnceTheirHitboxesAreRemoved()
        {
            var catalog=Catalog();var move=catalog.Moves[0];int oldStartup=move.Startup,oldActive=move.Active;
            move.Active=0;move.Recovery=0;AttackWorkshop.RetimePhases(move,oldStartup,oldActive);
            Assert.That(AttackWorkshop.Validate(catalog),Is.Not.Null,"A zero-active move cannot retain damaging windows");
            move.Hitboxes.Clear();Assert.That(AttackWorkshop.Validate(catalog),Is.Null);
        }
        [Test] public void NewAttackAndItsBindingRoundTripThroughAnAsset()
        {
            var source=SavedCatalog();var draft=Own(AttackWorkshop.CreateDraft(source));
            var added=JsonUtility.FromJson<MoveDefinition>(JsonUtility.ToJson(draft.Moves[0]));added.Id="new_authored";added.Title="New Authored";
            draft.Moves.Add(added);draft.Bindings.Add(new MoveBinding{Name="Air tool",Action=ActionId.Rag,Context=MoveContext.Air,
                Direction=MoveDirection.Horizontal,Stance=MoveStance.Normal,AnyRag=false,Rag=RagStyle.Illusionist,Priority=77,
                Sequence=new List<string>{added.Id,"tool"},ChargedMoveId="tool",ChargeFrames=11});
            Assert.That(AttackWorkshop.CommitDraft(draft,source,out string error),Is.True,error);
            string path=AssetDatabase.GetAssetPath(source);Undo.ClearUndo(source);Resources.UnloadAsset(source);
            var reloaded=AssetDatabase.LoadAssetAtPath<CombatCatalog>(path);var binding=reloaded.Bindings.Last();
            Assert.That(reloaded.Moves.Count,Is.EqualTo(2));Assert.That(reloaded.Find("new_authored"),Is.Not.Null);
            Assert.That(binding.Context,Is.EqualTo(MoveContext.Air));Assert.That(binding.Direction,Is.EqualTo(MoveDirection.Horizontal));
            Assert.That(binding.Priority,Is.EqualTo(77));Assert.That(binding.Sequence,Is.EqualTo(new[]{"new_authored","tool"}));
            Assert.That(binding.ChargeFrames,Is.EqualTo(11));Assert.That(binding.Rag,Is.EqualTo(RagStyle.Illusionist));
        }
        [Test] public void BindingThatReferencesANonexistentAttackCannotBeCommitted()
        {
            var catalog=Catalog();catalog.Bindings[0].Sequence[0]="missing_attack";
            Assert.That(AttackWorkshop.Validate(catalog),Is.Not.Null);
        }
        [Test] public void StaleDraftCannotOverwriteSourceEditsMadeOutsideTheWorkshop()
        {
            var source=SavedCatalog();string path=AssetDatabase.GetAssetPath(source),snapshot=JsonUtility.ToJson(source);
            var draft=Own(AttackWorkshop.CreateDraft(source));draft.Moves[0].Title="Local draft edit";
            source.Moves[0].Title="Newer inspector edit";EditorUtility.SetDirty(source);AssetDatabase.SaveAssetIfDirty(source);
            string saved=File.ReadAllText(path);
            Assert.That(AttackWorkshop.CommitDraft(draft,source,out string error,snapshot),Is.False);
            Assert.That(error,Does.Contain("fuente"));
            Assert.That(source.Moves[0].Title,Is.EqualTo("Newer inspector edit"));
            Assert.That(File.ReadAllText(path),Is.EqualTo(saved));
        }
        [Test] public void OlderSchemaDraftCannotDowngradeAnUpdatedSourceAsset()
        {
            var source=SavedCatalog();string path=AssetDatabase.GetAssetPath(source);
            var draft=Own(AttackWorkshop.CreateDraft(source));draft.Moves[0].Title="Old schema draft";
            source.SchemaVersion=draft.SchemaVersion+1;EditorUtility.SetDirty(source);AssetDatabase.SaveAssetIfDirty(source);
            string saved=File.ReadAllText(path);
            Assert.That(AttackWorkshop.CommitDraft(draft,source,out string error),Is.False);
            Assert.That(error,Does.Contain("esquema"));
            Assert.That(source.SchemaVersion,Is.EqualTo(draft.SchemaVersion+1));
            Assert.That(File.ReadAllText(path),Is.EqualTo(saved));
        }
        [Test] public void AnimationClipWithoutVisualPrefabCannotBeCommitted()
        {
            var source=SavedCatalog();string path=AssetDatabase.GetAssetPath(source),saved=File.ReadAllText(path);
            var draft=Own(AttackWorkshop.CreateDraft(source));draft.Moves[0].AnimationClip=Own(new AnimationClip());
            Assert.That(AttackWorkshop.Validate(draft),Does.Contain("PreviewPrefab"));
            Assert.That(AttackWorkshop.CommitDraft(draft,source,out string error),Is.False);
            Assert.That(error,Does.Contain("PreviewPrefab"));
            Assert.That(source.Moves[0].AnimationClip,Is.Null);
            Assert.That(File.ReadAllText(path),Is.EqualTo(saved));
        }
    }
}
