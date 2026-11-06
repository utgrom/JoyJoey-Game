using System;
using System.Collections.Generic;
using UnityEngine;

namespace JoyJoey.Sandbox
{
    /// <summary>60 Hz combat model, independent of rendering, physics timing and input devices.
    /// Positions are feet-centres. Geometry is axis-aligned; OneWay platforms only block falling bodies.</summary>
    public sealed class CombatSimulation
    {
        public const float Delta = 1f / 60f;
        public readonly FighterState Player = new FighterState();
        public readonly FighterState Dummy = new FighterState();
        public readonly List<ArenaPlatform> Platforms = new List<ArenaPlatform>();
        public readonly List<CombatEvent> Events = new List<CombatEvent>();
        public CombatCatalog Catalog { get; }
        public MotorTuning Tuning { get; }
        public CombatWorld World { get; }
        public bool TargetEnabled => World.TargetEnabled;
        public EnemyProfile TargetProfile { get; private set; }
        public bool DummyCanAct => TargetEnabled && Dummy.Hitstun <= 0 && (Dummy.Grounded || TargetProfile.CanActInAir);
        public bool DummyVulnerable => TargetEnabled && !DummyCanAct;
        public RagStyle SelectedRag { get; private set; }
        public bool Transformed { get; private set; }
        public float Fun { get; private set; }
        public int ComboHits { get; private set; }
        public float ComboDamage { get; private set; }
        public int BestCombo { get; private set; }
        public string StyleRank => _style >= 90 ? "SSS" : _style >= 65 ? "SS" : _style >= 42 ? "S" : _style >= 25 ? "A" : _style >= 12 ? "B" : "C";
        public string LastAction { get; private set; } = "LISTO";
        public string LastCancel { get; private set; } = "—";
        public int Tick { get; private set; }
        public int Hitstop { get; private set; }
        public int ChargeTicks { get; private set; }
        public bool IsCharging { get; private set; }
        public DummyMode DummyBehavior { get; private set; }
        public bool DamageScaling { get; set; }
        public bool Invulnerable => Player.DashTicks > Tuning.DashDuration - 6;
        public Rect PlayerHurtbox => Hurtbox(Player);
        public Rect DummyHurtbox => Hurtbox(Dummy);
        readonly List<Rect> _activeHitboxes = new List<Rect>();
        public List<Rect> ActiveHitboxes
        {
            get
            {
                _activeHitboxes.Clear(); var move=Player.Move;if(move==null || !move.IsActive(Player.MoveTick))return _activeHitboxes;
                if(move.Hitboxes != null && move.Hitboxes.Count>0)
                { foreach(var box in move.Hitboxes) if(box != null && box.IsActive(Player.MoveTick)) _activeHitboxes.Add(WorldHitbox(box.Sample(Player.MoveTick))); }
                else if(Catalog.SchemaVersion<2 && move.IsActive(Player.MoveTick) && move.Damage>0) _activeHitboxes.Add(Hitbox(Player));
                return _activeHitboxes;
            }
        }
        public Rect ActiveHitbox { get { var boxes=ActiveHitboxes;return boxes.Count>0?boxes[0]:new Rect(); } }
        public bool CanCancel => Player.Move != null && Player.Move.Cancels!=CancelActions.None && CancelWindow(Player.Move) && (!Player.Move.CancelRequiresHit || Player.MoveHit);
        public int BufferedInputs => _buffer.Count;
        public int LastComboGap { get; private set; }

        sealed class Buffered { public InputCommand Command; public int Life; }
        readonly List<Buffered> _buffer = new List<Buffered>();
        readonly Queue<string> _variety = new Queue<string>();
        Vector2 _chargeDirection;
        RagStyle _chargeRag;
        float _style;
        bool _chainOpen;
        int _comboIdle, _dummyIdle, _jabChain, _chainGrace, _trickCooldown, _gapTicks;
        bool _launcherFollowed;
        bool _variableJump;
        int _lastHitSerial = -1, _lastHitMoveTick = -999;
        InputFrame _input = new InputFrame();
        MoveBinding _lastBinding, _chargeBinding;
        ActionId _chargeAction=ActionId.Rag, _executingAction=ActionId.Basic;
        string _chargeMoveId;
        int _sequenceStep, _sequenceLife;
        readonly Dictionary<int,int> _hitGroups = new Dictionary<int,int>();
        float _correctionFacing=1, _correctionDistance=1.15f;

        public CombatSimulation(CombatCatalog catalog = null, MotorTuning tuning = null, EnemyProfile profile = null, CombatWorld world = null)
        {
            Catalog = catalog != null ? catalog : CombatDefaults.CreateCatalog();
            Tuning = tuning != null ? tuning : CombatDefaults.CreateTuning();
            TargetProfile = profile != null ? profile : ScriptableObject.CreateInstance<EnemyProfile>();
            World = world ?? CombatWorld.CreateSandbox();
            Platforms.AddRange(World.Platforms);
            Reset();
        }

