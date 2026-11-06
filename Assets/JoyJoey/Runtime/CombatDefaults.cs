using System.Collections.Generic;
using UnityEngine;

namespace JoyJoey.Sandbox
{
    /// <summary>Authoring examples at 60 Hz. Geometry and motion create continuations; explicit cancels are exceptional.</summary>
    public static class CombatDefaults
    {
        public static CombatCatalog CreateCatalog()
        {
            var c = ScriptableObject.CreateInstance<CombatCatalog>();
            c.name = "Sandbox Combat Catalog";
            c.SchemaVersion = CatalogMigration.CurrentVersion;

            Add(c,"jab1","Balloon Cuts I",ActionId.Basic,5,3,0,5,V(1.2f,.9f),12,2);
            Add(c,"jab2","Balloon Cuts II",ActionId.Basic,5,3,0,5,V(1.5f,1.5f),13,2);
            var jabEnd=Add(c,"jab3","Balloon Cuts · Cierre",ActionId.Basic,7,4,3,10,V(8,-5),18,4);
            jabEnd.Finisher=true;
            var slash=Add(c,"slash","Balloon Slash",ActionId.Basic,6,4,0,8,V(3.4f,3),14,3);
            slash.Impulse=V(5,0); Shape(slash,V(1,.95f),V(2.5f,1.9f));
            var launcher=Add(c,"launcher","Pop Launch",ActionId.Basic,6,4,0,7,V(1.2f,11),26,3);
            launcher.Impulse=V(.3f,0);launcher.FollowOnHold=true;launcher.FollowFrame=6;launcher.FollowImpulse=V(0,13);
            Shape(launcher,V(.9f,1.15f),V(2.3f,2.6f));
            launcher.Hitboxes[0].Hit.EnemyVelocityMode=VelocityMode.Add;
            launcher.Hitboxes[0].Hit.JugglePower=18;launcher.Hitboxes[0].Hit.ArmorDamage=18;
            launcher.Poses[1].FrontArmAngle=145;launcher.Poses[1].BodyAngle=13;

            Add(c,"air1","Balloon Air-Raid I",ActionId.Basic,4,3,0,4,V(1.1f,5.6f),15,2);
            Add(c,"air2","Balloon Air-Raid II",ActionId.Basic,5,3,0,5,V(1.6f,6),16,2);
            var airEnd=Add(c,"air3","Balloon Air-Raid · Cierre",ActionId.Basic,7,4,2,9,V(9,-8),18,4);
            airEnd.Finisher=true;
            var dive=Add(c,"dive","Pop Drop",ActionId.Basic,5,10,0,9,V(2,12),22,4);
            dive.Impulse=V(0,-15);dive.VerticalMomentumRetention=.1f;Shape(dive,V(.4f,.2f),V(1.8f,1.8f));
            dive.Hitboxes[0].Hit.AttackerImpulse=V(1,10);dive.Hitboxes[0].Hit.AttackerVelocityMode=VelocityMode.Override;

            var strong=Add(c,"ragStrong","Titanenklatsch",ActionId.Rag,11,4,0,14,V(4.5f,7),20,4);
            strong.Impulse=V(2,0);Shape(strong,V(1.2f,1),V(2.8f,2));
            strong.Hitboxes[0].Hit.JugglePower=25;strong.Hitboxes[0].Hit.ArmorDamage=25;
            var strongCharged=Add(c,"ragStrongCharged","Titanenklatsch cargado",ActionId.Rag,19,5,3,24,V(7,13),28,6);
            strongCharged.Impulse=V(2.5f,0);Shape(strongCharged,V(1.25f,1.2f),V(3.1f,2.6f));
            strongCharged.Hitboxes[0].Hit.JugglePower=40;strongCharged.Hitboxes[0].Hit.ArmorDamage=40;

            var jester=Add(c,"ragJester","Jester Rattle I",ActionId.Rag,5,6,0,2.5f,V(.7f,4.5f),13,1);
            jester.Rehit=4;jester.Hitboxes[0].Hit.Rehit=4;
            jester.Hitboxes[0].Hit.JugglePower=6;jester.Hitboxes[0].Hit.ArmorDamage=4;
            var jester2=Add(c,"ragJester2","Jester Rattle II",ActionId.Rag,6,4,0,5,V(1.1f,5.5f),14,2);
            jester2.Hitboxes[0].Hit.JugglePower=8;jester2.Hitboxes[0].Hit.ArmorDamage=6;
            var jesterEnd=Add(c,"ragJesterEnd","Jester Rattle · Cierre",ActionId.Rag,8,4,2,9,V(9,-7),18,4);
            jesterEnd.Finisher=true;
            var encore=Add(c,"ragJesterCharged","Jester Encore · Cierre",ActionId.Rag,9,18,0,2.5f,V(.8f,4),14,1);
            encore.Finisher=true;encore.Hitboxes[0].EndFrame=21;encore.Hitboxes[0].Hit.Rehit=5;
            encore.Hitboxes[0].Hit.JugglePower=6;encore.Hitboxes[0].Hit.ArmorDamage=5;
            encore.Hitboxes.Add(new HitboxTrack{Name="Remate",StartFrame=23,EndFrame=27,HitGroup=1,
                Hit=new HitProperties{Damage=8,EnemyImpulse=V(9,-8),Hitstun=18,Hitstop=4,JugglePower=15,ArmorDamage=13},
                Keys=new List<HitboxKeyframe>{new HitboxKeyframe{Frame=23,Offset=V(1.1f,1),Size=V(2.4f,2)},new HitboxKeyframe{Frame=26,Offset=V(1.35f,.6f),Size=V(2.3f,2)}}});

            var illusion=Add(c,"ragIllusion","Curtain Call",ActionId.Rag,7,3,0,7,V(1.8f,7),18,3);
            illusion.Impulse=V(2,1);illusion.BlinkToTarget=true;illusion.BlinkRange=4;Shape(illusion,V(1,1.05f),V(2.4f,2.1f));
            var vanish=Add(c,"ragIllusionCharged","Vanishing Act",ActionId.Rag,14,4,0,13,V(3,10),23,4);
            vanish.BlinkToTarget=true;vanish.BlinkRange=5;Shape(vanish,V(1,1.1f),V(2.6f,2.5f));
            var suitStrong=Add(c,"suitStrong","Gigatitanenklatsch",ActionId.Basic,10,5,0,19,V(6,8),22,5);
            suitStrong.Impulse=V(1.5f,0);Shape(suitStrong,V(1.3f,1),V(3.1f,2.1f));
            suitStrong.Hitboxes[0].Hit.JugglePower=30;suitStrong.Hitboxes[0].Hit.ArmorDamage=30;
            var suitJester=Add(c,"suitJester","Jester Crescendo",ActionId.Basic,5,8,0,3.5f,V(1,5),14,1);
            suitJester.Rehit=5;suitJester.Hitboxes[0].Hit.Rehit=5;
            var suitIllusion=Add(c,"suitIllusion","Mirror Cut",ActionId.Basic,5,3,0,9,V(2,6),17,3);
            suitIllusion.Impulse=V(3,0);Shape(suitIllusion,V(1,1),V(2.5f,1.9f));

            var bound=Add(c,"trickStrong","Strongman Bound",ActionId.Trick,3,3,0,4,V(1,8),15,2);
            bound.Impulse=V(1.5f,12);Shape(bound,V(.65f,.8f),V(1.8f,2.1f));
            var step=Add(c,"trickJester","Jester Step",ActionId.Trick,2,3,0,3,V(1,5),13,1);
            step.Impulse=V(5,8);
            var slip=Add(c,"trickIllusion","Slip Through",ActionId.Trick,2,2,0,0,V(0,0),0,0);
            slip.Impulse=V(0,2);slip.BlinkToTarget=true;slip.BlinkRange=5;slip.Hitboxes[0].Hit.Correction=false;
            slip.Hitboxes.Clear();

            // A modest ranged tool: lifts light targets, weak against armor, and only slows the player's fall.
            var shot=Add(c,"airShot","Balloon Shot",ActionId.Rag,4,2,2,2,V(.3f,5),9,1);
            shot.VerticalMomentumRetention=.8f;shot.GravityScale=.75f;shot.Impulse=Vector2.zero;
            Shape(shot,V(3.2f,1.1f),V(6.4f,1.4f));
            var shotHit=shot.Hitboxes[0].Hit;shotHit.EnemyVelocityMode=VelocityMode.Add;shotHit.JugglePower=6;
            shotHit.ArmorDamage=2;shotHit.Correction=false;
            var diagonal=Add(c,"heavyDiagonal","Strongman Diagonal",ActionId.Rag,16,5,2,16,V(6,-8),23,4);
            diagonal.Finisher=true;diagonal.Impulse=V(4,-3);diagonal.VerticalMomentumRetention=.7f;
            Shape(diagonal,V(1.7f,.1f),V(2.7f,2.6f));
            diagonal.Hitboxes[0].Keys[1].Offset=V(2.15f,-.8f);
            diagonal.Hitboxes[0].Hit.JugglePower=30;diagonal.Hitboxes[0].Hit.ArmorDamage=28;

            AddBindings(c);
            return c;
        }

