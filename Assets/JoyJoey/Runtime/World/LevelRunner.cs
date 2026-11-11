using System;
using System.Collections.Generic;
using JoyJoey.Sandbox;
using UnityEngine;

namespace JoyJoey.Game
{
    /// <summary>Connects authored scene objects to the same input and motor used by the sandbox.</summary>
    [DisallowMultipleComponent]
    public sealed class LevelRunner : MonoBehaviour
    {
        [Header("Datos compartidos con el sandbox")]
        public CombatCatalog Catalog;
        public MotorTuning Tuning;
        [Header("Objetos de esta escena")]
        public WorldPlayer Player;
        public Transform SpawnPoint;
        [Tooltip("Las superficies activas dentro de esta raíz se capturan al iniciar Play.")]
        public Transform GeometryRoot;
        [Tooltip("Alternativa cuando no se asigna GeometryRoot.")]
        public LevelSurface[] Surfaces;
        [Header("Límites de reaparición, en coordenadas del mundo")]
        public float RespawnBelowY = -12;
        public float LeftBoundary = -60, RightBoundary = 60;

        public CombatSimulation Simulation { get; private set; }
        public string InputDevice => _input != null ? _input.Device : "TECLADO";
        public int GeometryCount => Simulation != null ? Simulation.Platforms.Count : 0;
        JoyInput _input;
        readonly InputFrame _pending = new InputFrame();
        float _accumulator;
        EnemyProfile _ownedProfile;

        void Start() { Initialize(); }
        void OnEnable() { if (Simulation != null && Application.isPlaying) EnableInput(); }
        void OnDisable() { ReleaseInput(); }

        public void Initialize()
        {
            if (Simulation != null) return;
            if (!Catalog || !Tuning || !Player || !SpawnPoint)
            {
                Debug.LogError("Joy Joey: asigna Catalog, Tuning, Player y SpawnPoint en LevelRunner.", this);
                enabled = false;
                return;
            }
            if (LeftBoundary >= RightBoundary)
            {
                Debug.LogError("Joy Joey: LeftBoundary debe ser menor que RightBoundary.", this);
                enabled = false;
                return;
            }
            try
            {
                var world = new CombatWorld
                {
                    TargetEnabled = false,
                    PlayerSpawn = SpawnPoint.position,
                    RespawnBelowY = RespawnBelowY,
                    LeftBoundary = LeftBoundary,
                    RightBoundary = RightBoundary
                };
                world.Platforms.AddRange(ReadGeometry());
                _ownedProfile = ScriptableObject.CreateInstance<EnemyProfile>();
                _ownedProfile.hideFlags = HideFlags.DontSave;
                Simulation = new CombatSimulation(Catalog, Tuning, _ownedProfile, world);
                Player.Render(Simulation.Player);
                if (Application.isPlaying) GameSession.EnsureExists().RegisterLevel(this);
                if (Application.isPlaying && isActiveAndEnabled) EnableInput();
            }
            catch (InvalidOperationException error)
            {
                Debug.LogError("Joy Joey: " + error.Message, this);
                enabled = false;
            }
        }

        List<ArenaPlatform> ReadGeometry()
        {
            var result = new List<ArenaPlatform>();
            var surfaces = GeometryRoot ? GeometryRoot.GetComponentsInChildren<LevelSurface>() : Surfaces;
            if (surfaces == null) return result;
            foreach (var surface in surfaces)
            {
                if (!surface || !surface.isActiveAndEnabled) continue;
                var box = surface.Collider ? surface.Collider : surface.GetComponent<BoxCollider2D>();
                if (!box || !box.enabled || box.isTrigger) continue;
                result.Add(surface.ToPlatform());
            }
            return result;
        }

        /// <summary>Explicit refresh for deliberate runtime geometry changes; moving platforms are not simulated.</summary>
        public void RefreshWorldGeometry()
        {
            if (Simulation == null) return;
            var geometry = ReadGeometry();
            Simulation.Platforms.Clear();
            Simulation.Platforms.AddRange(geometry);
            Simulation.World.Platforms.Clear();
            Simulation.World.Platforms.AddRange(geometry);
        }

        public void ResetPlayer()
        {
            if (Simulation == null) return;
            Simulation.World.PlayerSpawn = SpawnPoint ? (Vector2)SpawnPoint.position : Simulation.World.PlayerSpawn;
            Simulation.Reset();
            ClearPending();
            _input?.ClearPending();
            _accumulator = 0;
            Player.Render(Simulation.Player);
        }

        public bool IsSafeCheckpoint(Vector2 point)
        {
            if (Simulation == null || float.IsNaN(point.x) || float.IsNaN(point.y) ||
                float.IsInfinity(point.x) || float.IsInfinity(point.y) || Simulation.World.IsOutside(point)) return false;
            float half = Tuning.Width * .5f;
            bool supported = false;
            var body = new Rect(point.x - half, point.y + .02f, Tuning.Width, Mathf.Max(.01f, Tuning.Height - .04f));
            foreach (var surface in Simulation.Platforms)
            {
                if (!surface.OneWay && body.Overlaps(surface.Bounds)) return false;
                if (Mathf.Abs(point.y - surface.Bounds.yMax) < .06f &&
                    point.x - half >= surface.Bounds.xMin && point.x + half <= surface.Bounds.xMax) supported = true;
            }
            return supported;
        }