        public void Reset()
        {
            ResetFighter(Player, World.PlayerSpawn);
            ResetFighter(Dummy, World.DummySpawn);
            Dummy.Facing = -1;
            Fun = 50; Transformed = false; Hitstop = 0; IsCharging = false; ChargeTicks = 0;
            ComboHits = 0; ComboDamage = 0; _chainOpen = false; _comboIdle = 0;
            _style = 0; _variety.Clear(); _buffer.Clear(); _jabChain = 0; _chainGrace = 0;
            _dummyIdle = 0; _trickCooldown = 0; _lastHitSerial = -1; _gapTicks = 0;
            _launcherFollowed = false;
            _variableJump=false;
            Dummy.Armor=TargetProfile.GroundArmor;
            _lastBinding=null;_chargeBinding=null;_sequenceStep=0;_sequenceLife=0;_hitGroups.Clear();
            LastCancel = "—"; LastAction = TargetEnabled ? "LISTO · ACÉRCATE AL DUMMY" : "LISTO";
            Events.Clear(); Emit("reset", TargetEnabled ? "ENTRENAMIENTO REINICIADO" : "NIVEL REINICIADO", Player.Position);
        }

        static void ResetFighter(FighterState f, Vector2 position)
        {
            f.Position = position; f.Velocity = Vector2.zero; f.Facing = 1; f.Grounded = true;
            f.Damage = 0; f.Hitstun = 0; f.Coyote = 6; f.AirJumps = 1; f.AirDashes = 1;
            f.DashTicks = 0; f.DashCooldown = 0; f.DropTicks = 0; f.WallSide = 0;
            f.Move = null; f.MoveTick = 0; f.MoveHit = false; f.LastHitTick = -9999;
            f.MoveSerial++;
            f.Armor=0;f.JuggleMeter=0;f.ArmorRegenDelay=0;f.JuggleExpiry=0;f.CorrectionRemaining=Vector2.zero;f.CorrectionSpeed=0;
        }

        public void SetDummyMode(DummyMode mode) { DummyBehavior = mode; Emit("setting", "DUMMY: " + mode, Dummy.Position); }
        /// <summary>Invalidate pending action intent without a Release that could execute a charged move.</summary>
        public void CancelActionsForPause()
        {
            _buffer.Clear();
            _input = new InputFrame();
            bool charging = IsCharging;
            IsCharging = false;
            ChargeTicks = 0;
            _chargeBinding = null;
            _chargeMoveId = null;
            _chargeDirection = Vector2.zero;
            if (charging) LastAction = "CARGA CANCELADA · PAUSA";
        }
        public void SetEnemyProfile(EnemyProfile profile)
        {
            if(profile==null) return;
            TargetProfile=profile;ResetDummy();
            Player.CorrectionRemaining=Vector2.zero;
            Emit("setting","PERFIL: "+profile.DisplayName,Dummy.Position);
        }