        public static void AddBindings(CombatCatalog c)
        {
            Bind(c,"Básicos suelo",ActionId.Basic,MoveContext.Ground,MoveDirection.Neutral,MoveStance.Normal,null,10,"jab1","jab2","jab3");
            Bind(c,"Básico avanzando",ActionId.Basic,MoveContext.Ground,MoveDirection.Horizontal,MoveStance.Normal,null,10,"slash");
            Bind(c,"Lanzador abajo",ActionId.Basic,MoveContext.Ground,MoveDirection.Down,MoveStance.Normal,null,20,"launcher");
            Bind(c,"Lanzador arriba",ActionId.Basic,MoveContext.Ground,MoveDirection.Up,MoveStance.Normal,null,20,"launcher");
            Bind(c,"Básicos aire",ActionId.Basic,MoveContext.Air,MoveDirection.Any,MoveStance.Normal,null,5,"air1","air2","air3");
            Bind(c,"Picado",ActionId.Basic,MoveContext.Air,MoveDirection.Down,MoveStance.Normal,null,20,"dive");
            Bind(c,"Traje Strongman",ActionId.Basic,MoveContext.Any,MoveDirection.Any,MoveStance.Transformed,RagStyle.Strongman,30,"suitStrong");
            Bind(c,"Traje Jester",ActionId.Basic,MoveContext.Any,MoveDirection.Any,MoveStance.Transformed,RagStyle.Jester,30,"suitJester");
            Bind(c,"Traje Ilusionista",ActionId.Basic,MoveContext.Any,MoveDirection.Any,MoveStance.Transformed,RagStyle.Illusionist,30,"suitIllusion");
            Bind(c,"Rag Strongman",ActionId.Rag,MoveContext.Any,MoveDirection.Any,MoveStance.Any,RagStyle.Strongman,0,"ragStrong").ChargedMoveId="ragStrongCharged";
            Bind(c,"Rag Jester",ActionId.Rag,MoveContext.Any,MoveDirection.Any,MoveStance.Any,RagStyle.Jester,0,"ragJester","ragJester2","ragJesterEnd").ChargedMoveId="ragJesterCharged";
            Bind(c,"Rag Ilusionista",ActionId.Rag,MoveContext.Any,MoveDirection.Any,MoveStance.Any,RagStyle.Illusionist,0,"ragIllusion").ChargedMoveId="ragIllusionCharged";
            Bind(c,"Disparo aéreo arriba",ActionId.Rag,MoveContext.Air,MoveDirection.Up,MoveStance.Any,null,40,"airShot");
            Bind(c,"Diagonal fuerte abajo",ActionId.Rag,MoveContext.Air,MoveDirection.Down,MoveStance.Any,RagStyle.Strongman,40,"heavyDiagonal");
            Bind(c,"Trick Strongman",ActionId.Trick,MoveContext.Any,MoveDirection.Any,MoveStance.Any,RagStyle.Strongman,0,"trickStrong");
            Bind(c,"Trick Jester",ActionId.Trick,MoveContext.Any,MoveDirection.Any,MoveStance.Any,RagStyle.Jester,0,"trickJester");
            Bind(c,"Trick Ilusionista",ActionId.Trick,MoveContext.Any,MoveDirection.Any,MoveStance.Any,RagStyle.Illusionist,0,"trickIllusion");
        }

