using System;
using System.Collections.Generic;
using JoyJoey.Game.UI;
using UnityEngine;

namespace JoyJoey.Sandbox
{
    /// <summary>Owns the fixed 60 Hz simulation. Rendering and input collection never set combat time.</summary>
    public sealed class SandboxRunner : MonoBehaviour
    {
        public CombatCatalog Catalog;
        public MotorTuning Tuning;
        public EnemyProfile LightEnemy,HeavyEnemy;
        public bool HeavyTarget { get; private set; }
        public CombatSimulation Simulation { get; private set; }
        public bool DebugHitboxes { get; private set; }
        public bool HelpVisible { get; private set; }
        public bool Paused { get; private set; }
        public bool SlowMotion { get; private set; }
        public string InputDevice => _input == null ? "TECLADO" : _input.Device;
        public IReadOnlyList<string> InputHistory => _history;
        public Camera ArenaCamera { get; private set; }

        JoyInput _input;
        readonly InputFrame _pending = new InputFrame();
        readonly List<string> _history = new List<string>();
        readonly List<Spark> _sparks = new List<Spark>();
        readonly List<SpriteRenderer> _debugLines = new List<SpriteRenderer>();
        FighterView _playerView, _dummyView;
        SandboxHud _hud;
        Transform _world;
        float _accumulator, _ghostTime;
        float _poseTime;
        int _stepRequests;

        public static Color PlayerColor = new Color(.32f, .91f, .88f);
        public static Color DummyColor = new Color(1f, .48f, .39f);
        public static readonly Color Ink = new Color(.055f, .075f, .12f);
        static Sprite _square, _disc;
        sealed class Spark { public SpriteRenderer Sprite; public Vector2 Velocity; public float Remaining, Lifetime; public Color Color; }