        public void Step(InputFrame input)
        {
            _input = input ?? new InputFrame();
            Events.Clear(); Tick++;
            foreach (var command in _input.Commands)
            {
                if (command.Edge != InputEdge.Press) continue;
                if (command.Action == ActionId.Reset) { Reset(); return; }
                if (command.Action == ActionId.PreviousSuit || command.Action == ActionId.NextSuit)
                {
                    if (!Transformed)
                    {
                        SelectedRag = (RagStyle)(((int)SelectedRag + (command.Action == ActionId.NextSuit ? 1 : 2)) % 3);
                        Emit("suit", SelectedRag.ToString(), Player.Position);
                    }
                    else Emit("setting", "SAL DEL TRAJE PARA CAMBIAR RAG", Player.Position);
                    continue;
                }
                if ((int)command.Action <= (int)ActionId.Transform)
                    _buffer.Add(new Buffered { Command = command, Life = Tuning.InputBuffer });
            }
            // Commands survive hitstop; neither cancel windows nor buffer lifetime advance while frozen.
            if (Hitstop > 0) { Hitstop--; return; }

            if (Transformed)
            {
                Fun = Mathf.Max(0, Fun - 5 * Delta);
                if (Fun <= 0) { Transformed = false; Emit("transform", "FUN AGOTADO", Player.Position); }
            }
            _style = Mathf.Max(0, _style - (3 + _style * .035f) * Delta);
            if (_trickCooldown > 0) _trickCooldown--;
            if(_sequenceLife>0) _sequenceLife--;
            if (_chainGrace > 0) _chainGrace--; else if (Player.Move == null) _jabChain = 0;
            if (Player.DashCooldown > 0) Player.DashCooldown--;
            if (Player.DropTicks > 0) Player.DropTicks--;
            if (TargetEnabled)
            {
                if (Dummy.Hitstun > 0) Dummy.Hitstun--;
                if(Dummy.JuggleExpiry>0) { if(--Dummy.JuggleExpiry==0) Dummy.JuggleMeter=0; }
                if(Dummy.ArmorRegenDelay>0) Dummy.ArmorRegenDelay--;
                else if(Dummy.Grounded) Dummy.Armor=Mathf.Min(TargetProfile.GroundArmor,Dummy.Armor+TargetProfile.ArmorRegenPerSecond*Delta);
                if (DummyCanAct)
                {
                    _chainOpen = false; _gapTicks++;
                    if (++_comboIdle > 150) { ComboHits = 0; ComboDamage = 0; }
                }
                else { _gapTicks = 0; _comboIdle = 0; }
            }

            AdvanceMove();
            ProcessBuffered();
            if (IsCharging)
            {
                ChargeTicks++;
                int chargeLimit=_chargeBinding != null?Mathf.Max(1,_chargeBinding.ChargeFrames):18;
                if (!_input.IsHeld(_chargeAction) || ChargeTicks >= chargeLimit)
                {
                    bool charged = ChargeTicks >= chargeLimit;
                    IsCharging = false;
                    string chosen=_chargeBinding != null?(charged?_chargeBinding.ChargedMoveId:_chargeMoveId):RagMove(charged);
                    BeginMove(chosen, _chargeDirection, _chargeAction.ToString().ToUpperInvariant(),_chargeAction);
                }
            }
            ApplyHoldFollow();
            MovePlayer();
            if (TargetEnabled)
            {
                MoveDummy();
                DetectHit();
            }
            for (int i = _buffer.Count - 1; i >= 0; i--) if (--_buffer[i].Life <= 0) _buffer.RemoveAt(i);
            if (World.IsOutside(Player.Position))
            {
                ResetFighter(Player, World.PlayerSpawn);
                // A level respawn must not carry a buffered attack or a held charge into its new position.
                if (!TargetEnabled)
                {
                    _buffer.Clear(); IsCharging = false; ChargeTicks = 0;
                    _variableJump = false; _launcherFollowed = false;
                    _lastBinding = null; _chargeBinding = null; _sequenceLife = 0; _sequenceStep = 0;
                    _jabChain = 0; _chainGrace = 0; _chargeMoveId = null;
                    _trickCooldown = 0; _hitGroups.Clear();
                    Emit("respawn", "JOEY RECOLOCADO", Player.Position);
                }
            }
            if (TargetEnabled && World.IsOutside(Dummy.Position)) ResetDummy();
        }

        void ProcessBuffered()
        {
            // Oldest executable command wins, preserving buffered chains without replaying held buttons.
            for (int i = 0; i < _buffer.Count; i++)
            {
                var cmd = _buffer[i].Command;
                if (!CanPerform(cmd.Action)) continue;
                if (!Perform(cmd)) continue;
                _buffer.RemoveAt(i);
                break;
            }
        }

        bool CanPerform(ActionId action)
        {
            if (Player.Hitstun > 0 || Player.DashTicks > 3) return false;
            if (IsCharging) return action == ActionId.Dash || action == ActionId.Jump;
            if (Player.Move == null) return true;
            if (!CancelWindow(Player.Move) || (Player.Move.CancelRequiresHit && !Player.MoveHit)) return false;
            return (Player.Move.Cancels & Mask(action)) != 0;
        }

        bool CancelWindow(MoveDefinition m) => Player.MoveTick >= m.CancelOpen && Player.MoveTick <= m.CancelClose;
        static CancelActions Mask(ActionId a)
        {
            switch (a) { case ActionId.Basic: return CancelActions.Basic; case ActionId.Rag: return CancelActions.Rag;
                case ActionId.Trick: return CancelActions.Trick; case ActionId.Jump: return CancelActions.Jump;
                case ActionId.Dash: return CancelActions.Dash; case ActionId.Transform: return CancelActions.Transform;
                default: return CancelActions.None; }
        }