        public bool TryGetCheckpoint(out Vector2 point)
        {
            point = Simulation != null ? Simulation.Player.Position : Vector2.zero;
            if (Simulation == null) return false;
            var actor = Simulation.Player;
            return actor.Grounded && actor.Move == null && actor.DashTicks == 0 &&
                !Simulation.IsCharging && IsSafeCheckpoint(point);
        }

        public bool TryMoveJoeyTo(Vector2 point)
        {
            if (!IsSafeCheckpoint(point)) return false;
            Simulation.PlacePlayerOnGround(point);
            ClearPending();
            _input?.ClearPending();
            Player.Render(Simulation.Player);
            foreach (var camera in FindObjectsByType<LevelCamera>())
                if (camera.Target == Player.transform) camera.SnapToTarget();
            return true;
        }

        void EnableInput()
        {
            if (_input == null) _input = new JoyInput();
            _input.SetGameplayPaused(GameSession.Instance && GameSession.Instance.BlocksGameplay);
        }
        public void SetGameplayPaused(bool paused)
        {
            if (_input != null && _input.GameplayPaused == paused) return;
            _input?.SetGameplayPaused(paused);
            if (paused) Simulation?.CancelActionsForPause();
            ClearPending();
            _accumulator = 0;
        }
        void ReleaseInput()
        {
            _input?.Dispose();
            _input = null;
            ClearPending();
            _accumulator = 0;
        }
        void ClearPending()
        {
            _pending.Commands.Clear();
            _pending.HeldActions.Clear();
            _pending.Move = Vector2.zero;
            _pending.JumpHeld = _pending.BasicHeld = _pending.RagHeld = false;
            _pending.BasicHeldSeconds = _pending.RagHeldSeconds = 0;
        }

        void Update()
        {
            if (Simulation == null || _input == null) return;
            _input.SetGameplayPaused(GameSession.Instance && GameSession.Instance.BlocksGameplay);
            var frame = _input.ConsumeFrame();
            if (GameSession.Instance && GameSession.Instance.BlocksGameplay)
            {
                ClearPending();
                _input.ClearPending();
                _accumulator = 0;
                return;
            }
            // Future pauses can stop scaled game time without replaying menu inputs on resume.
            if (Time.deltaTime <= 0)
            {
                ClearPending();
                _accumulator = 0;
                return;
            }
            _pending.Move = frame.Move;
            _pending.JumpHeld = frame.JumpHeld;
            _pending.BasicHeld = frame.BasicHeld;
            _pending.RagHeld = frame.RagHeld;
            _pending.BasicHeldSeconds = frame.BasicHeldSeconds;
            _pending.RagHeldSeconds = frame.RagHeldSeconds;
            _pending.Device = frame.Device;
            _pending.HeldActions.Clear();
            _pending.HeldActions.UnionWith(frame.HeldActions);
            foreach (var command in frame.Commands)
            {
                if ((int)command.Action < (int)ActionId.Reset) _pending.Commands.Add(command);
                else if (command.Action == ActionId.Reset && command.Edge == InputEdge.Press) ResetPlayer();
            }
            _accumulator += Mathf.Min(Time.deltaTime, .1f);
            int count = 0;
            while (_accumulator >= CombatSimulation.Delta && count++ < 8)
            {
                Simulation.Step(_pending);
                _pending.Commands.Clear();
                _accumulator -= CombatSimulation.Delta;
            }
            Player.Render(Simulation.Player);
        }

        void OnApplicationFocus(bool focused)
        {
            if (focused) return;
            ClearPending();
            _input?.ClearPending();
            _accumulator = 0;
        }

        void OnDestroy()
        {
            ReleaseInput();
            if (_ownedProfile)
            {
                if (Application.isPlaying) Destroy(_ownedProfile);
                else DestroyImmediate(_ownedProfile);
            }
        }

        void OnDrawGizmos()
        {
            if (!SpawnPoint) return;
            Vector3 position = SpawnPoint.position;
            Gizmos.color = new Color(1, .75f, .25f);
            Gizmos.DrawWireSphere(position, .16f);
            Gizmos.DrawLine(position, position + Vector3.up * .65f);
            Gizmos.DrawLine(position + Vector3.up * .65f, position + new Vector3(-.12f, .45f));
            Gizmos.DrawLine(position + Vector3.up * .65f, position + new Vector3(.12f, .45f));
            if (Tuning)
                Gizmos.DrawWireCube(position + Vector3.up * Tuning.Height * .5f,
                    new Vector3(Tuning.Width, Tuning.Height));
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1, .3f, .35f, .7f);
            Gizmos.DrawLine(new Vector3(LeftBoundary, RespawnBelowY), new Vector3(RightBoundary, RespawnBelowY));
            Gizmos.DrawLine(new Vector3(LeftBoundary, RespawnBelowY), new Vector3(LeftBoundary, RespawnBelowY + 30));
            Gizmos.DrawLine(new Vector3(RightBoundary, RespawnBelowY), new Vector3(RightBoundary, RespawnBelowY + 30));
        }
    }
}
