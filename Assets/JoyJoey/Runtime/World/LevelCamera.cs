using UnityEngine;

namespace JoyJoey.Game
{
    /// <summary>Follows a placed actor while keeping the camera viewport inside the level.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class LevelCamera : MonoBehaviour
    {
        public Transform Target;
        [Tooltip("Zona visible del nivel. Si es menor que la cámara, se centra en ese eje.")]
        public Rect Bounds = new Rect(-40, -2, 80, 18);
        public Vector3 Offset = new Vector3(0, 2, -10);
        [Min(0)] public float FollowSharpness = 8;
        Camera _camera;

        void Awake() => _camera = GetComponent<Camera>();
        void Start() => SnapToTarget();

        public Vector3 ConstrainPosition(Vector3 position)
        {
            if (!_camera) _camera = GetComponent<Camera>();
            if (!_camera || !_camera.orthographic) return position;
            float height = _camera.orthographicSize;
            float width = height * Mathf.Max(.01f, _camera.aspect);
            position.x = ClampAxis(position.x, Bounds.xMin, Bounds.xMax, width);
            position.y = ClampAxis(position.y, Bounds.yMin, Bounds.yMax, height);
            return position;
        }

        static float ClampAxis(float value, float min, float max, float halfView)
            => max - min <= halfView * 2 ? (min + max) * .5f : Mathf.Clamp(value, min + halfView, max - halfView);

        public void SnapToTarget()
        {
            if (Target) transform.position = ConstrainPosition(Target.position + Offset);
        }

        void LateUpdate()
        {
            if (!Target) return;
            Vector3 target = ConstrainPosition(Target.position + Offset);
            float ease = FollowSharpness <= 0 ? 1 : 1 - Mathf.Exp(-FollowSharpness * Time.deltaTime);
            transform.position = ConstrainPosition(Vector3.Lerp(transform.position, target, ease));
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(.4f, .8f, 1, .8f);
            Gizmos.DrawWireCube(new Vector3(Bounds.center.x, Bounds.center.y, 0), new Vector3(Bounds.width, Bounds.height, 0));
        }
    }
}
