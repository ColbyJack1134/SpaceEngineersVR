using System;
using System.Collections.Generic;
using HarmonyLib;
using Havok;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using SpaceEngineersVR.Player.Control;
using SpaceEngineersVR.Plugin;
using VRage.Game;
using VRage.Game.Entity;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Climbing stays in the native ladder state, but the native ladder offset moves continuously instead of
    // in animated two-rung steps. Controlled ladder characters are not position-corrected by the host, and the
    // reported LadderUp/LadderDown state steps the host's own copy for other players.
    internal static class LadderClimb
    {
        internal const float Speed=.847f; // native: two rungs per 59-tick step
        private const float ExitSpeed=2*Speed; // the native top exit takes 2.8 s; this one about half that
        internal const double HeadLead=.2; // installed ladder clips hold the head this far toward the ladder
        private const double GrabReach=.07,HandSpeed=3,EndPush=.1,ExitClearance=.3,Tick=1/60.0;
        private const float WristDrop=.52f; // the wrist rests about 30 degrees below the rung

        private delegate MyLadder Probe(MyCharacterLadderComponent ladder,Vector3D position,ref Vector3 delta,out bool hit);
        private delegate MyLadder Middle(MyCharacterLadderComponent ladder,Vector3D position,ref Vector3 delta);
        private static readonly AccessTools.FieldRef<MyCharacterLadderComponent,Vector3> increment=AccessTools.FieldRefAccess<MyCharacterLadderComponent,Vector3>("m_ladderIncrementToBase");
        private static readonly AccessTools.FieldRef<MyCharacterLadderComponent,MatrixD> baseMatrix=AccessTools.FieldRefAccess<MyCharacterLadderComponent,MatrixD>("m_baseMatrix");
        private static readonly AccessTools.FieldRef<MyCharacterLadderComponent,int> nativeStep=AccessTools.FieldRefAccess<MyCharacterLadderComponent,int>("m_currentLadderStep");
        private static readonly AccessTools.FieldRef<MyCharacterLadderComponent,HkConstraint> constraint=AccessTools.FieldRefAccess<MyCharacterLadderComponent,HkConstraint>("m_constraintInstance");
        private static readonly Action<MyCharacterLadderComponent,MatrixD> setConstraint=AccessTools.MethodDelegate<Action<MyCharacterLadderComponent,MatrixD>>(
            AccessTools.Method(typeof(MyCharacterLadderComponent),"SetCharacterLadderConstraint"));
        private static readonly Probe top=AccessTools.MethodDelegate<Probe>(AccessTools.Method(typeof(MyCharacterLadderComponent),"CheckTopLadder"));
        private static readonly Probe bottom=AccessTools.MethodDelegate<Probe>(AccessTools.Method(typeof(MyCharacterLadderComponent),"CheckBottomLadder"));
        private static readonly Middle middle=AccessTools.MethodDelegate<Middle>(AccessTools.Method(typeof(MyCharacterLadderComponent),"CheckMiddleLadder"));
        private static readonly Action<MyCharacterLadderComponent,MyLadder,bool,MyObjectBuilder_Character.LadderInfo?> change=
            AccessTools.MethodDelegate<Action<MyCharacterLadderComponent,MyLadder,bool,MyObjectBuilder_Character.LadderInfo?>>(AccessTools.Method(typeof(MyCharacterLadderComponent),"ChangeLadder"));

        private struct Rungs { public double First,Depth,HalfWidth,Pitch; }
        private sealed class Grip
        {
            public readonly GripCapture Capture=new GripCapture();
            public MyLadder Ladder;
            public int Rung;
            public float Offset;
            public double Grabbed;
            public bool Driving;
            public Vector3D Reference;
            public bool Held => Ladder!=null;
            public void Drop() { Ladder=null; Driving=false; }
        }
        private static readonly Grip[] grips={new Grip(),new Grip()};
        private static readonly Dictionary<string,Rungs?> models=new Dictionary<string,Rungs?>();
        private static readonly List<MyEntity> nearby=new List<MyEntity>();
        private static Grip driver;
        private static MyCharacter owner;
        private static double pushed,mountUntil,exitRise,exitForward,exitDistance;
        private static bool exiting;

        private static Grip For(Controller hand) => grips[hand==Player.HandL ? 1:0];
        internal static bool Holding(Controller hand) => For(hand).Held && owner?.IsOnLadder==true;
        internal static double ViewLead(MyCharacter character)
        {
            if(!character.IsOnLadder) return 0;
            // The body walks forward over the landing; the lead ends at the release point.
            return exiting && exitRise<=0 && exitDistance>0 ? HeadLead*exitForward/exitDistance : HeadLead;
        }
        internal static void Reset()
        {
            foreach(var g in grips) { g.Capture.Release(); g.Drop(); }
            driver=null; owner=null; exiting=false; pushed=0; mountUntil=0;
        }

        internal static void Update(MyCharacter character,float stick)
        {
            if(owner!=character) { Reset(); owner=character; }
            double now=Multiplayer.MultiplayerRuntime.Now;
            UpdateGrips(character,now);
            var component=character.Components.Get<MyCharacterLadderComponent>();
            if(!character.IsOnLadder || component==null || character.Ladder==null || character.Physics?.CharacterProxy==null)
            {
                exiting=false; pushed=0;
                if(now>mountUntil) foreach(var g in grips) g.Drop();
                return;
            }
            mountUntil=0;
            var state=character.GetCurrentMovementState();
            if(nativeStep(component)>0 || state==MyCharacterMovementEnum.LadderOut) return;
            if(exiting) { Exit(component,character); return; }

            double requested=HandMove(character) ?? stick*Speed*Tick;
            if(Math.Abs(requested)<1e-6) { pushed=0; Report(character,0); return; }
            var up=character.WorldMatrix.Up;
            var position=character.PositionComp.GetPosition();
            Vector3 delta=up*requested;
            bool hit;
            bool open=requested>0 ? top(component,position,ref delta,out hit)!=null : bottom(component,position,ref delta,out hit)!=null;
            if(!open)
            {
                Report(character,0);
                // Like native climbing, a ceiling above the last rung blocks the exit.
                if(requested>0 && hit) { pushed=0; return; }
                pushed+=Math.Abs(requested);
                if(pushed<EndPush) return;
                pushed=0;
                if(requested>0) BeginExit(character);
                else { foreach(var g in grips) g.Drop(); character.GetOffLadder(); }
                return;
            }
            pushed=0;
            Move(component,character,requested,0);
            Report(character,requested);
        }

        // The most recent grab drives the body so that the gripping hand stays on its rung.
        private static double? HandMove(MyCharacter character)
        {
            if(driver==null || !driver.Held) return null;
            var hand=driver==grips[1] ? Player.HandL:Player.HandR;
            if(!TrackedArms.TryFreeBarGrip(hand,out var region)) return null;
            var grid=character.Ladder.CubeGrid.PositionComp;
            if(!driver.Driving)
            {
                driver.Reference=Vector3D.Transform(region.Start,grid.WorldMatrixNormalizedInv);
                driver.Driving=true;
                return 0;
            }
            double error=Vector3D.Dot(region.Start-Vector3D.Transform(driver.Reference,grid.WorldMatrixRef),character.WorldMatrix.Up);
            return MathHelper.Clamp(-error,-HandSpeed*Tick,HandSpeed*Tick);
        }

        private static void Move(MyCharacterLadderComponent component,MyCharacter character,double rise,double forward)
        {
            ref var offset=ref increment(component);
            offset.Y+=(float)rise; offset.Z+=(float)forward;
            var up=character.WorldMatrix.Up;
            var position=character.PositionComp.GetPosition();
            Vector3 delta=up*rise;
            var above=middle(component,position+up*.1,ref delta);
            var below=middle(component,position-up*.1,ref delta);
            if(above==below && below!=null && below!=character.Ladder) change(component,below,false,null);
            if(constraint(component)==null) return;
            MatrixD target=baseMatrix(component)*character.Ladder.WorldMatrix;
            target.Translation+=up*offset.Y+character.WorldMatrix.Forward*offset.Z;
            setConstraint(component,target);
        }

        private static void Report(MyCharacter character,double rise)
        {
            var state=character.GetCurrentMovementState();
            if(state!=MyCharacterMovementEnum.Ladder && state!=MyCharacterMovementEnum.LadderUp && state!=MyCharacterMovementEnum.LadderDown) return;
            character.SetCurrentMovementState(rise>0 ? MyCharacterMovementEnum.LadderUp : rise<0 ? MyCharacterMovementEnum.LadderDown : MyCharacterMovementEnum.Ladder);
        }

        // Over the top: rise until the feet clear the topmost ladder block, then move onto the native stop point.
        private static void BeginExit(MyCharacter character)
        {
            var ladder=character.Ladder;
            var up=character.WorldMatrix.Up;
            for(int i=0;i<64;i++)
            {
                var next=LadderAt(ladder.CubeGrid,ladder.PositionComp.WorldAABB.Center+up*(ladder.PositionComp.LocalAABB.HalfExtents.Y+.05));
                if(next==null || next==ladder || next.Orientation.Forward!=ladder.Orientation.Forward) break;
                ladder=next;
            }
            var feet=character.PositionComp.GetPosition();
            var world=ladder.PositionComp.WorldMatrixRef;
            var summit=ladder.PositionComp.WorldAABB.Center+up*Math.Abs(Vector3D.Dot(world.Up*ladder.PositionComp.LocalAABB.HalfExtents.Y,up));
            var stop=Vector3D.Transform(ladder.StopMatrix.Translation,world);
            exitRise=Math.Max(0,Vector3D.Dot(summit-feet,up)+ExitClearance);
            exitDistance=exitForward=Math.Max(0,Vector3D.Dot(stop-feet,character.WorldMatrix.Forward));
            exiting=true;
            foreach(var g in grips) g.Drop();
        }
        private static void Exit(MyCharacterLadderComponent component,MyCharacter character)
        {
            double step=ExitSpeed*Tick;
            if(exitRise>0) { double d=Math.Min(step,exitRise); exitRise-=d; Move(component,character,d,0); Report(character,d); return; }
            if(exitForward>0) { double d=Math.Min(step,exitForward); exitForward-=d; Move(component,character,0,d); return; }
            exiting=false;
            // Native release from LadderOut stands the character instead of starting a fall.
            character.SetCurrentMovementState(MyCharacterMovementEnum.LadderOut);
            character.GetOffLadder();
        }

        private static void UpdateGrips(MyCharacter character,double now)
        {
            var c=Controls.Static;
            bool allowed=Main.VrActive && InputRouter.Gameplay && !Main.MenuOpen && !ThirdPersonView.Active && !exiting &&
                MySession.Static?.ControlledEntity==character && !character.IsDead && character.CurrentWeapon==null &&
                (character.IsOnLadder || InputRouter.Mode==InputMode.Walking || InputRouter.Mode==InputMode.Jetpack);
            for(int i=0;i<2;i++)
            {
                var g=grips[i]; var hand=i==0 ? Player.HandR:Player.HandL;
                bool available=allowed && hand.pose.isTracked && !HelmetHud.Consumes(hand) && !HandInteraction.Owns(hand);
                float pressure=(i==0 ? c.RightGripPressure:c.LeftGripPressure).RawPosition.X;
                MyLadder ladder=null; int rung=0; float offset=0; bool near=false;
                bool down=pressure>.55f;
                if(available && down && !g.Held && TrackedArms.TryFreeBarGrip(hand,out var region))
                    near=Nearest(character,region.End,out ladder,out rung,out offset);
                if(g.Capture.Update(available,down,near,true))
                {
                    g.Ladder=ladder; g.Rung=rung; g.Offset=offset; g.Grabbed=now; g.Driving=false; driver=g;
                    CockpitFeedback.Activate(hand,.4f,.04f);
                    if(!character.IsOnLadder) { character.GetOnLadder(ladder); mountUntil=now+1; }
                }
                if(!g.Capture.Held && g.Held)
                {
                    g.Drop();
                    if(driver==g) { driver=grips[1-i].Held ? grips[1-i]:null; if(driver!=null) driver.Driving=false; }
                }
                if(g.Held && (g.Ladder.Closed || g.Ladder.MarkedForClose)) g.Drop();
                if(g.Capture.Consumed) InteractionInput.Read(hand,true).Consume();
            }
        }

        private static bool TryRungs(MyLadder ladder,out Rungs rungs)
        {
            rungs=default;
            var model=ladder.Model;
            if(model==null) return false;
            if(!models.TryGetValue(model.AssetName,out var cached))
            {
                cached=null;
                if(model.Dummies.TryGetValue("pole_1",out var pole) && ladder.DistanceBetweenPoles>.05f)
                    cached=new Rungs { First=pole.Matrix.Translation.Y,Depth=pole.Matrix.Translation.Z,HalfWidth=pole.Matrix.Right.Length()*.5,Pitch=ladder.DistanceBetweenPoles };
                models[model.AssetName]=cached;
            }
            if(!cached.HasValue) return false;
            rungs=cached.Value;
            return true;
        }
        private static MyLadder LadderAt(MyCubeGrid grid,Vector3D point) => grid.GetCubeBlock(grid.WorldToGridInteger(point))?.FatBlock as MyLadder;
        private static bool Nearest(MyCharacter character,Vector3D point,out MyLadder best,out int rung,out float offset)
        {
            best=null; rung=0; offset=0;
            double nearest=GrabReach;
            nearby.Clear();
            if(character.Ladder!=null) nearby.Add(character.Ladder.CubeGrid);
            else
            {
                var sphere=new BoundingSphereD(point,.35);
                MyGamePruningStructure.GetAllEntitiesInSphere(ref sphere,nearby);
            }
            foreach(var entity in nearby)
            {
                var candidate=entity as MyLadder ?? (entity is MyCubeGrid grid ? LadderAt(grid,point) : null);
                if(candidate==null || candidate.Closed || !candidate.IsFunctional || !TryRungs(candidate,out var r)) continue;
                var local=Vector3D.Transform(point,candidate.PositionComp.WorldMatrixNormalizedInv);
                int index=(int)Math.Round((local.Y-r.First)/r.Pitch);
                double y=r.First+index*r.Pitch;
                var box=candidate.PositionComp.LocalAABB;
                if(y<box.Min.Y || y>box.Max.Y) continue;
                double x=MathHelper.Clamp(local.X,-r.HalfWidth,r.HalfWidth);
                double distance=Vector3D.Distance(local,new Vector3D(x,y,r.Depth));
                if(distance>=nearest) continue;
                nearest=distance; best=candidate; rung=index; offset=(float)x;
            }
            nearby.Clear();
            return best!=null;
        }

        internal static bool TryPalm(Controller hand,out MatrixD palm,out float blend)
        {
            var g=For(hand);
            palm=MatrixD.Identity; blend=0;
            if(!Holding(hand) || !TryRungs(g.Ladder,out var r)) return false;
            palm=Palm(g.Ladder,r,g.Rung,g.Offset,owner.WorldMatrix.Up,hand==Player.HandL);
            blend=MathHelper.Clamp((float)(Multiplayer.MultiplayerRuntime.Now-g.Grabbed)/.12f,0,1);
            return true;
        }
        private static MatrixD Palm(MyLadder ladder,Rungs r,int rung,float offset,Vector3D characterUp,bool left)
        {
            var world=ladder.PositionComp.WorldMatrixRef;
            Vector3D up=Vector3D.Dot(world.Up,characterUp)<0 ? world.Down : world.Up;
            // The native mount places climbers on the Forward side of the rungs.
            Vector3D toward=world.Forward;
            float cos=(float)Math.Cos(WristDrop),sin=(float)Math.Sin(WristDrop);
            var frame=MatrixD.Identity;
            frame.Up=up*cos+toward*sin;
            frame.Backward=toward*cos-up*sin;
            frame.Right=Vector3D.Cross(frame.Up,frame.Backward);
            frame.Translation=Vector3D.Transform(new Vector3D(offset,r.First+rung*r.Pitch,r.Depth),world);
            return (MatrixD)CockpitStickMath.GripPalm(left,Vector3.Zero,left ? Vector3.Right:Vector3.Left)*frame;
        }
    }
}
