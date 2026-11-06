using UnityEngine;

namespace JoyJoey.Sandbox
{
    /// <summary>Disposable placeholder puppet; the combat model does not depend on this rig.</summary>
    internal sealed class FighterView
    {
        readonly Transform _root, _rig;
        readonly SpriteRenderer _body, _head, _hair, _arm, _rearArm, _frontLeg, _rearLeg, _face, _shadow, _rag, _hat;
        readonly SpriteRenderer[] _weapon;
        readonly bool _dummy;
        float _flashUntil;
        GameObject _customVisual, _visualPrefab;

        public FighterView(Transform parent, string name, bool dummy)
        {
            _dummy = dummy;
            _root = new GameObject(name + " visual").transform;
            _root.SetParent(parent, false);
            _shadow = SandboxRunner.RectSprite(parent, name + " shadow", Vector2.zero, new Vector2(.95f, .15f), new Color(0, 0, 0, .3f), 4); _shadow.sprite = SandboxRunner.Disc;
            _rig = new GameObject("Pose rig").transform; _rig.SetParent(_root, false);
            _rearLeg = Part("Rear leg", new Vector2(-.17f, .25f), new Vector2(.21f, .50f), 8);
            _frontLeg = Part("Front leg", new Vector2(.17f, .25f), new Vector2(.23f, .50f), 10);
            _rearArm = Part("Rear arm", new Vector2(-.37f, .87f), new Vector2(.20f, .6f), 8);
            _body = Part("Torso", new Vector2(0, .89f), new Vector2(.66f, .72f), 10);
            _head = Part("Head", new Vector2(0, 1.43f), new Vector2(.53f, .53f), 11); _head.sprite = SandboxRunner.Disc;
            _hair = Part("Balloon hair", new Vector2(-.06f, 1.66f), new Vector2(.64f, .22f), 12);
            _face = Part("Facing eye", new Vector2(.16f, 1.46f), new Vector2(.06f, .09f), 13);
            _arm = Part("Front arm", new Vector2(.34f, 1), new Vector2(.20f, .65f), 12);
            _hat = Part("Costume crown", new Vector2(0, 1.87f), new Vector2(.55f, .12f), 14);
            _rag = Part("Rag companion", new Vector2(-.72f, 1.45f), new Vector2(.32f, .38f), 12); _rag.sprite = SandboxRunner.Disc;
            _weapon = new SpriteRenderer[14];
            for (int i = 0; i < _weapon.Length; i++) _weapon[i] = Part("Attack arc", Vector2.zero, Vector2.one * .06f, 15);
            if (dummy)
            {
                _hair.transform.localScale = new Vector3(.5f, .07f, 1);
                Part("Dummy target core", new Vector2(0, .97f), new Vector2(.25f, .25f), 11).color = new Color(.15f, .12f, .15f);
            }
        }

        SpriteRenderer Part(string name, Vector2 position, Vector2 size, int order) => SandboxRunner.RectSprite(_rig, name, position, size, Color.white, order);
        public void SetRagVisible(bool visible) { _rag.enabled = visible; }
        public void Flash() { _flashUntil = Time.unscaledTime + .10f; }

