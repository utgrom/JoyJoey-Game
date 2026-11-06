using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace JoyJoey.Sandbox.Tests
{
    public sealed class CombatSimulationTests
    {
        CombatSimulation sim;
        [SetUp] public void Setup() { sim = new CombatSimulation(); }
        [TearDown] public void Cleanup() { UnityEngine.Object.DestroyImmediate(sim.Catalog); UnityEngine.Object.DestroyImmediate(sim.Tuning); UnityEngine.Object.DestroyImmediate(sim.TargetProfile); }
        InputFrame Input(ActionId? press = null, Vector2? direction = null, bool jump = false, bool basic = false, bool rag = false)
        {
            var frame = new InputFrame { Move = direction ?? Vector2.zero, JumpHeld = jump, BasicHeld = basic, RagHeld = rag };
            if (press.HasValue) frame.Commands.Add(new InputCommand(press.Value, InputEdge.Press, frame.Move));
            return frame;
        }
        void Idle(int ticks, bool jump = false, bool basic = false, bool rag = false)
        { for (int i = 0; i < ticks; i++) sim.Step(Input(jump: jump, basic: basic, rag: rag)); }
        void CloseRange() { sim.Player.Position = new Vector2(-1.25f, 0); sim.Player.Velocity = Vector2.zero; }
        void WaitFor(Func<bool> condition, int limit = 120, bool basic = false)
        { for (int i = 0; i < limit && !condition(); i++) sim.Step(Input(basic: basic)); Assert.That(condition(), Is.True, "Condition timed out; action=" + sim.LastAction); }

        [Test] public void CatalogHasUniqueMovesAndValidWindows()
        {
            Assert.That(sim.Catalog.Moves.Count, Is.GreaterThanOrEqualTo(21));
            Assert.That(sim.Catalog.Moves.Select(m => m.Id).Distinct().Count(), Is.EqualTo(sim.Catalog.Moves.Count));
            foreach (var m in sim.Catalog.Moves)
            {
                Assert.That(m.Startup, Is.GreaterThanOrEqualTo(0));Assert.That(m.Recovery,Is.GreaterThanOrEqualTo(0));
                if(m.Cancels!=CancelActions.None){Assert.That(m.CancelClose, Is.LessThan(m.Total), m.Id);Assert.That(m.CancelOpen, Is.LessThanOrEqualTo(m.CancelClose));}
                foreach(var h in m.Hitboxes){Assert.That(h.StartFrame,Is.GreaterThanOrEqualTo(0));Assert.That(h.EndFrame,Is.LessThanOrEqualTo(m.Total),m.Id);Assert.That(h.EndFrame,Is.GreaterThan(h.StartFrame));}
            }
        }
        [Test] public void VariableJumpProducesDistinctApexes()
        {
            sim.Step(Input(ActionId.Jump, jump:true)); float high=0;
            for (int i=0;i<60;i++) { sim.Step(Input(jump:true)); high=Mathf.Max(high,sim.Player.Position.y); }
            sim.Reset(); sim.Step(Input(ActionId.Jump)); float low=0;
            for(int i=0;i<60;i++){ sim.Step(Input()); low=Mathf.Max(low,sim.Player.Position.y); }
            Assert.That(high, Is.GreaterThan(low + 1));
        }
        [Test] public void AirDashIsLimitedAndRestoredOnLanding()
        {
            sim.Step(Input(ActionId.Jump,jump:true)); sim.Player.Position = new Vector2(-11, 7);
            sim.Step(Input(ActionId.Dash,Vector2.right,jump:true));
            Assert.That(sim.Player.AirDashes, Is.Zero);
            Idle(25,jump:true); sim.Step(Input(ActionId.Dash,Vector2.right,jump:true));
            Assert.That(sim.Player.DashTicks, Is.Zero);
            WaitFor(()=>sim.Player.Grounded,180); Assert.That(sim.Player.AirDashes,Is.EqualTo(1));
        }
        [Test] public void DefaultAttackReducesAirDashMomentumByItsConfiguredRetention()
        {
            sim.Step(Input(ActionId.Jump,jump:true)); sim.Step(Input(ActionId.Dash,Vector2.right,jump:true)); Idle(9,jump:true);
            sim.Step(Input(ActionId.Basic,jump:true));
            Assert.That(sim.Player.Velocity.x,Is.LessThan(6));Assert.That(sim.Player.Velocity.x,Is.GreaterThan(0)); Assert.That(sim.Player.Move,Is.Not.Null);
        }
        [Test] public void SingleHitMoveCannotHitRepeatedlyDuringItsActiveWindow()
        {
            CloseRange(); sim.Step(Input(ActionId.Basic)); Idle(25);
            Assert.That(sim.Dummy.Damage,Is.EqualTo(5)); Assert.That(sim.ComboHits,Is.EqualTo(1));
        }
        [Test] public void BufferedJabsContinueNaturallyAfterThePreviousAttackEnds()
        {
            CloseRange(); sim.Step(Input(ActionId.Basic)); WaitFor(()=>sim.Player.MoveHit);
            sim.Step(Input(ActionId.Basic)); WaitFor(()=>sim.Player.MoveId=="jab2");
            WaitFor(()=>sim.Dummy.Damage>=10);
            Assert.That(sim.ComboHits,Is.EqualTo(2)); Assert.That(sim.LastCancel,Does.Not.Contain("BASIC"));
        }
        [Test] public void RunningApproachLinksJabsLauncherAndAirAttack()
        {
            while(sim.Player.Position.x < -1.25f) sim.Step(Input(direction:Vector2.right));
            Idle(2);sim.Step(Input(ActionId.Basic));WaitFor(()=>sim.Hitstop>0);
            Idle(1);sim.Step(Input(ActionId.Basic));WaitFor(()=>sim.ComboHits>=2);
            Idle(1);sim.Step(Input(ActionId.Basic,Vector2.down,basic:true));
            WaitFor(()=>sim.Player.MoveId=="launcher" && sim.Player.MoveHit,basic:true);
            Idle(8,basic:true);Idle(1);sim.Step(Input(ActionId.Basic));WaitFor(()=>sim.ComboHits>=4);
            Assert.That(sim.BestCombo,Is.GreaterThanOrEqualTo(4));
        }
        [Test] public void WhiffWaitsForTheAuthoredMoveToFinish()
        {
            sim.Catalog.Find("jab1").Recovery=10;sim.Catalog.Find("jab1").Cancels=CancelActions.None;
            sim.Step(Input(ActionId.Basic)); Idle(2); sim.Step(Input(ActionId.Dash,Vector2.right));
            Assert.That(sim.Player.MoveId,Is.EqualTo("jab1")); Assert.That(sim.Player.DashTicks,Is.Zero);
        }
        [Test] public void BufferedCommandsSurviveHitstop()
        {
            CloseRange(); sim.Step(Input(ActionId.Basic)); WaitFor(()=>sim.Hitstop>0);
            int moveTick=sim.Player.MoveTick; sim.Step(Input(ActionId.Basic));
            Assert.That(sim.Player.MoveTick,Is.EqualTo(moveTick)); Assert.That(sim.BufferedInputs,Is.EqualTo(1));
            WaitFor(()=>sim.Player.MoveId=="jab2");
        }
        [Test] public void LauncherHoldFollowsAndLinksIntoAirAttack()
        {
            CloseRange(); sim.Step(Input(ActionId.Basic,Vector2.down,basic:true));
            WaitFor(()=>sim.Player.MoveHit,basic:true); Idle(7,basic:true);
            Assert.That(sim.Dummy.Position.y,Is.GreaterThan(.2f)); Assert.That(sim.Player.Velocity.y,Is.GreaterThan(0));
            sim.Step(Input(ActionId.Basic,basic:true)); WaitFor(()=>sim.Player.MoveId=="air1");
            WaitFor(()=>sim.ComboHits>=2);
        }
        [Test] public void DefaultPopLaunchHeldFollowsEvenWhenThereIsNoTargetInRange()
        {
            sim.Player.Position=new Vector2(-10,0);
            sim.Step(Input(ActionId.Basic,Vector2.down,basic:true));
            WaitFor(()=>sim.Player.Velocity.y>2,basic:true);
            Assert.That(sim.Dummy.Damage,Is.Zero);Assert.That(sim.Player.Grounded,Is.False);
        }
        [Test] public void JesterMultihitProducesTrueCombo()
        {
            CloseRange(); sim.Step(Input(ActionId.NextSuit)); sim.Step(Input(ActionId.Rag)); Idle(50);
            Assert.That(sim.Dummy.Damage,Is.GreaterThan(0)); Assert.That(sim.BestCombo,Is.GreaterThanOrEqualTo(2));
        }
        [Test] public void RagTapAndHoldSelectDifferentMoves()
        {
            sim.Step(Input(ActionId.Rag)); Assert.That(sim.Player.MoveId,Is.EqualTo("ragStrong"));
            sim.Reset(); sim.Step(Input(ActionId.Rag,rag:true)); Idle(17,rag:true);
            Assert.That(sim.Player.MoveId,Is.EqualTo("ragStrongCharged"));
        }
        [Test] public void PausingCancelsAChargeWithoutFiringItsReleaseMove()
        {
            sim.Step(Input(ActionId.Rag, rag:true)); Idle(5, rag:true);
            Assert.That(sim.IsCharging, Is.True);
            int tick = sim.Tick;
            sim.CancelActionsForPause();
            Assert.That(sim.IsCharging, Is.False);
            Assert.That(sim.ChargeTicks, Is.Zero);
            Assert.That(sim.Tick, Is.EqualTo(tick));
            Idle(25);
            Assert.That(sim.Player.Move, Is.Null);
            Assert.That(sim.Dummy.Damage, Is.Zero);
            sim.Step(Input(ActionId.Rag, rag:true));
            Assert.That(sim.IsCharging, Is.True, "A fresh Press can start a new charge.");
        }
        [Test] public void PauseDropsBufferedIntentWhilePreservingAnAlreadyExecutingMove()
        {
            CloseRange(); sim.Step(Input(ActionId.Basic)); WaitFor(()=>sim.Hitstop>0);
            sim.Step(Input(ActionId.Rag, rag:true));
            Assert.That(sim.BufferedInputs, Is.GreaterThan(0));
            var move = sim.Player.Move;
            var position = sim.Player.Position;
            var velocity = sim.Player.Velocity;
            int moveTick = sim.Player.MoveTick;
            sim.CancelActionsForPause();
            Assert.That(sim.BufferedInputs, Is.Zero);
            Assert.That(sim.Player.Move, Is.SameAs(move));
            Assert.That(sim.Player.MoveTick, Is.EqualTo(moveTick));
            Assert.That(sim.Player.Position, Is.EqualTo(position));
            Assert.That(sim.Player.Velocity, Is.EqualTo(velocity));
            Idle(60);
            Assert.That(sim.IsCharging, Is.False);
        }
        [Test] public void SuitCycleWrapsAndLocksDuringTransformation()
        {
            sim.Step(Input(ActionId.PreviousSuit)); Assert.That(sim.SelectedRag,Is.EqualTo(RagStyle.Illusionist));
            sim.Step(Input(ActionId.Transform)); Assert.That(sim.Transformed,Is.True);
            sim.Step(Input(ActionId.NextSuit)); Assert.That(sim.SelectedRag,Is.EqualTo(RagStyle.Illusionist));
            sim.Step(Input(ActionId.Basic)); Assert.That(sim.Player.MoveId,Is.EqualTo("suitIllusion"));
        }
        [Test] public void DummyRecoveryBreaksTrueCombo()
        {
            CloseRange(); sim.Step(Input(ActionId.Basic)); Idle(60);
            CloseRange(); sim.Step(Input(ActionId.Basic)); WaitFor(()=>sim.Dummy.Damage>=10);
            Assert.That(sim.ComboHits,Is.EqualTo(1));
        }
        [Test] public void OneWayPlatformsLandAndDropWithoutTunnelling()
        {
            sim.Player.Position=new Vector2(5,4);sim.Player.Velocity=new Vector2(0,-22);sim.Player.Grounded=false;
            WaitFor(()=>sim.Player.Grounded); Assert.That(sim.Player.Position.y,Is.EqualTo(2.7f).Within(.01f));
            sim.Step(Input(ActionId.Jump,Vector2.down)); Idle(35);
            Assert.That(sim.Player.Position.y,Is.LessThan(2.4f));
        }
        [Test] public void ResetRestoresResourcesButKeepsBestCombo()
        {
            CloseRange();sim.Step(Input(ActionId.NextSuit));sim.Step(Input(ActionId.Rag));Idle(50);int best=sim.BestCombo;
            sim.Reset(); Assert.That(sim.Player.Position,Is.EqualTo(new Vector2(-3,0)));
            Assert.That(sim.Fun,Is.EqualTo(50));Assert.That(sim.Dummy.Damage,Is.Zero);Assert.That(sim.BestCombo,Is.EqualTo(best));
        }
        [Test] public void DroppingThroughPlatformCancelsRagCharge()
        {
            sim.Player.Position=new Vector2(5,2.7f);sim.Player.Grounded=true;
            sim.Step(Input(ActionId.Rag,rag:true)); Assert.That(sim.IsCharging,Is.True);
            sim.Step(Input(ActionId.Jump,Vector2.down,rag:true));Idle(25,rag:true);
            Assert.That(sim.IsCharging,Is.False);Assert.That(sim.Player.Move,Is.Null);
        }
        [Test] public void RagChargeKeepsItsOriginalRagWhenSelectionChanges()
        {
            sim.Step(Input(ActionId.Rag,rag:true));sim.Step(Input(ActionId.NextSuit,rag:true));Idle(16,rag:true);
            Assert.That(sim.SelectedRag,Is.EqualTo(RagStyle.Jester));
            Assert.That(sim.Player.MoveId,Is.EqualTo("ragStrongCharged"));
        }
        [Test] public void ResetClearsLauncherFollowGravityState()
        {
            CloseRange();sim.Step(Input(ActionId.Basic,Vector2.down,basic:true));WaitFor(()=>sim.Player.MoveHit,basic:true);Idle(7,basic:true);
            sim.Reset();sim.Step(Input(ActionId.Jump));float apex=0;
            for(int i=0;i<60;i++){sim.Step(Input());apex=Mathf.Max(apex,sim.Player.Position.y);}
            Assert.That(apex,Is.LessThan(1.2f));
        }
    }
}