        bool Perform(InputCommand cmd)
        {
            switch (cmd.Action)
            {
                case ActionId.Jump:
                    if (cmd.Direction.y < -.5f && Player.Grounded && OnOneWay())
                    {
                    CancelMove("DROP"); IsCharging = false; ChargeTicks = 0; Player.DropTicks = 18; Player.Grounded = false;
                        Player.Position += Vector2.down * .06f; Player.Velocity.y = -2;
                        return true;
                    }
                    if (!Player.Grounded && Player.Coyote <= 0 && Player.AirJumps <= 0 && Player.WallSide == 0) return false;
                    CancelMove("JUMP"); IsCharging = false;
                    if (Player.WallSide != 0 && !Player.Grounded)
                    { Player.Velocity.x = -Player.WallSide * 10; Player.Facing = -Player.WallSide; Emit("jump", "WALL JUMP", Player.Position); }
                    else if (!Player.Grounded && Player.Coyote <= 0) { Player.AirJumps--; Emit("jump", "DOBLE SALTO", Player.Position); }
                    else Emit("jump", "SALTO", Player.Position);
                    Player.Velocity.y = Tuning.JumpSpeed;
                    _variableJump=true;
                    // Quick press+release in the same render frame must still produce a short hop.
                    if (!_input.JumpHeld) Player.Velocity.y *= .6f;
                    Player.Grounded = false; Player.Coyote = 0;
                    return true;
                case ActionId.Dash:
                    if (Player.DashCooldown > 0 || (!Player.Grounded && Player.AirDashes <= 0)) return false;
                    CancelMove("DASH"); IsCharging = false;
                    if (!Player.Grounded) Player.AirDashes--;
                    var direction = cmd.Direction.sqrMagnitude > .16f ? StickDirections.SnapDash(cmd.Direction) : Vector2.zero;
                    if (direction == Vector2.zero) direction = new Vector2(Player.Facing, 0);
                    if (Player.Grounded && direction.y < 0) direction = new Vector2(Mathf.Sign(direction.x == 0 ? Player.Facing : direction.x), 0);
                    Player.Velocity = direction * Tuning.DashSpeed;
                    if (Mathf.Abs(direction.x) > .1f) Player.Facing = direction.x > 0 ? 1 : -1;
                    Player.DashTicks = Tuning.DashDuration; Player.DashCooldown = Tuning.DashCooldown;
                    _variableJump=false;
                    Emit("dash", "DASH", Player.Position); LastAction = "DASH";
                    return true;
                case ActionId.Transform:
                    if (!Transformed && Fun < 25) { Emit("setting", "NECESITAS 25 FUN", Player.Position); return true; }
                    CancelMove("TRANSFORM"); Transformed = !Transformed;
                    if (Transformed) Fun -= 8;
                    Emit("transform", Transformed ? "TRAJE: " + SelectedRag : "JOEY", Player.Position);
                    return true;
                case ActionId.Basic:
                    var basicBinding=ResolveBinding(cmd.Action,cmd.Direction);
                    if(basicBinding==null && Catalog.Bindings.Count>0) return false;
                    return PerformBoundMove(basicBinding,basicBinding==null?BasicMove(cmd.Direction):null,cmd);
                case ActionId.Rag:
                    var ragBinding=ResolveBinding(cmd.Action,cmd.Direction);
                    if(ragBinding==null && Catalog.Bindings.Count>0)return false;
                    if(ragBinding!=null)return PerformBoundMove(ragBinding,null,cmd);
                    CancelMove("RAG"); IsCharging = true; ChargeTicks = 0; _chargeDirection = cmd.Direction; _chargeRag = SelectedRag;
                    _chargeBinding=null;_chargeMoveId=null;_chargeAction=ActionId.Rag;
                    LastAction = "RAG · SUELTA / MANTÉN"; return true;
                case ActionId.Trick:
                    if (_trickCooldown > 0) return false;
                    var trickBinding=ResolveBinding(cmd.Action,cmd.Direction);
                    if(trickBinding==null && Catalog.Bindings.Count>0)return false;
                    _trickCooldown = 32;
                    string id = SelectedRag == RagStyle.Strongman ? "trickStrong" : SelectedRag == RagStyle.Jester ? "trickJester" : "trickIllusion";
                    return PerformBoundMove(trickBinding,id,cmd);
            }
            return false;
        }

        bool PerformBoundMove(MoveBinding binding,string fallback,InputCommand cmd)
        {
            string chosen=binding!=null?NextInSequence(binding):fallback;
            if(binding!=null && !string.IsNullOrEmpty(binding.ChargedMoveId))
            {
                CancelMove("CHARGE");IsCharging=true;ChargeTicks=0;_chargeDirection=cmd.Direction;
                _chargeBinding=binding;_chargeMoveId=chosen;_chargeAction=cmd.Action;_chargeRag=SelectedRag;
                LastAction=cmd.Action+" · SUELTA / MANTÉN";return true;
            }
            BeginMove(chosen,cmd.Direction,cmd.Action.ToString().ToUpperInvariant(),cmd.Action);return true;
        }

        MoveBinding ResolveBinding(ActionId action,Vector2 direction)
        {
            MoveDirection intent=direction.y<-.45f?MoveDirection.Down:direction.y>.6f?MoveDirection.Up:
                Mathf.Abs(direction.x)>.55f?MoveDirection.Horizontal:MoveDirection.Neutral;
            MoveBinding best=null;int bestRank=int.MinValue;
            foreach(var binding in Catalog.Bindings)
            {
                if(binding==null || binding.Action!=action || binding.Sequence==null || binding.Sequence.Count==0)continue;
                if(binding.Context!=MoveContext.Any && (binding.Context==MoveContext.Ground)!=Player.Grounded)continue;
                if(binding.Direction!=MoveDirection.Any && binding.Direction!=intent)continue;
                if(binding.Stance!=MoveStance.Any && (binding.Stance==MoveStance.Transformed)!=Transformed)continue;
                if(!binding.AnyRag && binding.Rag!=SelectedRag)continue;
                int score=binding.Priority*100+(binding.Context!=MoveContext.Any?8:0)+(binding.Direction!=MoveDirection.Any?4:0)+
                    (binding.Stance!=MoveStance.Any?2:0)+(!binding.AnyRag?1:0);
                if(score>=bestRank){best=binding;bestRank=score;}
            }
            return best;
        }

