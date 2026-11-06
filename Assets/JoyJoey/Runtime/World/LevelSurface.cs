using System;
using JoyJoey.Sandbox;
using UnityEngine;

namespace JoyJoey.Game
{
    /// <summary>An authored rectangular collider consumed by the shared character motor.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(BoxCollider2D))]
    public sealed class LevelSurface : MonoBehaviour
    {
        public BoxCollider2D Collider;
        [Tooltip("Solo bloquea al caer; abajo + salto permite atravesarla.")]
        public bool OneWay;

        public bool IsAvailable => isActiveAndEnabled && Collider != null && Collider.enabled && !Collider.isTrigger;

        void Reset() { Collider = GetComponent<BoxCollider2D>(); }
        void OnValidate() { if (!Collider) Collider = GetComponent<BoxCollider2D>(); }

        /// <summary>Uses serialized collider data, so scene edits are valid before Physics2D has stepped.</summary>
        public ArenaPlatform ToPlatform()
        {
            var box = Collider ? Collider : GetComponent<BoxCollider2D>();
            if (!box) throw new InvalidOperationException(name + ": falta BoxCollider2D.");
            Vector2 half = box.size * .5f;
            Vector3 a = box.transform.TransformPoint(box.offset - half);
            Vector3 b = box.transform.TransformPoint(box.offset + new Vector2(half.x, -half.y));
            Vector3 c = box.transform.TransformPoint(box.offset + half);
            Vector3 d = box.transform.TransformPoint(box.offset + new Vector2(-half.x, half.y));
            if (!AxisAligned(b - a) || !AxisAligned(d - a) ||
                Mathf.Abs(a.z - b.z) > .0001f || Mathf.Abs(a.z - d.z) > .0001f)
                throw new InvalidOperationException(name + ": LevelSurface admite rectángulos alineados con los ejes; usa rotaciones de 0, 90, 180 o 270 grados.");
            Vector2 min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
            Vector2 max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
            if (max.x - min.x <= .0001f || max.y - min.y <= .0001f)
                throw new InvalidOperationException(name + ": la superficie debe tener ancho y alto positivos.");
            return new ArenaPlatform(min.x, min.y, max.x - min.x, max.y - min.y, OneWay);
        }

        static bool AxisAligned(Vector3 edge) => Mathf.Abs(edge.x) <= .0001f || Mathf.Abs(edge.y) <= .0001f;

        void OnDrawGizmosSelected()
        {
            try
            {
                var platform = ToPlatform();
                Gizmos.color = OneWay ? new Color(.4f, .8f, 1) : new Color(.3f, 1, .6f);
                Gizmos.DrawWireCube(platform.Bounds.center, platform.Bounds.size);
                if (OneWay)
                    Gizmos.DrawLine(new Vector3(platform.Bounds.xMin, platform.Bounds.yMax),
                        new Vector3(platform.Bounds.xMax, platform.Bounds.yMax));
            }
            catch (InvalidOperationException) { }
        }
    }
}