        public static Sprite Square
        {
            get
            {
                if (_square == null) _square = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.one * .5f, 1);
                return _square;
            }
        }
        public static Sprite Disc
        {
            get
            {
                if (_disc != null) return _disc;
                const int size = 48;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Sandbox circle", filterMode = FilterMode.Bilinear };
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), Vector2.one * size / 2f);
                    pixels[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(size / 2f - distance));
                }
                tex.SetPixels(pixels); tex.Apply();
                _disc = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * .5f, size);
                return _disc;
            }
        }

        void Start()
        {
            JoyJoey.Game.GameSession.EnsureExists();
            if(!LightEnemy)LightEnemy=CombatDefaults.CreateLightEnemy();
            if(!HeavyEnemy)HeavyEnemy=CombatDefaults.CreateHeavyEnemy();
            Simulation = new CombatSimulation(Catalog, Tuning,LightEnemy);
            _input = new JoyInput();
            _world = new GameObject("Circo Onirico - runtime stage").transform;
            _world.SetParent(transform, false);
            BuildStage();
            _playerView = new FighterView(_world, "Joey", false);
            _dummyView = new FighterView(_world, "Practice dummy", true);
            _hud = new SandboxHud(this);
            UpdateViews(0);
        }

        void Update()
        {
            if (Simulation == null) return;
            var session = JoyJoey.Game.GameSession.Instance;
            _input.SetGameplayPaused(session && session.BlocksGameplay);
            if (session && session.BlocksGameplay)
            {
                _input.ConsumeFrame();
                _input.ClearPending();
                _pending.Commands.Clear();
                _pending.HeldActions.Clear();
                _accumulator = 0;
                _stepRequests = 0;
                return;
            }
            CollectInput(_input.ConsumeFrame());
            if (!Paused && !HelpVisible)
            {
                _accumulator += Mathf.Min(Time.unscaledDeltaTime, .1f) * (SlowMotion ? .35f : 1);
                int count = 0;
                while (_accumulator >= CombatSimulation.Delta && count++ < 8)
                {
                    Advance();
                    _accumulator -= CombatSimulation.Delta;
                }
            }
            else
            {
                _accumulator = 0;
                while (_stepRequests > 0) { Advance(); _stepRequests--; }
            }
            UpdateViews(Time.unscaledDeltaTime);
        }

        void CollectInput(InputFrame frame)
        {
            _pending.Move = frame.Move;
            _pending.JumpHeld = frame.JumpHeld;
            _pending.BasicHeld = frame.BasicHeld;
            _pending.RagHeld = frame.RagHeld;
            _pending.BasicHeldSeconds = frame.BasicHeldSeconds;
            _pending.RagHeldSeconds = frame.RagHeldSeconds;
            _pending.Device = frame.Device;
            _pending.HeldActions.Clear();_pending.HeldActions.UnionWith(frame.HeldActions);
            foreach (var command in frame.Commands)
            {
                bool training = true;
                if (command.Edge == InputEdge.Press)
                {
                    switch (command.Action)
                    {
                        case ActionId.TogglePause: TogglePause(); break;
                        case ActionId.Step: Paused = true; HelpVisible = false; _stepRequests++; break;
                        case ActionId.ToggleHelp: ToggleHelp(); break;
                        case ActionId.ToggleDebug: DebugHitboxes = !DebugHitboxes; break;
                        case ActionId.ToggleDummy: CycleDummy(); break;
                        case ActionId.ToggleSlow: SlowMotion = !SlowMotion; break;
                        case ActionId.ToggleDamage: ToggleDamage(); break;
                        case ActionId.ToggleEnemy: ToggleEnemy(); break;
                        case ActionId.Reset: ResetPractice(); break;
                        default: training = false; break;
                    }
                }
                else training = (int)command.Action >= (int)ActionId.Reset;
                if (training) continue;
                _pending.Commands.Add(command);
                if (command.Edge == InputEdge.Press)
                {
                    string direction = command.Direction.y < -.45f ? "v " : command.Direction.y > .45f ? "^ " : Mathf.Abs(command.Direction.x) > .45f ? (command.Direction.x > 0 ? "> " : "< ") : "- ";
                    _history.Insert(0, direction + command.Action.ToString().ToUpperInvariant());
                    if (_history.Count > 7) _history.RemoveAt(_history.Count - 1);
                }
            }
        }

        void Advance()
        {
            bool frozen=Simulation.Hitstop>0;
            Simulation.Step(_pending);
            if(!frozen)_poseTime+=CombatSimulation.Delta;
            _pending.Commands.Clear();
            foreach (var evt in Simulation.Events) OnCombatEvent(evt);
        }

        public void ResetPractice()
        {
            Simulation.Reset();
            _pending.Commands.Clear();
            _input?.ClearPending();
            _accumulator = 0;
            _history.Clear();
            foreach (var spark in _sparks) if (spark.Sprite) Destroy(spark.Sprite.gameObject);
            _sparks.Clear();
        }
        public void TogglePause()
        {
            if (JoyJoey.Game.GameSession.Instance)
                JoyJoey.Game.GameSession.Instance.SetPaused(!JoyJoey.Game.GameSession.Instance.IsPaused);
            else Paused = !Paused;
            _pending.Commands.Clear(); _input?.ClearPending();
        }
        public void ResumeFromMenu()
        {
            Paused = false;
            _accumulator = 0;
            _pending.Commands.Clear();
            _input?.ClearPending();
        }
        public void SetGameplayPaused(bool paused)
        {
            if (_input != null && _input.GameplayPaused == paused) return;
            _input?.SetGameplayPaused(paused);
            if (paused) Simulation?.CancelActionsForPause();
            else Paused = false;
            _pending.Commands.Clear();
            _pending.HeldActions.Clear();
            _pending.Move = Vector2.zero;
            _pending.JumpHeld = _pending.BasicHeld = _pending.RagHeld = false;
            _pending.BasicHeldSeconds = _pending.RagHeldSeconds = 0;
            _accumulator = 0;
            _stepRequests = 0;
        }
        public void ToggleHelp() { HelpVisible = !HelpVisible; _pending.Commands.Clear(); _input?.ClearPending(); }
        public void ToggleDebug() => DebugHitboxes = !DebugHitboxes;
        public void ToggleSlow() => SlowMotion = !SlowMotion;
        public void CycleDummy() => Simulation.SetDummyMode((DummyMode)(((int)Simulation.DummyBehavior + 1) % 3));
        public void ToggleDamage() => Simulation.DamageScaling = !Simulation.DamageScaling;
        public void ToggleEnemy()
        { HeavyTarget=!HeavyTarget;Simulation.SetEnemyProfile(HeavyTarget?HeavyEnemy:LightEnemy); }

        void BuildStage()
        {
            ArenaCamera = Camera.main;
            if (!ArenaCamera) ArenaCamera = new GameObject("Arena Camera", typeof(Camera)).GetComponent<Camera>();
            ArenaCamera.orthographic = true;
            ArenaCamera.orthographicSize = 6.8f;
            ArenaCamera.clearFlags = CameraClearFlags.SolidColor;
            ArenaCamera.backgroundColor = new Color(.048f, .065f, .10f);
            ArenaCamera.transform.SetPositionAndRotation(new Vector3(0, 3.1f, -10), Quaternion.identity);
            if (!FindAnyObjectByType<AudioListener>()) ArenaCamera.gameObject.AddComponent<AudioListener>();

            // A deliberately quiet arena: all shapes that affect movement have a bright upper edge.
            RectSprite(_world, "Backdrop", new Vector2(0, 6), new Vector2(70, 36), new Color(.055f, .079f, .12f), -40);
            for (int x = -18; x <= 18; x += 3)
                RectSprite(_world, "Grid", new Vector2(x, 4), new Vector2(.018f, 16), new Color(.10f, .14f, .19f), -30);
            for (int y = 0; y <= 14; y += 2)
                RectSprite(_world, "Grid", new Vector2(0, y), new Vector2(40, .018f), new Color(.10f, .14f, .19f), -30);
            for (int x = -18; x <= 18; x += 6)
            {
                var stripe = RectSprite(_world, "Tent stripe", new Vector2(x, 10.5f), new Vector2(2.2f, 8), new Color(.11f, .10f, .18f), -35);
                stripe.transform.rotation = Quaternion.Euler(0, 0, x > 0 ? -13 : 13);
                RectSprite(_world, "Tent pin", new Vector2(x, 7.4f), new Vector2(.12f, .12f), new Color(.6f, .43f, .3f), -28).sprite = Disc;
            }
            foreach (var platform in Simulation.Platforms)
            {
                Rect r = platform.Bounds;
                Color fill = platform.OneWay ? new Color(.15f, .21f, .27f) : new Color(.10f, .15f, .21f);
                RectSprite(_world, platform.OneWay ? "One-way platform" : "Solid", r.center, r.size, fill, 0);
                RectSprite(_world, "Platform rim", new Vector2(r.center.x, r.yMax - .045f), new Vector2(r.width, .09f), platform.OneWay ? new Color(.4f, .56f, .62f) : new Color(.26f, .64f, .62f), 1);
                if (platform.OneWay)
                {
                    RectSprite(_world, "Platform bracket", new Vector2(r.xMin + .25f, r.yMin - .10f), new Vector2(.09f, .25f), fill, 0);
                    RectSprite(_world, "Platform bracket", new Vector2(r.xMax - .25f, r.yMin - .10f), new Vector2(.09f, .25f), fill, 0);
                }
            }
            for (int x = -13; x <= 13; x++)
                RectSprite(_world, "Training ruler", new Vector2(x, -.2f), new Vector2(.025f, x % 5 == 0 ? .18f : .09f), new Color(.23f, .32f, .37f), 2);
            RectSprite(_world, "Joey spawn mark", new Vector2(-3, -.02f), new Vector2(.8f, .035f), PlayerColor, 3);
            RectSprite(_world, "Dummy spawn mark", new Vector2(0, -.02f), new Vector2(.8f, .035f), DummyColor, 3);
            for (int i = 0; i < 12; i++) _debugLines.Add(RectSprite(_world, "Debug outline", Vector2.zero, Vector2.one, Color.white, 30));
        }

        void UpdateViews(float dt)
        {
            var sim = Simulation;
            Color ragColor = sim.SelectedRag == RagStyle.Strongman ? new Color(1, .70f, .27f) : sim.SelectedRag == RagStyle.Jester ? new Color(.77f, .50f, 1) : new Color(.38f, .70f, 1);
            _playerView.Update(sim.Player, sim.Transformed ? ragColor : PlayerColor, ragColor, sim.Transformed, _poseTime, sim.Hitstop,sim.Catalog.SchemaVersion<2);
            _dummyView.Update(sim.Dummy, DummyColor, DummyColor, false, _poseTime, sim.Hitstop,sim.Catalog.SchemaVersion<2);
            _playerView.SetRagVisible(true);
            _dummyView.SetRagVisible(false);

            Vector2 middle = (sim.Player.Position + sim.Dummy.Position) * .5f;
            float width = Mathf.Abs(sim.Player.Position.x - sim.Dummy.Position.x) + 7;
            float height = Mathf.Max(sim.Player.Position.y, sim.Dummy.Position.y) + 4;
            float aspect = Mathf.Max(.8f, ArenaCamera.aspect);
            float size = Mathf.Clamp(Mathf.Max(6.8f, width / (2 * aspect), height / 1.6f), 6.8f, 11.5f);
            float targetY = Mathf.Max(3.1f, Mathf.Max(sim.Player.Position.y, sim.Dummy.Position.y) * .38f + 2.3f);
            float targetX = Mathf.Clamp(middle.x, -4, 4);
            float ease = 1 - Mathf.Exp(-5 * dt);
            ArenaCamera.orthographicSize = Mathf.Lerp(ArenaCamera.orthographicSize, size, ease);
            ArenaCamera.transform.position = Vector3.Lerp(ArenaCamera.transform.position, new Vector3(targetX, targetY, -10), ease);

            DrawRect(0, sim.PlayerHurtbox, PlayerColor);
            DrawRect(4, sim.DummyHurtbox, DummyColor);
            var boxes=sim.ActiveHitboxes;
            while(_debugLines.Count<8+boxes.Count*4)_debugLines.Add(RectSprite(_world,"Debug hitbox",Vector2.zero,Vector2.one,Color.white,30));
            for(int index=8;index<_debugLines.Count;index+=4)
                DrawRect(index,(index-8)/4<boxes.Count?boxes[(index-8)/4]:new Rect(),new Color(1,.85f,.2f));
            _ghostTime -= dt;
            if (sim.Player.DashTicks > 0 && _ghostTime <= 0 && !Paused)
            {
                _ghostTime = .035f;
                AddSpark(sim.Player.Position + Vector2.up * .85f, Vector2.zero, sim.Transformed ? ragColor : PlayerColor, .16f, new Vector2(.55f, 1.3f));
            }
            float fxDt = Paused || HelpVisible ? 0 : dt * (SlowMotion ? .35f : 1);
            for (int i = _sparks.Count - 1; i >= 0; i--)
            {
                Spark spark = _sparks[i];
                spark.Remaining -= fxDt;
                if (spark.Remaining <= 0) { Destroy(spark.Sprite.gameObject); _sparks.RemoveAt(i); continue; }
                spark.Sprite.transform.position += (Vector3)(spark.Velocity * fxDt);
                spark.Velocity += Vector2.down * 10 * fxDt;
                spark.Sprite.color = new Color(spark.Color.r, spark.Color.g, spark.Color.b, spark.Remaining / spark.Lifetime);
            }
        }

        void DrawRect(int offset, Rect rect, Color color)
        {
            bool show = DebugHitboxes && rect.width > 0 && rect.height > 0;
            for (int i = 0; i < 4; i++) _debugLines[offset + i].enabled = show;
            if (!show) return;
            SetLine(_debugLines[offset], new Vector2(rect.xMin, rect.center.y), new Vector2(.025f, rect.height), color);
            SetLine(_debugLines[offset + 1], new Vector2(rect.xMax, rect.center.y), new Vector2(.025f, rect.height), color);
            SetLine(_debugLines[offset + 2], new Vector2(rect.center.x, rect.yMin), new Vector2(rect.width, .025f), color);
            SetLine(_debugLines[offset + 3], new Vector2(rect.center.x, rect.yMax), new Vector2(rect.width, .025f), color);
        }
        static void SetLine(SpriteRenderer sprite, Vector2 at, Vector2 size, Color color)
        { sprite.transform.position = new Vector3(at.x, at.y, -1); sprite.transform.localScale = size; sprite.color = color; }

        void OnCombatEvent(CombatEvent evt)
        {
            if (evt.Kind == "hit" || evt.Kind == "launch" || evt.Kind == "bounce")
            {
                _dummyView.Flash();
                for (int i = 0; i < 9; i++)
                {
                    float angle = i * Mathf.PI * 2 / 9 + UnityEngine.Random.Range(-.2f, .2f);
                    Vector2 velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * UnityEngine.Random.Range(2f, 6f);
                    AddSpark(evt.Position, velocity, i % 3 == 0 ? Color.white : new Color(1, .75f, .32f), .22f, new Vector2(.08f, .13f));
                }
            }
            else if (evt.Kind == "dash" || evt.Kind == "jump" || evt.Kind == "cancel")
                for (int i = 0; i < 4; i++) AddSpark(evt.Position + Vector2.up * .15f, new Vector2(UnityEngine.Random.Range(-2, 2), UnityEngine.Random.Range(.2f, 2)), PlayerColor, .2f, Vector2.one * .07f);
        }
        void AddSpark(Vector2 at, Vector2 velocity, Color color, float life, Vector2 size)
        {
            if (_sparks.Count > 100) return;
            SpriteRenderer sprite = RectSprite(_world, "Hit spark", at, size, color, 25);
            sprite.transform.rotation = Quaternion.Euler(0, 0, UnityEngine.Random.Range(0, 180));
            _sparks.Add(new Spark { Sprite = sprite, Velocity = velocity, Color = color, Lifetime = life, Remaining = life });
        }

        public static SpriteRenderer RectSprite(Transform parent, string name, Vector2 position, Vector2 size, Color color, int order)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = new Vector3(size.x, size.y, 1);
            var sprite = obj.AddComponent<SpriteRenderer>();
            sprite.sprite = Square; sprite.color = color; sprite.sortingOrder = order;
            return sprite;
        }

        void OnGUI()
        {
            var session = JoyJoey.Game.GameSession.Instance;
            if (CinematicOverlay.CapturesInput) return;
            if (session && (session.IsPaused || session.IsLoading)) return;
            if (_hud != null && Simulation != null) _hud.Draw();
        }
        void OnApplicationFocus(bool focus) { if (!focus) { _pending.Commands.Clear(); _pending.Move = Vector2.zero; _input?.ClearPending(); } }
        void OnDestroy()
        {
            _input?.Dispose();
            _hud?.Dispose();
            if (_world) Destroy(_world.gameObject);
        }
    }
}