        string NextInSequence(MoveBinding binding)
        {
            if(_lastBinding!=binding || _sequenceLife<=0)_sequenceStep=0;
            string id=binding.Sequence[_sequenceStep%binding.Sequence.Count];
            _sequenceStep=(_sequenceStep+1)%binding.Sequence.Count;_sequenceLife=Mathf.Max(1,binding.SequenceTimeout);_lastBinding=binding;
            return id;
        }

        string RagMove(bool charged)
        {
            string root = _chargeRag == RagStyle.Strongman ? "ragStrong" : _chargeRag == RagStyle.Jester ? "ragJester" : "ragIllusion";
            return root + (charged ? "Charged" : "");
        }

        string BasicMove(Vector2 direction)
        {
            if (Transformed) return SelectedRag == RagStyle.Strongman ? "suitStrong" : SelectedRag == RagStyle.Jester ? "suitJester" : "suitIllusion";
            if (direction.y < -.45f || direction.y > .6f)
            {
                _jabChain = 0;
                return Player.Grounded || direction.y > 0 ? "launcher" : "dive";
            }
            if (Player.Grounded && Mathf.Abs(direction.x) > .55f) { _jabChain = 0; return "slash"; }
            _jabChain = (_jabChain % 3) + 1; _chainGrace = 35;
            return (Player.Grounded ? "jab" : "air") + _jabChain;
        }

        void BeginMove(string id, Vector2 direction, string cancel,ActionId pressedAction=ActionId.Basic)
        {
            var move = Catalog.Find(id);
            if (move == null) { Emit("error", "MOVIMIENTO SIN DATOS: " + id, Player.Position); return; }
            CancelMove(cancel);
            if (Mathf.Abs(direction.x) > .35f) Player.Facing = direction.x > 0 ? 1 : -1;
            Player.Move = move; Player.MoveTick = 0; Player.MoveHit = false; Player.MoveSerial++;
            _executingAction=pressedAction;
            _lastHitMoveTick = -999; _launcherFollowed = false; _variableJump=false; _hitGroups.Clear();
            Player.Velocity = new Vector2(Player.Velocity.x * Mathf.Clamp01(move.MomentumRetention),
                Player.Velocity.y * Mathf.Clamp01(move.VerticalMomentumRetention));
            ApplyVelocity(Player,move.Impulse,move.StartImpulseMode,Player.Facing);
            if (Player.Velocity.y > 0) Player.Grounded = false;
            if (move.BlinkToTarget && TargetEnabled)
            {
                Vector2 desired=Dummy.Position+new Vector2(-Player.Facing*1.2f,.05f);
                Vector2 offset=Vector2.ClampMagnitude(desired-Player.Position,Mathf.Max(0,move.BlinkRange));
                Motor(Player,offset,false);
            }
            LastAction = move.Title;
            Emit("move", move.Title, Player.Position);
        }

        void CancelMove(string into)
        {
            if (Player.Move != null)
            {
                LastCancel = Player.Move.Title + " → " + into;
                Emit("cancel", LastCancel, Player.Position + Vector2.up * 2);
            }
            Player.Move = null; Player.DashTicks = 0; _launcherFollowed = false;
        }

        void AdvanceMove()
        {
            if (Player.Move == null) return;
            Player.MoveTick++;
            if (Player.MoveTick >= Player.Move.Total) { Player.Move = null; _launcherFollowed = false; return; }
        }

        void ApplyHoldFollow()
        {
            var move=Player.Move;
            if(move==null)return;
            bool held=_input.IsHeld(_executingAction);
            if (move.FollowOnHold && Player.MoveTick>=move.FollowFrame && held && !_launcherFollowed)
            {
                ApplyVelocity(Player,move.FollowImpulse,VelocityMode.Add,Player.Facing);
                Player.Grounded = false; Player.Coyote = 0;
                _launcherFollowed = true;
                Emit("jump", "POP LAUNCH · FOLLOW", Player.Position);
            }
        }

