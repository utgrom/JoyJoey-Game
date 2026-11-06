using System;
using System.Collections.Generic;
using UnityEngine;

namespace JoyJoey.Sandbox
{
    public enum ActionId { Jump, Basic, Rag, Trick, Dash, Transform, PreviousSuit, NextSuit, Reset, ToggleDebug, ToggleHelp, TogglePause, Step, ToggleDummy, ToggleSlow, ToggleDamage, ToggleEnemy }
    public enum InputEdge { Press, Release }
    [Flags] public enum CancelActions { None = 0, Basic = 1, Rag = 2, Trick = 4, Jump = 8, Dash = 16, Transform = 32, All = 63 }
    public enum RagStyle { Strongman, Jester, Illusionist }
    public enum DummyMode { Stand, Escape, Jump }
    public enum VelocityMode { Override, Add }
    public enum MoveContext { Any, Ground, Air }
    public enum MoveDirection { Any, Neutral, Horizontal, Up, Down }
    public enum MoveStance { Any, Normal, Transformed }

    public struct InputCommand
    {
        public ActionId Action;
        public InputEdge Edge;
        public Vector2 Direction;
        public double Time;
        public int TapCount;
        public InputCommand(ActionId action, InputEdge edge, Vector2 direction, double time = 0, int taps = 1)
        { Action = action; Edge = edge; Direction = direction; Time = time; TapCount = taps; }
    }

    public sealed class InputFrame
    {
        public Vector2 Move;
        public bool JumpHeld, BasicHeld, RagHeld;
        public float BasicHeldSeconds, RagHeldSeconds;
        public string Device = "TECLADO";
        public readonly List<InputCommand> Commands = new List<InputCommand>();
        public readonly HashSet<ActionId> HeldActions = new HashSet<ActionId>();
        public bool IsHeld(ActionId action) => HeldActions.Contains(action) || action==ActionId.Basic && BasicHeld ||
            action==ActionId.Rag && RagHeld || action==ActionId.Jump && JumpHeld;
    }

    [Serializable]
    public sealed class MoveDefinition
    {
        public string Id, Title;
        public ActionId Action;
        [Min(0)] public int Startup = 5, Active = 3, Recovery;
        public Vector2 HitboxOffset = new Vector2(1, 1);
        public Vector2 HitboxSize = new Vector2(2, 1.6f);
        public float Damage = 6;
        public Vector2 Launch = new Vector2(3, 4);
        public int Hitstun = 24, Hitstop = 4, Rehit = 0;
        public int CancelOpen = 7, CancelClose = 18;
        public bool CancelRequiresHit = true;
        public CancelActions Cancels = CancelActions.None;
        public Vector2 Impulse;
        [Range(0,1)] public float MomentumRetention = .2f;
        [Range(0,1)] public float VerticalMomentumRetention = .2f;
        public VelocityMode StartImpulseMode = VelocityMode.Add;
        [Min(.05f)] public float GravityScale = 1;
        public bool Finisher;
        public bool FollowOnHold;
        public int FollowFrame = 6;
        public Vector2 FollowImpulse = new Vector2(0, 13);
        public bool BlinkToTarget;
        [Min(0)] public float BlinkRange = 4;
        public AnimationClip AnimationClip;
        public GameObject PreviewPrefab;
        [Min(.01f)] public float AnimationSpeed = 1;
        public List<HitboxTrack> Hitboxes = new List<HitboxTrack>();
        public List<PoseKeyframe> Poses = new List<PoseKeyframe>();
        public int Total => Mathf.Max(1, Startup + Active + Recovery);
        public bool IsActive(int tick)
        {
            if(tick<Startup || tick>=Startup+Active)return false;
            if (Hitboxes != null && Hitboxes.Count > 0) return Hitboxes.Exists(h => h != null && h.IsActive(tick));
            return true;
        }
        public PoseKeyframe SamplePose(float tick) => AttackSampling.Pose(Poses, tick);
    }

    [Serializable]
    public sealed class HitProperties
    {
        public float Damage = 6;
        public Vector2 EnemyImpulse = new Vector2(2, 5);
        public VelocityMode EnemyVelocityMode = VelocityMode.Override;
        public Vector2 AttackerImpulse;
        public VelocityMode AttackerVelocityMode = VelocityMode.Add;
        [Min(0)] public int Hitstun = 15, Hitstop = 3, Rehit;
        [Min(0)] public float JugglePower = 10, ArmorDamage = 10;
        public bool Correction = true;
        [Min(0)] public float PreferredDistance = 1.15f, MaxCorrection = .45f;
        [Min(.01f)] public float CorrectionSpeed = 9;
        [Range(0,1)] public float PlayerCorrectionShare = .3f;
    }
    [Serializable]
    public sealed class HitboxKeyframe
    {
        [Min(0)] public int Frame;
        public Vector2 Offset = new Vector2(1, .9f), Size = new Vector2(2, 1.8f);
    }
    [Serializable]
    public sealed class HitboxTrack
    {
        public string Name = "Hitbox";
        [Min(0)] public int StartFrame = 5, EndFrame = 8;
        public int HitGroup;
        public HitProperties Hit = new HitProperties();
        public List<HitboxKeyframe> Keys = new List<HitboxKeyframe>();
        public bool IsActive(int frame) => frame >= StartFrame && frame < EndFrame;
        public Rect Sample(float frame) => AttackSampling.Box(Keys, frame);
    }
    [Serializable]
    public sealed class PoseKeyframe
    {
        public int Frame;
        public Vector2 BodyOffset;
        public float BodyAngle, FrontArmAngle, RearArmAngle, FrontLegAngle, RearLegAngle;
    }
    [Serializable]
    public sealed class MoveBinding
    {
        public string Name = "New binding";
        public ActionId Action = ActionId.Basic;
        public MoveContext Context;
        public MoveDirection Direction;
        public MoveStance Stance;
        public bool AnyRag = true;
        public RagStyle Rag;
        public int Priority;
        [Min(1)] public int SequenceTimeout = 30;
        public List<string> Sequence = new List<string>();
        public string ChargedMoveId;
        [Min(1)] public int ChargeFrames = 18;
    }

    [Serializable]
    public struct ArenaPlatform
    {
        public Rect Bounds;
        public bool OneWay;
        public ArenaPlatform(float x, float y, float width, float height, bool oneWay = false)
        { Bounds = new Rect(x, y, width, height); OneWay = oneWay; }
    }

    public sealed class FighterState
    {
        public Vector2 Position, Velocity;
        public int Facing = 1;
        public bool Grounded;
        public float Damage;
        public int Hitstun, Coyote, AirJumps = 1, AirDashes = 1, DashTicks, DashCooldown, DropTicks;
        public int WallSide;
        public MoveDefinition Move;
        public int MoveTick;
        public bool MoveHit;
        public int LastHitTick = -9999;
        public int MoveSerial;
        public float Armor, JuggleMeter;
        public int ArmorRegenDelay, JuggleExpiry;
        public Vector2 CorrectionRemaining;
        public float CorrectionSpeed;
        public string MoveId => Move == null ? "idle" : Move.Id;
        public string State => Hitstun > 0 ? "HITSTUN" : DashTicks > 0 ? "DASH" : Move != null ? (MoveTick < Move.Startup ? "STARTUP" : MoveTick < Move.Startup+Move.Active ? "ACTIVE" : "RECOVERY") : Grounded ? "GROUND" : "AIR";
    }

    public struct CombatEvent
    {
        public string Kind, Text;
        public Vector2 Position;
        public CombatEvent(string kind, string text, Vector2 position) { Kind = kind; Text = text; Position = position; }
    }
}
