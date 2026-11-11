using UnityEngine;
using UnityEngine.Events;

namespace JoyJoey.Game
{
    [AddComponentMenu("Joy Joey/World/Joey Area")]
    [DisallowMultipleComponent, RequireComponent(typeof(BoxCollider2D))]
    public class JoeyArea : MonoBehaviour
    {
        [Tooltip("Si queda vacío, usa el LevelRunner registrado en GameSession.")]
        [SerializeField] LevelRunner _level;
        [SerializeField] UnityEvent<GameObject> _onEntered = new UnityEvent<GameObject>();
        BoxCollider2D _area;
        bool _inside;

        protected void Reset() => GetComponent<BoxCollider2D>().isTrigger = true;

        protected virtual void Awake() => _area = GetComponent<BoxCollider2D>();

        protected void LateUpdate()
        {
            var session = GameSession.Instance;
            if (session && session.BlocksGameplay) return;

            var level = _level ? _level : session?.ActiveLevel;
            if (!level || !level.Player || level.Simulation == null || !_area.enabled)
            {
                _inside = false;
                return;
            }

            // The kinematic motor has no Rigidbody2D. Sample its body centre after movement.
            Vector2 center = level.Simulation.Player.Position + Vector2.up * (level.Tuning.Height * .5f);
            bool wasInside = _inside;
            _inside = Contains(_area, center);
            if (_inside && !wasInside) Entered(level.Player.gameObject);
        }

        protected virtual void Entered(GameObject joey) => _onEntered.Invoke(joey);

        static bool Contains(BoxCollider2D area, Vector2 point)
        {
            var worldPoint = new Vector3(point.x, point.y, area.transform.position.z);
            Vector2 local = (Vector2)area.transform.InverseTransformPoint(worldPoint) - area.offset;
            Vector2 half = area.size * .5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y;
        }
    }
}