        void MovePlayer()
        {
            if (Player.Grounded) { Player.Coyote = Tuning.CoyoteTime; Player.AirJumps = 1; Player.AirDashes = 1; }
            else if (Player.Coyote > 0) Player.Coyote--;
            if (Player.DashTicks > 0)
            {
                Player.DashTicks--;
                // Retain the dash's horizontal inertia after the burst. Gravity resumes next tick.
                Motor(Player); return;
            }
            float inputX = Mathf.Clamp(_input.Move.x, -1, 1);
            if (Player.Move == null && !IsCharging && Mathf.Abs(inputX) > .15f) Player.Facing = inputX > 0 ? 1 : -1;
            float control = Player.Move != null ? .28f : IsCharging ? .35f : 1;
            if (Mathf.Abs(inputX) > .12f)
            {
                // Accelerate toward run speed without deleting excess same-direction momentum.
                float acceleration = Player.Grounded ? Tuning.GroundAcceleration : Tuning.AirAcceleration;
                bool overSpeed = Mathf.Sign(Player.Velocity.x) == Mathf.Sign(inputX) && Mathf.Abs(Player.Velocity.x) > Tuning.RunSpeed;
                if (!overSpeed) Player.Velocity.x = Mathf.MoveTowards(Player.Velocity.x, inputX * Tuning.RunSpeed, acceleration * control * Delta);
                else Player.Velocity.x = Mathf.MoveTowards(Player.Velocity.x, inputX * Tuning.RunSpeed, Tuning.AirFriction * Delta);
            }
            // Attacks retain existing momentum, while the floor still brakes normally.
            // Reducing friction with attack steering made close ground combos slide through the target.
            else Player.Velocity.x = Mathf.MoveTowards(Player.Velocity.x, 0, (Player.Grounded ? Tuning.GroundFriction : Tuning.AirFriction) * Delta);
            float gravity = Tuning.Gravity * (Player.Move!=null?Mathf.Max(.05f,Player.Move.GravityScale):1);
            if (_variableJump && Player.Velocity.y > 0 && !_input.JumpHeld) gravity *= 2.1f;
            if(Player.Velocity.y<=0)_variableJump=false;
            if (_input.Move.y < -.5f && Player.Velocity.y < 0) gravity *= 1.7f;
            Player.Velocity.y = Mathf.Max(-Tuning.FallSpeed, Player.Velocity.y - gravity * Delta);
            if (Player.WallSide != 0 && inputX * Player.WallSide > .1f && Player.Velocity.y < -3.5f) Player.Velocity.y = -3.5f;
            Motor(Player);
        }

        void MoveDummy()
        {
            if (DummyCanAct)
            {
                if (DummyBehavior == DummyMode.Escape)
                {
                    float away = Player.Position.x < Dummy.Position.x ? 1 : -1;
                    Dummy.Velocity.x = Mathf.MoveTowards(Dummy.Velocity.x, away * 5.5f, 45 * Delta);
                    if (Dummy.Grounded && _dummyIdle % 45 == 0) { Dummy.Velocity.y = 10; Dummy.Grounded = false; }
                }
                else if (DummyBehavior == DummyMode.Jump && Dummy.Grounded)
                { Dummy.Velocity.y = 12; Dummy.Grounded = false; }
                else Dummy.Velocity.x = Mathf.MoveTowards(Dummy.Velocity.x, 0, (Dummy.Grounded ? 26 : .9f) * Delta);
                _dummyIdle++;
            }
            else { _dummyIdle = 0; Dummy.Velocity.x = Mathf.MoveTowards(Dummy.Velocity.x, 0, (Dummy.Grounded ? 5 : .7f) * Delta); }
            Dummy.Velocity.y = Mathf.Max(-Tuning.FallSpeed, Dummy.Velocity.y - Tuning.Gravity * Delta);
            Motor(Dummy);
            if (DummyBehavior == DummyMode.Stand && _dummyIdle > 220 && Vector2.Distance(Dummy.Position, World.DummySpawn) > 2.5f) ResetDummy();
        }

        void ResetDummy()
        {
            ResetFighter(Dummy, World.DummySpawn); Dummy.Facing = -1; _dummyIdle = 0; _chainOpen = false;
            Dummy.Armor=TargetProfile.GroundArmor;Player.CorrectionRemaining=Vector2.zero;
            Emit("reset", "DUMMY RECOLOCADO", Dummy.Position);
        }

        void DetectHit()
        {
            var move = Player.Move;
            if (move == null || !move.IsActive(Player.MoveTick)) return;
            if(move.Hitboxes!=null && move.Hitboxes.Count>0)
            {
                foreach(var box in move.Hitboxes)
                {
                    if(box==null || box.Hit==null || !box.IsActive(Player.MoveTick))continue;
                    if(_hitGroups.TryGetValue(box.HitGroup,out int previous) && (box.Hit.Rehit<=0 || Player.MoveTick-previous<box.Hit.Rehit))continue;
                    if(!WorldHitbox(box.Sample(Player.MoveTick)).Overlaps(DummyHurtbox))continue;
                    _hitGroups[box.HitGroup]=Player.MoveTick;
                    ApplyHit(box.Hit,move);
                }
            }
            else if(Catalog.SchemaVersion<2 && move.IsActive(Player.MoveTick) && move.Damage>0 &&
                !(_lastHitSerial==Player.MoveSerial && (move.Rehit<=0 || Player.MoveTick-_lastHitMoveTick<move.Rehit)) &&
                Hitbox(Player).Overlaps(DummyHurtbox))
            {
                _lastHitSerial=Player.MoveSerial;_lastHitMoveTick=Player.MoveTick;
                ApplyHit(new HitProperties{Damage=move.Damage,EnemyImpulse=move.Launch,Hitstun=move.Hitstun,Hitstop=move.Hitstop,Rehit=move.Rehit},move);
            }
        }

