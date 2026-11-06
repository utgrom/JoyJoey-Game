using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace JoyJoey.Sandbox.Tests
{
    /// <summary>Tests authored tools through the same input and simulation path used in practice.</summary>
    public sealed class AttackAuthoringTests
    {
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }
        [TearDown] public void TearDown() { foreach(var value in owned) if(value) UnityEngine.Object.DestroyImmediate(value); owned.Clear(); }

        CombatSimulation Make(MoveDefinition move = null, EnemyProfile profile = null)
        {
            var catalog = Own(ScriptableObject.CreateInstance<CombatCatalog>());
            move = move ?? Attack();
            catalog.Moves.Add(move);
            catalog.Bindings.Add(new MoveBinding { Name="authored basic", Action=ActionId.Basic, Priority=100, Sequence=new List<string>{move.Id} });
            var tuning = Own(ScriptableObject.CreateInstance<MotorTuning>());
            tuning.Gravity=0;tuning.GroundFriction=0;tuning.AirFriction=0;
            profile=profile??Own(ScriptableObject.CreateInstance<EnemyProfile>());
            var sim=new CombatSimulation(catalog,tuning,profile);
            sim.Player.Position=new Vector2(-1,0);sim.Dummy.Position=Vector2.zero;
            return sim;
        }
        static MoveDefinition Attack()
        {
            var move=new MoveDefinition{Id="authored_tool",Title="Authored Tool",Startup=1,Active=2,Recovery=0,Cancels=CancelActions.None,
                MomentumRetention=1,VerticalMomentumRetention=1};
            move.Hitboxes.Add(Track());return move;
        }
        static HitboxTrack Track(int group=0)
        {
            return new HitboxTrack {Name="blade",StartFrame=1,EndFrame=3,HitGroup=group,
                Keys=new List<HitboxKeyframe>{new HitboxKeyframe{Frame=1,Offset=new Vector2(1,.8f),Size=new Vector2(2,1.8f)}},
                Hit=new HitProperties{Damage=4,EnemyImpulse=Vector2.zero,AttackerImpulse=Vector2.zero,Hitstop=0,Hitstun=12,ArmorDamage=4,JugglePower=4,Correction=false}};
        }
        static InputFrame Frame(ActionId? press=null, bool basic=false)
        {
            var input=new InputFrame{BasicHeld=basic};
            if(press.HasValue)input.Commands.Add(new InputCommand(press.Value,InputEdge.Press,Vector2.zero));
            return input;
        }
        static void Idle(CombatSimulation sim,int ticks){for(int i=0;i<ticks;i++)sim.Step(Frame());}
        static void Until(CombatSimulation sim,Func<bool> condition,int ticks=60)
        { for(int i=0;i<ticks&&!condition();i++)sim.Step(Frame());Assert.That(condition(),Is.True,"Authored condition timed out: "+sim.LastAction); }
        static void Strike(CombatSimulation sim)
        {
            float before=sim.Dummy.Damage;
            sim.Step(Frame(ActionId.Basic));Until(sim,()=>sim.Dummy.Damage>before);
        }
        static void Finish(CombatSimulation sim) => Until(sim,()=>sim.Player.Move==null);

        [Test] public void HitboxKeysInterpolateOffsetAndSizeRegardlessOfListOrder()
        {
            var box=new HitboxTrack{Keys=new List<HitboxKeyframe>{
                new HitboxKeyframe{Frame=6,Offset=new Vector2(3,2),Size=new Vector2(4,4)},
                new HitboxKeyframe{Frame=2,Offset=new Vector2(1,0),Size=new Vector2(2,2)}}};
            Rect sample=box.Sample(4);
            Assert.That(sample.center,Is.EqualTo(new Vector2(2,1)));Assert.That(sample.size,Is.EqualTo(new Vector2(3,3)));
            Assert.That(box.Sample(-10).center,Is.EqualTo(new Vector2(1,0)));
            Assert.That(box.Sample(100).center,Is.EqualTo(new Vector2(3,2)));
        }
        [Test] public void ActiveWindowIsStartInclusiveEndExclusive()
        {
            var box=Track();box.StartFrame=3;box.EndFrame=6;
            Assert.That(box.IsActive(2),Is.False);Assert.That(box.IsActive(3),Is.True);
            Assert.That(box.IsActive(5),Is.True);Assert.That(box.IsActive(6),Is.False);
        }
        [Test] public void StartupCannotHitEvenWhenATrackStartsTooEarly()
        {
            var move=Attack();move.Startup=3;move.Active=2;move.Recovery=3;
            move.Hitboxes[0].StartFrame=0;move.Hitboxes[0].EndFrame=8;
            var sim=Make(move);sim.Step(Frame(ActionId.Basic));Idle(sim,2);
            Assert.That(sim.Player.MoveTick,Is.EqualTo(2));Assert.That(sim.Dummy.Damage,Is.Zero);
            sim.Step(Frame());Assert.That(sim.Dummy.Damage,Is.EqualTo(4));
        }
        [Test] public void RecoveryCannotHitEvenWhenATrackExtendsBeyondTheActivePhase()
        {
            var move=Attack();move.Startup=3;move.Active=2;move.Recovery=3;
            var track=move.Hitboxes[0];track.StartFrame=0;track.EndFrame=8;
            track.Keys=new List<HitboxKeyframe>{
                new HitboxKeyframe{Frame=0,Offset=new Vector2(5,.8f),Size=new Vector2(.5f,1.8f)},
                new HitboxKeyframe{Frame=4,Offset=new Vector2(5,.8f),Size=new Vector2(.5f,1.8f)},
                new HitboxKeyframe{Frame=5,Offset=new Vector2(1,.8f),Size=new Vector2(.5f,1.8f)}};
            var sim=Make(move);sim.Step(Frame(ActionId.Basic));Idle(sim,7);
            Assert.That(sim.Dummy.Damage,Is.Zero,"Reaching the target during recovery must not create a hit");
        }
        [Test] public void PoseKeysUseShortestAngularPathAndFrameInterpolation()
        {
            var move=Attack();move.Poses.Add(new PoseKeyframe{Frame=0,BodyOffset=Vector2.zero,BodyAngle=350});
            move.Poses.Add(new PoseKeyframe{Frame=10,BodyOffset=new Vector2(2,4),BodyAngle=10});
            var pose=move.SamplePose(5);
            Assert.That(pose.BodyOffset,Is.EqualTo(new Vector2(1,2)));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(pose.BodyAngle,0)),Is.LessThan(.001f));
        }
        [Test] public void AnimationPreviewAndRuntimeUseTheSameSixtyHertzSamplingClock()
        {
            var clip=Own(new AnimationClip{legacy=true});
            clip.SetCurve("",typeof(Transform),"m_LocalPosition.x",AnimationCurve.Linear(0,0,1,10));
            var visual=Own(new GameObject("sample target"));var move=Attack();move.AnimationClip=clip;move.AnimationSpeed=2;
            float time=AttackSampling.AnimationTime(move,6);
            Assert.That(time,Is.EqualTo(.2f).Within(.0001));clip.SampleAnimation(visual,time);
            Assert.That(visual.transform.localPosition.x,Is.EqualTo(2).Within(.001));
            Assert.That(AttackSampling.AnimationTime(move,600),Is.EqualTo(clip.length));
        }
        [Test] public void ZeroStartupAndRecoveryHitWithoutAnExtraDeadFrame()
        {
            var move=Attack();move.Startup=0;move.Active=1;move.Recovery=0;
            move.Hitboxes[0].StartFrame=0;move.Hitboxes[0].EndFrame=1;
            var sim=Make(move);sim.Step(Frame(ActionId.Basic));
            Assert.That(move.Total,Is.EqualTo(1));Assert.That(sim.Dummy.Damage,Is.EqualTo(4));
        }
        [Test] public void NewMoveIsReachableUsingOnlyAnAuthoredBinding()
        {
            var move=Attack();move.Id="new_move_not_known_by_source";
            var sim=Make(move);sim.Step(Frame(ActionId.Basic));
            Assert.That(sim.Player.MoveId,Is.EqualTo(move.Id));
        }
        [Test] public void RuntimeCollisionUsesTheHitboxAtTheCurrentAnimationFrame()
        {
            var move=Attack();move.Active=3;var track=move.Hitboxes[0];track.EndFrame=4;
            track.Keys=new List<HitboxKeyframe>{
                new HitboxKeyframe{Frame=1,Offset=new Vector2(5,.8f),Size=new Vector2(.5f,1.8f)},
                new HitboxKeyframe{Frame=2,Offset=new Vector2(1,.8f),Size=new Vector2(.5f,1.8f)}};
            var sim=Make(move);sim.Step(Frame(ActionId.Basic));sim.Step(Frame());
            Assert.That(sim.Dummy.Damage,Is.Zero,"The early box is out of reach");
            sim.Step(Frame());Assert.That(sim.Dummy.Damage,Is.EqualTo(4),"The moved box reaches on authored frame 2");
        }
        [Test] public void SameHitGroupPreventsOverlappingBoxesFromDoubleHitting()
        {
            var move=Attack();move.Hitboxes.Add(Track());var sim=Make(move);
            Strike(sim);Idle(sim,10);Assert.That(sim.Dummy.Damage,Is.EqualTo(4));
        }
        [Test] public void IndependentHitGroupsCanApplyDistinctHits()
        {
            var move=Attack();move.Hitboxes.Add(Track(1));var sim=Make(move);
            Strike(sim);Assert.That(sim.Dummy.Damage,Is.EqualTo(8));
        }
        [Test] public void HitPropertiesBelongToTheWindowRatherThanTheWholeMove()
        {
            var move=Attack();move.Active=4;
            var late=Track(1);late.StartFrame=3;late.EndFrame=5;late.Hit.Damage=9;
            move.Hitboxes.Add(late);var sim=Make(move);Strike(sim);Until(sim,()=>sim.Dummy.Damage>4);
            Assert.That(sim.Dummy.Damage,Is.EqualTo(13));
        }
        [TestCase(VelocityMode.Add,5,2)]
        [TestCase(VelocityMode.Override,3,6)]
        public void EnemyImpulseCanAddToOrReplaceExistingMomentum(VelocityMode mode,float x,float y)
        {
            var move=Attack();move.Hitboxes[0].Hit.EnemyImpulse=new Vector2(3,6);move.Hitboxes[0].Hit.EnemyVelocityMode=mode;
            var sim=Make(move);sim.Player.Position=new Vector2(-1,3);sim.Player.Grounded=false;
            sim.Dummy.Position=new Vector2(0,3);sim.Dummy.Velocity=new Vector2(2,-4);sim.Dummy.Grounded=false;
            Strike(sim);Assert.That(sim.Dummy.Velocity.x,Is.EqualTo(x).Within(.05));Assert.That(sim.Dummy.Velocity.y,Is.EqualTo(y).Within(.001));
        }
        [TestCase(VelocityMode.Add,5,1)]
        [TestCase(VelocityMode.Override,1,3)]
        public void ImpactCanAlsoApplyASeparateImpulseToTheAttacker(VelocityMode mode,float x,float y)
        {
            var move=Attack();move.Hitboxes[0].Hit.AttackerImpulse=new Vector2(1,3);move.Hitboxes[0].Hit.AttackerVelocityMode=mode;
            var sim=Make(move);sim.Player.Position=new Vector2(-1,3);sim.Player.Grounded=false;sim.Player.Velocity=new Vector2(4,-2);
            sim.Dummy.Position=new Vector2(0,3);sim.Dummy.Grounded=false;
            Strike(sim);Assert.That(sim.Player.Velocity.x,Is.EqualTo(x).Within(.001));Assert.That(sim.Player.Velocity.y,Is.EqualTo(y).Within(.001));
        }
        [TestCase(.2f,.2f,2,2)]
        [TestCase(1,1,10,10)]
        [TestCase(.1f,.8f,1,8)]
        public void MomentumRetentionIsConfiguredPerAxisAndPerMove(float horizontal,float vertical,float expectedX,float expectedY)
        {
            var move=Attack();move.MomentumRetention=horizontal;move.VerticalMomentumRetention=vertical;
            var sim=Make(move);sim.Player.Position=new Vector2(-8,3);sim.Player.Grounded=false;sim.Player.Velocity=new Vector2(10,10);
            sim.Step(Frame(ActionId.Basic));
            Assert.That(sim.Player.Velocity.x,Is.EqualTo(expectedX).Within(.001));Assert.That(sim.Player.Velocity.y,Is.EqualTo(expectedY).Within(.001));
        }
        [Test] public void AWhiffNeverSchedulesPositionCorrection()
        {
            var move=Attack();move.Hitboxes[0].Hit.Correction=true;
            var sim=Make(move);sim.Player.Position=new Vector2(-5,0);Idle(sim,1);sim.Step(Frame(ActionId.Basic));Idle(sim,15);
            Assert.That(sim.Dummy.Damage,Is.Zero);Assert.That(sim.Player.Position.x,Is.EqualTo(-5).Within(.001));
            Assert.That(sim.Player.CorrectionRemaining,Is.EqualTo(Vector2.zero));Assert.That(sim.Dummy.CorrectionRemaining,Is.EqualTo(Vector2.zero));
        }
        [Test] public void CloseHitCorrectionIsGradualAndBounded()
        {
            var move=Attack();var hit=move.Hitboxes[0].Hit;hit.Correction=true;hit.MaxCorrection=.45f;hit.CorrectionSpeed=9;
            var sim=Make(move);sim.Player.Position=new Vector2(-.1f,0);
            Strike(sim);Assert.That(sim.Dummy.Position.x-sim.Player.Position.x,Is.LessThan(.21f),"No teleport on impact");
            Idle(sim,25);float separation=sim.Dummy.Position.x-sim.Player.Position.x;
            Assert.That(separation,Is.GreaterThan(.15f));Assert.That(separation,Is.LessThanOrEqualTo(.551f));
        }
        [Test] public void HeavyCorrectionWeightMovesThePlayerInsteadOfShovingTheEnemy()
        {
            var move=Attack();move.Hitboxes[0].Hit.Correction=true;
            var light=Make(move);light.Player.Position=new Vector2(-.1f,0);Strike(light);Idle(light,20);
            float lightPlayer=Mathf.Abs(light.Player.Position.x+.1f),lightEnemy=Mathf.Abs(light.Dummy.Position.x);
            var heavyProfile=Own(ScriptableObject.CreateInstance<EnemyProfile>());heavyProfile.CorrectionWeight=8;
            var heavy=Make(Attack(),heavyProfile);heavy.Catalog.Moves[0].Hitboxes[0].Hit.Correction=true;
            heavy.Player.Position=new Vector2(-.1f,0);Strike(heavy);Idle(heavy,20);
            Assert.That(Mathf.Abs(heavy.Player.Position.x+.1f),Is.GreaterThan(lightPlayer));
            Assert.That(Mathf.Abs(heavy.Dummy.Position.x),Is.LessThan(lightEnemy));
        }
        [Test] public void CorrectionRespectsSolidArenaWalls()
        {
            var move=Attack();move.Hitboxes[0].Hit.Correction=true;
            var sim=Make(move);sim.Player.Position=new Vector2(13.45f,0);sim.Dummy.Position=new Vector2(13.55f,0);
            Strike(sim);Idle(sim,30);
            Assert.That(sim.Dummy.Position.x,Is.LessThanOrEqualTo(14-sim.Tuning.Width*.5f+.001f));
        }
        [Test] public void GroundArmorMustBreakBeforeTheSameLauncherCanLift()
        {
            var profile=Own(ScriptableObject.CreateInstance<EnemyProfile>());profile.GroundArmor=12;profile.ArmorRegenPerSecond=0;
            var move=Attack();move.Hitboxes[0].Hit.EnemyImpulse=new Vector2(0,10);
            var sim=Make(move,profile);Strike(sim);
            Assert.That(sim.Dummy.Armor,Is.EqualTo(8));Assert.That(sim.Dummy.Velocity.y,Is.EqualTo(0));Finish(sim);
            Strike(sim);Assert.That(sim.Dummy.Armor,Is.EqualTo(4));Assert.That(sim.Dummy.Velocity.y,Is.EqualTo(0));Finish(sim);
            Strike(sim);Assert.That(sim.Dummy.Armor,Is.Zero);Assert.That(sim.Dummy.Velocity.y,Is.GreaterThan(0));
        }
        [Test] public void WeakAirHitsAccumulateJugglePowerOnlyWhenChainedQuickly()
        {
            var profile=Own(ScriptableObject.CreateInstance<EnemyProfile>());profile.AirJuggleThreshold=12;profile.JuggleWindow=10;
            var move=Attack();move.Hitboxes[0].Hit.EnemyImpulse=new Vector2(0,5);move.Hitboxes[0].Hit.EnemyVelocityMode=VelocityMode.Add;
            var sim=Make(move,profile);sim.Player.Position=new Vector2(-1,3);sim.Player.Grounded=false;
            sim.Dummy.Position=new Vector2(0,3);sim.Dummy.Grounded=false;sim.Dummy.Velocity=new Vector2(0,-4);
            Strike(sim);Assert.That(sim.Dummy.Velocity.y,Is.EqualTo(-4).Within(.001));Finish(sim);
            Strike(sim);Assert.That(sim.Dummy.Velocity.y,Is.EqualTo(-4).Within(.001));Finish(sim);
            Strike(sim);Assert.That(sim.Dummy.Velocity.y,Is.GreaterThan(-4));
        }
        [Test] public void ExpiredWeakAirHitsDoNotBuildTowardALaterJuggle()
        {
            var profile=Own(ScriptableObject.CreateInstance<EnemyProfile>());profile.AirJuggleThreshold=8;profile.JuggleWindow=5;
            var move=Attack();move.Hitboxes[0].Hit.EnemyImpulse=new Vector2(0,5);move.Hitboxes[0].Hit.EnemyVelocityMode=VelocityMode.Add;
            var sim=Make(move,profile);sim.Player.Position=new Vector2(-1,3);sim.Player.Grounded=false;
            sim.Dummy.Position=new Vector2(0,3);sim.Dummy.Grounded=false;sim.Dummy.Velocity=new Vector2(0,-1);
            Strike(sim);Idle(sim,10);Strike(sim);
            Assert.That(sim.Dummy.Velocity.y,Is.EqualTo(-1).Within(.001));Assert.That(sim.Dummy.JuggleMeter,Is.LessThanOrEqualTo(4));
        }
        [Test] public void ArmorRegeneratesOnlyAfterItsConfiguredGroundDelay()
        {
            var profile=Own(ScriptableObject.CreateInstance<EnemyProfile>());profile.GroundArmor=12;profile.ArmorRegenDelay=6;profile.ArmorRegenPerSecond=6;
            var sim=Make(Attack(),profile);Strike(sim);float damaged=sim.Dummy.Armor;
            Idle(sim,3);Assert.That(sim.Dummy.Armor,Is.EqualTo(damaged).Within(.001));
            Idle(sim,10);Assert.That(sim.Dummy.Armor,Is.GreaterThan(damaged));
        }
        [Test] public void HeldFollowDoesNotRequireAHit()
        {
            var move=Attack();move.FollowOnHold=true;move.FollowFrame=1;move.FollowImpulse=new Vector2(0,13);
            var sim=Make(move);sim.Player.Position=new Vector2(-7,0);sim.Step(Frame(ActionId.Basic,basic:true));
            for(int i=0;i<5&&sim.Player.Velocity.y<=0;i++)sim.Step(Frame(basic:true));
            Assert.That(sim.Dummy.Damage,Is.Zero);Assert.That(sim.Player.Velocity.y,Is.GreaterThan(0));
        }
        [Test] public void NaturalContinuationWaitsForMoveEndWithoutRequiringHitConfirm()
        {
            var sim=Make();sim.Player.Position=new Vector2(-7,0);
            sim.Step(Frame(ActionId.Basic));int serial=sim.Player.MoveSerial;
            sim.Step(Frame(ActionId.Basic));
            Until(sim,()=>sim.Player.MoveSerial>serial);
            Assert.That(sim.Dummy.Damage,Is.Zero);Assert.That(sim.Player.MoveId,Is.EqualTo("authored_tool"));
        }
        [TestCase(ActionId.Basic)]
        [TestCase(ActionId.Rag)]
        [TestCase(ActionId.Trick)]
        public void AnyAuthoredCombatActionCanChooseTapOrHoldVariants(ActionId action)
        {
            var sim=Make();var charged=Attack();charged.Id="authored_charged";sim.Catalog.Moves.Add(charged);
            var binding=sim.Catalog.Bindings[0];binding.Action=action;binding.ChargedMoveId=charged.Id;binding.ChargeFrames=3;
            var press=Frame(action);press.HeldActions.Add(action);sim.Step(press);Assert.That(sim.IsCharging,Is.True);
            sim.Step(Frame());Assert.That(sim.Player.MoveId,Is.EqualTo("authored_tool"),"Release chooses the tap tool");
            sim.Reset();press=Frame(action);press.HeldActions.Add(action);sim.Step(press);
            for(int i=0;i<2;i++){var held=Frame();held.HeldActions.Add(action);sim.Step(held);}
            Assert.That(sim.Player.MoveId,Is.EqualTo("authored_charged"),"Holding chooses the authored charge tool");
        }
        [Test] public void AuthoredUpwardImpulseHasTheSameApexWithOrWithoutHoldingJump()
        {
            float Apex(bool jumpHeld)
            {
                var move=Attack();move.Impulse=new Vector2(0,12);var sim=Make(move);
                sim.Tuning.Gravity=38;sim.Player.Position=new Vector2(-7,0);
                var press=Frame(ActionId.Basic);press.JumpHeld=jumpHeld;sim.Step(press);float apex=sim.Player.Position.y;
                for(int i=0;i<60;i++){var input=Frame();input.JumpHeld=jumpHeld;sim.Step(input);apex=Mathf.Max(apex,sim.Player.Position.y);}
                return apex;
            }
            float released=Apex(false),held=Apex(true);
            Assert.That(released,Is.GreaterThan(1.5f));
            Assert.That(held,Is.EqualTo(released).Within(.001f),"Short-hop control belongs to real jumps, not authored attack impulses");
        }
        [Test] public void BlinkAppliesItsDisplacementAndIntegratesVelocityOnlyOnce()
        {
            var move=Attack();move.BlinkToTarget=true;move.BlinkRange=20;move.Impulse=new Vector2(6,0);
            var sim=Make(move);sim.Player.Position=new Vector2(-7,0);sim.Step(Frame(ActionId.Basic));
            Assert.That(sim.Player.Position.x,Is.EqualTo(-1.1f).Within(.001f));
            Assert.That(sim.Player.Velocity.x,Is.EqualTo(6).Within(.001f));Assert.That(sim.Dummy.Damage,Is.Zero);
        }
        [Test] public void DisabledCancelMaskNeverAdvertisesACancelAfterAHit()
        {
            var move=Attack();move.Cancels=CancelActions.None;move.CancelOpen=0;move.CancelClose=move.Total-1;
            var sim=Make(move);Strike(sim);
            Assert.That(sim.Player.MoveHit,Is.True);Assert.That(sim.CanCancel,Is.False);
        }
        [Test] public void FinisherIsAuthoringIntentRatherThanAHiddenComboLockout()
        {
            var move=Attack();move.Finisher=true;var sim=Make(move);
            Strike(sim);Finish(sim);Strike(sim);
            Assert.That(sim.ComboHits,Is.EqualTo(2),"A finisher closes through its authored physics, not an arbitrary combo prohibition");
        }
        [TestCase(false,2)]
        [TestCase(true,1)]
        public void AirborneVulnerabilityDependsOnWhetherThatEnemyCanActInAir(bool canActInAir,int expectedHits)
        {
            var profile=Own(ScriptableObject.CreateInstance<EnemyProfile>());profile.CanActInAir=canActInAir;
            var move=Attack();move.Hitboxes[0].Hit.Hitstun=0;var sim=Make(move,profile);
            sim.Player.Position=new Vector2(-1,3);sim.Player.Grounded=false;sim.Dummy.Position=new Vector2(0,3);sim.Dummy.Grounded=false;
            Strike(sim);Finish(sim);Idle(sim,8);Strike(sim);
            Assert.That(sim.ComboHits,Is.EqualTo(expectedHits));
        }
    }
}