        static MoveBinding Bind(CombatCatalog c,string name,ActionId action,MoveContext context,MoveDirection direction,MoveStance stance,RagStyle? rag,int priority,params string[] sequence)
        {
            var b=new MoveBinding{Name=name,Action=action,Context=context,Direction=direction,Stance=stance,AnyRag=!rag.HasValue,
                Rag=rag??RagStyle.Strongman,Priority=priority,SequenceTimeout=32,Sequence=new List<string>(sequence)};
            c.Bindings.Add(b);return b;
        }

        public static MotorTuning CreateTuning()
        { var tuning=ScriptableObject.CreateInstance<MotorTuning>();tuning.name="Sandbox Motor Tuning";return tuning; }
        public static EnemyProfile CreateLightEnemy()
        { var p=ScriptableObject.CreateInstance<EnemyProfile>();p.name="Dummy Light";p.DisplayName="Ligero";return p; }
        public static EnemyProfile CreateHeavyEnemy()
        {
            var p=ScriptableObject.CreateInstance<EnemyProfile>();p.name="Dummy Heavy";p.DisplayName="Pesado";
            p.GroundArmor=30;p.ArmorRegenPerSecond=12;p.ArmorRegenDelay=45;p.AirJuggleThreshold=18;
            p.JuggleWindow=18;p.CorrectionWeight=8;p.LaunchVelocityScale=.9f;return p;
        }