        void ApplyHit(HitProperties hit,MoveDefinition move)
        {
            bool continuation = _chainOpen && DummyVulnerable;
            LastComboGap = continuation ? 0 : _gapTicks;
            if (!continuation) { ComboHits = 0; ComboDamage = 0; }
            ComboHits++; ComboDamage += Mathf.Max(0,hit.Damage); BestCombo = Mathf.Max(BestCombo, ComboHits);
            _chainOpen = true; _comboIdle = 0; _gapTicks = 0;
            Dummy.Damage += Mathf.Max(0,hit.Damage);
            bool armorBlocked=false,juggleBlocked=false;
            if(Dummy.Grounded && Dummy.Armor>0)
            {
                Dummy.Armor=Mathf.Max(0,Dummy.Armor-Mathf.Max(0,hit.ArmorDamage));
                armorBlocked=Dummy.Armor>0;
                Emit("armor",armorBlocked?"ARMADURA "+Dummy.Armor.ToString("0.#"):"ARMADURA ROTA",Dummy.Position+Vector2.up*2);
            }
            else if(!Dummy.Grounded && TargetProfile.AirJuggleThreshold>0)
            {
                Dummy.JuggleMeter+=Mathf.Max(0,hit.JugglePower);Dummy.JuggleExpiry=TargetProfile.JuggleWindow;
                juggleBlocked=Dummy.JuggleMeter<TargetProfile.AirJuggleThreshold;
                if(!juggleBlocked)Dummy.JuggleMeter=Mathf.Max(0,Dummy.JuggleMeter-TargetProfile.AirJuggleThreshold);
            }
            Dummy.ArmorRegenDelay=TargetProfile.ArmorRegenDelay;
            float scaling = DamageScaling ? Mathf.Min(1.65f, 1 + Dummy.Damage * .006f) : 1;
            if(!armorBlocked && !juggleBlocked)
            {
                ApplyVelocity(Dummy,hit.EnemyImpulse*scaling*TargetProfile.LaunchVelocityScale,hit.EnemyVelocityMode,Player.Facing);
                if(Dummy.Velocity.y>0)Dummy.Grounded=false;
                Dummy.Hitstun=Mathf.Max(Dummy.Hitstun,Mathf.Max(0,hit.Hitstun));
            }
            ApplyVelocity(Player,hit.AttackerImpulse,hit.AttackerVelocityMode,Player.Facing);
            if(Player.Velocity.y>0)Player.Grounded=false;
            ScheduleCorrection(hit);
            Dummy.Facing = -Player.Facing; Dummy.LastHitTick = Tick;
            Player.MoveHit = true; Player.LastHitTick = Tick;
            Hitstop = Mathf.Max(Hitstop,Mathf.Max(0,hit.Hitstop));
            Fun = Mathf.Min(100, Fun + Mathf.Max(0,hit.Damage) * (Transformed ? .22f : .95f));
            bool fresh = !_variety.Contains(move.Id);
            if (fresh) _style = Mathf.Min(100, _style + 5 + Mathf.Max(0,hit.Damage) * .7f);
            _variety.Enqueue(move.Id); while (_variety.Count > 6) _variety.Dequeue();
            Emit("hit", move.Title + (armorBlocked?" · ARMOR":juggleBlocked?" · RESISTE":continuation?" · CONTINÚA":""), Dummy.Position + Vector2.up);
        }

        static void ApplyVelocity(FighterState actor,Vector2 impulse,VelocityMode mode,int facing)
        {
            Vector2 oriented=new Vector2(impulse.x*facing,impulse.y);
            actor.Velocity=mode==VelocityMode.Add?actor.Velocity+oriented:oriented;
        }

        void ScheduleCorrection(HitProperties hit)
        {
            if(!hit.Correction || hit.MaxCorrection<=0)return;
            float separation=(Dummy.Position.x-Player.Position.x)*Player.Facing;
            float gap=hit.PreferredDistance-separation;
            if(gap<=0)return;
            // Only confirmed overlaps schedule assistance. No target attraction is used to manufacture a hit.
            float amount=Mathf.Min(gap,hit.MaxCorrection);
            float playerShare=Mathf.Clamp01(hit.PlayerCorrectionShare);
            float targetShare=(1-playerShare)/Mathf.Max(.05f,TargetProfile.CorrectionWeight);
            float total=playerShare+targetShare;
            if(total<=0)return;
            playerShare/=total;targetShare/=total;
            Player.CorrectionRemaining=new Vector2(-Player.Facing*amount*playerShare,0);
            Dummy.CorrectionRemaining=new Vector2(Player.Facing*amount*targetShare,0);
            Player.CorrectionSpeed=Dummy.CorrectionSpeed=Mathf.Max(.01f,hit.CorrectionSpeed);
            _correctionFacing=Player.Facing;_correctionDistance=hit.PreferredDistance;
        }

