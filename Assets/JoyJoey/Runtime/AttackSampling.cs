using System.Collections.Generic;
using UnityEngine;

namespace JoyJoey.Sandbox
{
    public static class AttackSampling
    {
        public static float AnimationTime(MoveDefinition move, float frame) => move == null || move.AnimationClip == null ? 0 :
            Mathf.Clamp(frame * CombatSimulation.Delta * Mathf.Max(.01f,move.AnimationSpeed),0,move.AnimationClip.length);
        public static void SampleVisual(MoveDefinition move,GameObject visual,float frame)
        {
            if(move==null || visual==null || move.AnimationClip==null)return;
            move.AnimationClip.SampleAnimation(visual,AnimationTime(move,frame));
            // Root motion belongs to the combat motor; animation drives only the visual hierarchy.
            visual.transform.localPosition=Vector3.zero;
            visual.transform.localRotation=Quaternion.identity;
            visual.transform.localScale=move.PreviewPrefab?move.PreviewPrefab.transform.localScale:Vector3.one;
        }
        public static Rect Box(List<HitboxKeyframe> keys, float frame)
        {
            if (keys == null || keys.Count == 0) return new Rect(.1f, 0, 1.8f, 1.8f);
            HitboxKeyframe a = null, b = null;
            foreach (var key in keys)
            {
                if (key == null) continue;
                if (key.Frame <= frame && (a == null || key.Frame > a.Frame)) a = key;
                if (key.Frame >= frame && (b == null || key.Frame < b.Frame)) b = key;
            }
            a = a ?? b; b = b ?? a;
            if(a == null) return new Rect();
            float t = a.Frame == b.Frame ? 0 : Mathf.InverseLerp(a.Frame,b.Frame,frame);
            Vector2 size = Vector2.Max(Vector2.one * .02f, Vector2.Lerp(a.Size,b.Size,t));
            return new Rect(Vector2.Lerp(a.Offset,b.Offset,t)-size*.5f,size);
        }
        public static PoseKeyframe Pose(List<PoseKeyframe> keys, float frame)
        {
            if(keys == null || keys.Count == 0) return null;
            PoseKeyframe a=null,b=null;
            foreach(var key in keys)
            {
                if(key == null) continue;
                if(key.Frame<=frame && (a==null || key.Frame>a.Frame)) a=key;
                if(key.Frame>=frame && (b==null || key.Frame<b.Frame)) b=key;
            }
            a=a??b;b=b??a;if(a==null)return null;
            float t=a.Frame==b.Frame?0:Mathf.InverseLerp(a.Frame,b.Frame,frame);
            return new PoseKeyframe{Frame=Mathf.FloorToInt(frame),BodyOffset=Vector2.Lerp(a.BodyOffset,b.BodyOffset,t),
                BodyAngle=Mathf.LerpAngle(a.BodyAngle,b.BodyAngle,t),FrontArmAngle=Mathf.LerpAngle(a.FrontArmAngle,b.FrontArmAngle,t),
                RearArmAngle=Mathf.LerpAngle(a.RearArmAngle,b.RearArmAngle,t),FrontLegAngle=Mathf.LerpAngle(a.FrontLegAngle,b.FrontLegAngle,t),
                RearLegAngle=Mathf.LerpAngle(a.RearLegAngle,b.RearLegAngle,t)};
        }
    }
}