        static MoveDefinition Add(CombatCatalog c,string id,string title,ActionId action,int startup,int active,int recovery,float damage,Vector2 launch,int hitstun,int hitstop)
        {
            var m=new MoveDefinition{Id=id,Title=title,Action=action,Startup=startup,Active=active,Recovery=recovery,Damage=damage,
                Launch=launch,Hitstun=hitstun,Hitstop=hitstop,CancelOpen=startup,CancelClose=Mathf.Max(startup,startup+active+recovery-1),
                Cancels=CancelActions.None,MomentumRetention=.2f,VerticalMomentumRetention=.2f};
            m.Hitboxes.Add(new HitboxTrack{Name="Impacto",StartFrame=startup,EndFrame=startup+active,HitGroup=0,
                Hit=new HitProperties{Damage=damage,EnemyImpulse=launch,Hitstun=hitstun,Hitstop=hitstop,JugglePower=Mathf.Max(8,damage*1.5f),ArmorDamage=Mathf.Max(6,damage*1.4f)},
                Keys=new List<HitboxKeyframe>{new HitboxKeyframe{Frame=startup,Offset=V(.9f,.9f),Size=V(2,1.8f)},
                    new HitboxKeyframe{Frame=startup+active-1,Offset=V(1.1f,.95f),Size=V(2,1.8f)}}});
            m.Poses.Add(new PoseKeyframe{Frame=0,BodyAngle=7,FrontArmAngle=-25,RearArmAngle=15});
            m.Poses.Add(new PoseKeyframe{Frame=startup,BodyAngle=-10,FrontArmAngle=100,RearArmAngle=-20,FrontLegAngle=-8,RearLegAngle=12});
            m.Poses.Add(new PoseKeyframe{Frame=m.Total-1,BodyAngle=-3,FrontArmAngle=35,RearArmAngle=0});
            c.Moves.Add(m);return m;
        }
        static void Shape(MoveDefinition m,Vector2 centre,Vector2 size)
        {
            m.HitboxOffset=centre;m.HitboxSize=size;
            m.Hitboxes[0].Keys[0].Offset=centre-V(.1f,0);m.Hitboxes[0].Keys[0].Size=size;
            m.Hitboxes[0].Keys[1].Offset=centre+V(.1f,0);m.Hitboxes[0].Keys[1].Size=size;
        }
        static Vector2 V(float x,float y)=>new Vector2(x,y);
    }
}