        public void Update(FighterState actor, Color color, Color ragColor, bool transformed, float time, int hitstop,bool legacyFallback=false)
        {
            _root.position = actor.Position;
            _root.localScale = new Vector3(actor.Facing, 1, 1);
            bool hit = Time.unscaledTime < _flashUntil;
            bool attacking = actor.Move != null;
            bool active = attacking && actor.Move.IsActive(actor.MoveTick) &&
                (actor.Move.Hitboxes!=null && actor.Move.Hitboxes.Count>0 || legacyFallback && actor.Move.Damage>0);
            float attack = attacking ? Mathf.Clamp01(actor.MoveTick / (float)Mathf.Max(1, actor.Move.Startup)) : 0;
            float walk = actor.Grounded && !attacking ? Mathf.Sin(time * 18) * Mathf.Clamp01(Mathf.Abs(actor.Velocity.x) / 6) : 0;
            float lean = actor.Hitstun > 0 ? Mathf.Clamp(actor.Velocity.x * -2, -24, 24) : actor.DashTicks > 0 ? -25 : attacking ? -12 * Mathf.Sin(attack * Mathf.PI * .7f) : -actor.Velocity.x * .45f;
            _rig.localRotation = Quaternion.Euler(0, 0, lean);
            _rig.localPosition = new Vector3(0, actor.DashTicks > 0 ? -.16f : actor.Grounded ? Mathf.Abs(walk) * .035f : 0, 0);
            Color shade = Color.Lerp(color, SandboxRunner.Ink, .25f);
            _body.color = hit ? Color.white : color;
            _head.color = hit ? Color.white : _dummy ? new Color(.9f, .63f, .49f) : new Color(1, .83f, .69f);
            _hair.color = hit ? Color.white : _dummy ? shade : color;
            _arm.color = hit ? Color.white : color;
            _rearArm.color = shade;
            _frontLeg.color = hit ? Color.white : shade;
            _rearLeg.color = Color.Lerp(shade, SandboxRunner.Ink, .3f);
            _face.color = SandboxRunner.Ink;
            _hat.enabled = transformed;
            _hat.color = ragColor;
            _rag.color = ragColor;
            _rag.transform.localPosition = new Vector3(-.72f, 1.45f + Mathf.Sin(time * 4) * .10f, 0);
            _frontLeg.transform.localRotation = Quaternion.Euler(0, 0, actor.Grounded ? walk * 24 : -23);
            _rearLeg.transform.localRotation = Quaternion.Euler(0, 0, actor.Grounded ? -walk * 24 : 27);
            _rearArm.transform.localRotation = Quaternion.Euler(0, 0, actor.Grounded ? walk * 20 : -32);
            _arm.transform.localPosition = new Vector3(attacking ? .55f : .34f, attacking ? 1.10f : 1f, 0);
            _arm.transform.localRotation = Quaternion.Euler(0, 0, attacking ? 70 + 35 * attack : actor.DashTicks > 0 ? -60 : -walk * 22);
            if(attacking)
            {
                var pose=actor.Move.SamplePose(actor.MoveTick);
                if(pose!=null)
                {
                    _rig.localPosition=pose.BodyOffset;
                    _rig.localRotation=Quaternion.Euler(0,0,pose.BodyAngle);
                    _arm.transform.localRotation=Quaternion.Euler(0,0,pose.FrontArmAngle);
                    _rearArm.transform.localRotation=Quaternion.Euler(0,0,pose.RearArmAngle);
                    _frontLeg.transform.localRotation=Quaternion.Euler(0,0,pose.FrontLegAngle);
                    _rearLeg.transform.localRotation=Quaternion.Euler(0,0,pose.RearLegAngle);
                }
            }
            var prefab=attacking?actor.Move.PreviewPrefab:null;
            if(prefab!=_visualPrefab)
            {
                if(_customVisual)Object.Destroy(_customVisual);
                _visualPrefab=prefab;
                _customVisual=prefab?Object.Instantiate(prefab,_root):null;
                if(_customVisual)
                {
                    _customVisual.transform.localPosition=Vector3.zero;_customVisual.transform.localRotation=Quaternion.identity;
                    foreach(var animator in _customVisual.GetComponentsInChildren<Animator>(true))animator.enabled=false;
                    foreach(var animation in _customVisual.GetComponentsInChildren<Animation>(true))animation.enabled=false;
                }
            }
            _rig.gameObject.SetActive(!_customVisual);
            if(attacking && actor.Move.AnimationClip && _customVisual)
            {
                AttackSampling.SampleVisual(actor.Move,_customVisual,actor.MoveTick);
                _root.position=actor.Position;_root.localScale=new Vector3(actor.Facing,1,1);
                _root.rotation=Quaternion.identity;
            }
            _shadow.transform.position = new Vector3(actor.Position.x, actor.Grounded ? actor.Position.y - .015f : -.015f, 0);
            _shadow.transform.localScale = new Vector3(Mathf.Lerp(.95f, .45f, actor.Position.y / 8), .14f, 1);
            _shadow.color = new Color(0, 0, 0, Mathf.Lerp(.3f, .05f, actor.Position.y / 8));

            for (int i = 0; i < _weapon.Length; i++)
            {
                _weapon[i].enabled = active;
                if (!active) continue;
                Rect box=new Rect(actor.Move.HitboxOffset-actor.Move.HitboxSize*.5f,actor.Move.HitboxSize);
                if(actor.Move.Hitboxes!=null)
                {
                    foreach(var track in actor.Move.Hitboxes)
                        if(track!=null && track.IsActive(actor.MoveTick)){box=track.Sample(actor.MoveTick);break;}
                }
                Vector2 offset = box.center;
                Vector2 size = box.size;
                float phase = i / (float)(_weapon.Length - 1);
                float angle = Mathf.Lerp(-80, 85, phase) * Mathf.Deg2Rad;
                Vector2 point = offset + new Vector2(Mathf.Cos(angle) * size.x * .42f, Mathf.Sin(angle) * size.y * .45f);
                _weapon[i].transform.localPosition = point;
                _weapon[i].transform.localScale = new Vector3(.055f + Mathf.Sin(phase * Mathf.PI) * .035f, .13f, 1);
                _weapon[i].transform.localRotation = Quaternion.Euler(0, 0, -angle * Mathf.Rad2Deg);
                _weapon[i].color = Color.Lerp(color, Color.white, .65f);
            }
        }
    }
}