        Vector2 CorrectionStep(FighterState actor)
        {
            if (!TargetEnabled) { actor.CorrectionRemaining = Vector2.zero; return Vector2.zero; }
            float separation=(Dummy.Position.x-Player.Position.x)*_correctionFacing;
            if(separation>=_correctionDistance)
            {Player.CorrectionRemaining=Vector2.zero;Dummy.CorrectionRemaining=Vector2.zero;return Vector2.zero;}
            float ratio=1-Mathf.Exp(-actor.CorrectionSpeed*Delta);
            Vector2 step=Vector2.ClampMagnitude(actor.CorrectionRemaining*ratio,3*Delta);
            actor.CorrectionRemaining-=step;
            if(actor.CorrectionRemaining.sqrMagnitude<.000001f)actor.CorrectionRemaining=Vector2.zero;
            return step;
        }

        Rect Hurtbox(FighterState f) => new Rect(f.Position.x - Tuning.Width * .5f, f.Position.y, Tuning.Width, Tuning.Height);
        Rect WorldHitbox(Rect local)
        {
            Vector2 centre=Player.Position+new Vector2(local.center.x*Player.Facing,local.center.y);
            return new Rect(centre-local.size*.5f,local.size);
        }
        Rect Hitbox(FighterState f)
        {
            var move = f.Move;
            Vector2 centre = f.Position + new Vector2(move.HitboxOffset.x * f.Facing, move.HitboxOffset.y);
            return new Rect(centre - move.HitboxSize * .5f, move.HitboxSize);
        }
        bool OnOneWay()
        {
            foreach (var p in Platforms)
                if (p.OneWay && Mathf.Abs(Player.Position.y - p.Bounds.yMax) < .06f && Player.Position.x > p.Bounds.xMin - .4f && Player.Position.x < p.Bounds.xMax + .4f) return true;
            return false;
        }

        void Motor(FighterState f, Vector2 extra = default,bool integrateVelocity=true)
        {
            // Swept axis separation prevents tunnelling through thin platforms at dash / dive speed.
            float half = Tuning.Width * .5f, height = Tuning.Height;
            Vector2 old = f.Position, next = old + extra + (integrateVelocity?f.Velocity*Delta+CorrectionStep(f):Vector2.zero);
            float travelX=next.x-old.x,travelY=next.y-old.y;
            f.WallSide = 0;
            foreach (var p in Platforms)
            {
                if (p.OneWay || old.y >= p.Bounds.yMax - .015f || old.y + height <= p.Bounds.yMin + .015f) continue;
                if (travelX > 0 && old.x + half <= p.Bounds.xMin + .015f && next.x + half >= p.Bounds.xMin)
                { next.x = Mathf.Min(next.x, p.Bounds.xMin - half); f.Velocity.x = 0; f.WallSide = 1; }
                if (travelX < 0 && old.x - half >= p.Bounds.xMax - .015f && next.x - half <= p.Bounds.xMax)
                { next.x = Mathf.Max(next.x, p.Bounds.xMax + half); f.Velocity.x = 0; f.WallSide = -1; }
            }
            f.Grounded = false;
            float landing = float.NegativeInfinity, ceiling = float.PositiveInfinity;
            foreach (var p in Platforms)
            {
                if (next.x + half <= p.Bounds.xMin + .001f || next.x - half >= p.Bounds.xMax - .001f) continue;
                if (p.OneWay && f.DropTicks > 0) continue;
                if (travelY <= 0 && old.y >= p.Bounds.yMax - .025f && next.y <= p.Bounds.yMax)
                    landing = Mathf.Max(landing, p.Bounds.yMax);
                if (!p.OneWay && travelY > 0 && old.y + height <= p.Bounds.yMin + .015f && next.y + height >= p.Bounds.yMin)
                    ceiling = Mathf.Min(ceiling, p.Bounds.yMin - height);
            }
            if (!float.IsNegativeInfinity(landing)) { next.y = landing; f.Velocity.y = 0; f.Grounded = true; f.AirJumps = 1; f.AirDashes = 1; }
            else if (!float.IsPositiveInfinity(ceiling)) { next.y = ceiling; f.Velocity.y = 0; }
            f.Position = next;
        }

        void Emit(string kind, string text, Vector2 position) => Events.Add(new CombatEvent(kind, text, position));
    }
}
