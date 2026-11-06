using JoyJoey.Sandbox;
using UnityEngine;

namespace JoyJoey.Game
{
    /// <summary>Scene-authored visual adapter. The shared simulation remains authoritative.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldPlayer : MonoBehaviour
    {
        public SpriteRenderer Visual;
        public Sprite IdleSprite, RunSprite, JumpSprite, WallSprite;
        [Min(.1f)] public float VisualHeight = 1.9f;
        GameObject _customVisual, _visualPrefab;

        void OnValidate()
        {
            if (!Application.isPlaying) RefreshPreview();
        }

        /// <summary>Keeps different prototype drawings at the same height with their feet at the root.</summary>
        public void RefreshPreview()
        {
            if (!Visual) Visual = GetComponentInChildren<SpriteRenderer>(true);
            if (!Visual) return;
            if (IdleSprite) Visual.sprite = IdleSprite;
            FitSprite();
        }

        public void Render(FighterState actor)
        {
            if (actor == null) return;
            transform.position = new Vector3(actor.Position.x, actor.Position.y, transform.position.z);
            if (!Visual) Visual = GetComponentInChildren<SpriteRenderer>(true);
            if (Visual)
            {
                var sprite = !actor.Grounded ? (actor.WallSide != 0 && actor.Velocity.y < 0 && WallSprite ? WallSprite : JumpSprite) :
                    Mathf.Abs(actor.Velocity.x) > .3f ? RunSprite : IdleSprite;
                Visual.sprite = sprite ? sprite : IdleSprite;
                Visual.flipX = actor.Facing < 0;
                FitSprite();
            }

            var prefab = actor.Move != null ? actor.Move.PreviewPrefab : null;
            if (_visualPrefab != prefab)
            {
                DisposeCustomVisual();
                _visualPrefab = prefab;
                if (prefab)
                {
                    _customVisual = Instantiate(prefab, transform);
                    _customVisual.transform.localPosition = Vector3.zero;
                    _customVisual.transform.localRotation = Quaternion.identity;
                    foreach (var animator in _customVisual.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                    foreach (var animation in _customVisual.GetComponentsInChildren<Animation>(true)) animation.enabled = false;
                }
            }
            if (Visual) Visual.enabled = !_customVisual;
            if (_customVisual)
            {
                AttackSampling.SampleVisual(actor.Move, _customVisual, actor.MoveTick);
                var scale = prefab.transform.localScale;
                scale.x *= actor.Facing;
                _customVisual.transform.localScale = scale;
                _customVisual.transform.localPosition = Vector3.zero;
                _customVisual.transform.localRotation = Quaternion.identity;
            }
        }

        void FitSprite()
        {
            if (!Visual || !Visual.sprite) return;
            float scale = Mathf.Max(.1f, VisualHeight) / Mathf.Max(.001f, Visual.sprite.bounds.size.y);
            Visual.transform.localScale = new Vector3(scale, scale, 1);
            var bounds = Visual.sprite.bounds;
            Visual.transform.localPosition = new Vector3(-bounds.center.x * scale, -bounds.min.y * scale, 0);
        }

        void OnDestroy() { DisposeCustomVisual(); }
        void DisposeCustomVisual()
        {
            if (_customVisual)
            {
                if (Application.isPlaying) Destroy(_customVisual);
                else DestroyImmediate(_customVisual);
            }
            _customVisual = null;
            _visualPrefab = null;
        }
    }
}
