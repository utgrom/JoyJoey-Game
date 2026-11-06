using NUnit.Framework;
using UnityEngine;

namespace JoyJoey.Sandbox.Tests
{
    public sealed class AttackSafetyTests
    {
        [Test] public void RemovingAllAuthoredHitboxesRemovesCollisionAndDebugBoxes()
        {
            var catalog=CombatDefaults.CreateCatalog();var tuning=CombatDefaults.CreateTuning();
            var profile=CombatDefaults.CreateLightEnemy();
            try
            {
                var attack=catalog.Find("jab1");attack.Hitboxes.Clear();
                Assert.That(attack.Damage,Is.GreaterThan(0),"Legacy data must not create an invisible hit");
                var simulation=new CombatSimulation(catalog,tuning,profile);
                simulation.Player.Position=new Vector2(-1,0);
                var press=new InputFrame();press.Commands.Add(new InputCommand(ActionId.Basic,InputEdge.Press,Vector2.zero));
                simulation.Step(press);
                for(int frame=0;frame<attack.Total+2;frame++)
                {
                    Assert.That(simulation.ActiveHitboxes.Count,Is.Zero);
                    simulation.Step(new InputFrame());
                }
                Assert.That(simulation.Dummy.Damage,Is.Zero);
            }
            finally{Object.DestroyImmediate(catalog);Object.DestroyImmediate(tuning);Object.DestroyImmediate(profile);}
        }

        [Test] public void VisualSamplingIgnoresRootMotionAndPreservesAnimatedChildren()
        {
            var prefab=new GameObject("PreviewRig");var arm=new GameObject("Arm");arm.transform.SetParent(prefab.transform,false);
            prefab.transform.localScale=new Vector3(2,3,1);
            var clip=new AnimationClip{legacy=true};
            clip.SetCurve("",typeof(Transform),"m_LocalPosition.x",AnimationCurve.Linear(0,20,1,30));
            clip.SetCurve("",typeof(Transform),"localEulerAnglesRaw.z",AnimationCurve.Linear(0,20,1,80));
            clip.SetCurve("",typeof(Transform),"m_LocalScale.x",AnimationCurve.Constant(0,1,99));
            clip.SetCurve("Arm",typeof(Transform),"m_LocalPosition.y",AnimationCurve.Linear(0,0,1,10));
            var visual=Object.Instantiate(prefab);
            try
            {
                var move=new MoveDefinition{AnimationClip=clip,PreviewPrefab=prefab,AnimationSpeed=1};
                AttackSampling.SampleVisual(move,visual,6);
                Assert.That(visual.transform.localPosition,Is.EqualTo(Vector3.zero));
                Assert.That(Quaternion.Angle(visual.transform.localRotation,Quaternion.identity),Is.LessThan(.001));
                Assert.That(visual.transform.localScale,Is.EqualTo(prefab.transform.localScale));
                Assert.That(visual.transform.Find("Arm").localPosition.y,Is.EqualTo(1).Within(.001));
            }
            finally{Object.DestroyImmediate(visual);Object.DestroyImmediate(prefab);Object.DestroyImmediate(clip);}
        }
    }
}
