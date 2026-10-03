using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using HarmonyLib;
using Sandbox.Game.GUI.HudViewers;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class MarkerTests
    {
        private static void Near(double actual,double expected,string name,double tolerance=1e-7)
        {
            if (!actual.IsValid() || Math.Abs(actual-expected)>tolerance) throw new Exception(name+": "+actual+" != "+expected);
        }
        private static double Angle(Vector3D a,Vector3D b) => Math.Acos(MathHelper.Clamp(Vector3D.Dot(Vector3D.Normalize(a),Vector3D.Normalize(b)),-1,1));

        public static void Run(Action<string> log)
        {
            Crosshair(log);
            Lead(log);
            Motion(log);
            int count=0;
            foreach (double roll in new[] { 0.0,0.5,-0.8 })
            foreach (double yaw in new[] { -1.15,-0.6,0,0.6,1.15 })
            foreach (double pitch in new[] { -0.55,0.0,0.55 })
            foreach (double distance in new[] { 2.0,200.0,1000000.0 })
            {
                var head=MatrixD.CreateRotationZ(roll);
                head.Translation=new Vector3D(1000000,2000000,3000000);
                var direction=Vector3D.TransformNormal(new Vector3D(Math.Sin(yaw)*Math.Cos(pitch),Math.Sin(pitch),-Math.Cos(yaw)*Math.Cos(pitch)),head);
                var position=head.Translation+direction*distance;
                if (!MarkerBillboard.TryCreate(position,head,out var board)) throw new Exception("Valid marker rejected");
                var normal=Vector3D.Cross(board.Right,board.Up);
                Near(Vector3D.Dot(normal,Vector3D.Normalize(head.Backward)),1,"Peripheral billboard is not view aligned");
                Near(board.Right.Length(),1,"Billboard right basis"); Near(board.Up.Length(),1,"Billboard up basis");
                Near(Vector3D.Dot(board.Right,board.Up),0,"Billboard sheared");
                foreach (double eye in new[] { -0.032,0.032 })
                {
                    var view=MatrixD.Invert(MatrixD.CreateTranslation(eye,0,0)*head);
                    var projection=VrMath.Projection(-2.5f,2.8f,-1.8f,2.1f,.05);
                    var sprite=new NativeSprite(null,default(RectangleF),Vector4.One);
                    if (!board.Project(new RectangleF(-.5f,-.5f,1,1),view,projection,ref sprite) || !sprite.Projected)
                        throw new Exception("Billboard lost projected-quad path");
                    var actual=Vector4D.Transform((Vector4D)sprite.TopLeft,MatrixD.Invert(projection));
                    var recovered=Vector3D.Transform(new Vector3D(actual.X,actual.Y,actual.Z)/actual.W,MatrixD.Invert(view));
                    Near((recovered-board.Point(-.5,-.5)).Length()/distance,0,"Per-eye corner reprojection",2e-6);
                    Near(sprite.TopLeft.Y/sprite.TopLeft.W,sprite.TopRight.Y/sprite.TopRight.W,"Peripheral horizontal edge tilted",2e-6);
                    Near(sprite.TopLeft.X/sprite.TopLeft.W,sprite.BottomLeft.X/sprite.BottomLeft.W,"Peripheral vertical edge tilted",2e-6);
                }
                count++;
            }
            if (MarkerBillboard.TryCreate(Vector3D.Zero,MatrixD.Identity,out _) ||
                MarkerBillboard.TryCreate(new Vector3D(double.NaN,0,-1),MatrixD.Identity,out _)) throw new Exception("Invalid marker accepted");
            if (!MarkerBillboard.TryCreate(Vector3D.Up,MatrixD.Identity,out var pole) || !pole.Right.IsValid() || !pole.Up.IsValid())
                throw new Exception("Vertical marker billboard singularity");
            log("PASS marker billboards: "+count+" peripheral/tilted/distant poses, view-aligned edges, shared stereo corners and world depth");
        }

        private static void Crosshair(Action<string> log)
        {
            var native=new Sandbox.Game.Gui.MyHudCrosshair();
            var ship=MatrixD.CreateRotationY(.4); ship.Translation=new Vector3D(1e12,-2e12,3e12);
            var captured=ShipCrosshair.Read(native,ship);
            Near(Vector3D.Distance(captured.Position,ship.Translation+ship.Forward*1000),0,"Native ship aim");
            native.HideDefaultSprite();
            if(ShipCrosshair.Read(native,ship)!=null) throw new Exception("Hidden native crosshair retained");
            if(captured.Icon!=VRage.Game.Gui.MyHudTexturesEnum.crosshair) throw new Exception("Native crosshair snapshot mutated");
            native.ResetToDefault();
            native.ChangeDefaultSprite(VRage.Game.Gui.MyHudTexturesEnum.Target_enemy,.01f);
            if(ShipCrosshair.Read(native,ship).Icon!=VRage.Game.Gui.MyHudTexturesEnum.Target_enemy) throw new Exception("Native tool crosshair ignored");
            var config=new SpaceEngineersVR.Config.PluginConfig(); config.ShowVitals=true; config.WaypointMode=0;
            if(!ShipCrosshair.Enabled(config,true) || ShipCrosshair.Enabled(config,false)) throw new Exception("Crosshair depends on waypoint mode or ignores visor");
            config.ShowVitals=false;
            if(ShipCrosshair.Enabled(config,true)) throw new Exception("HUD Off retained crosshair");
            config.WaypointMode=2; config.ShipCrosshair=false;
            if(ShipCrosshair.Enabled(config,true)) throw new Exception("Crosshair toggle ignored");
            foreach(double yaw in new[] {0,.25,-.3})
            {
                var head=MatrixD.CreateRotationY(yaw)*ship;
                head.Translation+=ship.Up*4+ship.Backward*12;
                foreach(double eye in new[] {-.032,.032})
                {
                    var view=MatrixD.Invert(MatrixD.CreateTranslation(eye,0,0)*head);
                    var projection=VrMath.Projection(-1,1,-1,1,.05);
                    if(!WorldMarkers.Project(captured.Position,view,projection,out var point)) throw new Exception("Ship aim disappeared when looking aside");
                    var local=Vector3D.Transform(captured.Position,view);
                    Near(point.X,.5+local.X/-local.Z*.5,"World ship aim follows head",1e-6);
                    if(yaw!=0 && Math.Abs(point.X-.5)<.01) throw new Exception("Ship aim stuck to face");
                }
            }
            log("PASS ship crosshair: native default/tool/hidden sprites, independent waypoint setting, HUD Off/visor gates, stereo ship direction under head turns and third-person offsets at large coordinates.");
        }

        internal static Vector3D Predict(Vector3D target,Vector3D velocity,Vector3D shooterVelocity=default(Vector3D)) =>
            (Vector3D)AccessTools.Method(typeof(Sandbox.Game.Weapons.MyLargeTurretTargetingSystem.MyPositionPrediction),"FindIntersection").Invoke(
                null,new object[] {Vector3D.Zero,Vector3D.Zero,600f,target,velocity-shooterVelocity});

        private static void Lead(Action<string> log)
        {
            var field=AccessTools.Field(typeof(MyHudMarkerRender.MyTargetIndicatorRender),"m_targetLeadRender");
            if(field==null || AccessTools.Field(field.FieldType,"m_positionPrediction")?.FieldType!=typeof(Sandbox.Game.Weapons.MyLargeTurretTargetingSystem.MyPositionPrediction))
                throw new Exception("Native lead predictor API changed");
            var target=new Vector3D(80,0,-700); var velocity=new Vector3D(80,0,0);
            Near(Vector3D.Distance(Predict(target,Vector3D.Zero),target),0,"Stationary native lead");
            var aim=Predict(target,velocity);
            if(!aim.IsValid() || aim.X<=target.X) throw new Exception("Native transverse lead missing");
            double flight=aim.Length()/600;
            Near(Vector3D.Distance(aim,target+velocity*flight),0,"Native projectile intercept",.001);
            Near(Vector3D.Distance(Predict(target,velocity,velocity),target),0,"Native shooter velocity compensation",.001);
            log("PASS native lead: installed predictor fields and native intercept kernel for stationary/transverse targets and shooter velocity compensation. Live ammunition/gravity selection requires in-game validation.");
        }

        private static void Motion(Action<string> log)
        {
            var type=AccessTools.Inner(typeof(MyHudMarkerRender),"PointOfInterest");
            var field=AccessTools.Field(typeof(MyHudMarkerRender),"m_pointsOfInterest");
            var renderer=(MyHudMarkerRender)FormatterServices.GetUninitializedObject(typeof(MyHudMarkerRender));
            var points=(IList)Activator.CreateInstance(field.FieldType);
            field.SetValue(renderer,points);
            var position=AccessTools.Property(type,"WorldPosition");
            object moving=Activator.CreateInstance(type,true),gps=Activator.CreateInstance(type,true);
            foreach(var poi in new[] { moving,gps })
            {
                var kind=AccessTools.Property(type,"POIType");
                kind.SetValue(poi,Enum.Parse(kind.PropertyType,poi==moving ? "SmallEntity" : "GPS"));
                ((StringBuilder)AccessTools.Property(type,"Text").GetValue(poi)).Append(poi==moving ? "Moving ship" : "Fixed GPS");
                points.Add(poi);
            }
            var origin=new Vector3D(1000000,2000000,3000000);
            var offset=new Vector3D(10,0,-100);
            position.SetValue(gps,origin+new Vector3D(-30,0,-1000));
            var packets=new Queue<Tuple<object,Vector3D>>();
            var now=DateTime.UtcNow;
            double oldLag=0;
            for(int tick=0;tick<180;tick++)
            {
                var head=origin+new Vector3D(tick*5,0,0); // 300 m/s at 60 Hz.
                position.SetValue(moving,head+offset);
                object message=new object();
                RenderFrameBridge.Capture(message,new CameraRig.Frame(MatrixD.CreateTranslation(head),Matrix.Identity),
                    ShipCrosshair.Read(new Sandbox.Game.Gui.MyHudCrosshair(),MatrixD.CreateTranslation(head)));
                var view=WorldMarkers.Read(renderer,MyHudMarkerRender.SignalMode.FullDisplay,head,now.AddSeconds(tick/60.0));
                RenderFrameBridge.CaptureMarkers(view);
                RenderFrameBridge.Commit();
                packets.Enqueue(Tuple.Create(message,head));
                oldLag=Math.Max(oldLag,tick%6*5);
                if(packets.Count<=2) continue;
                var delayed=packets.Dequeue();
                RenderFrameBridge.Consume(delayed.Item1);
                var paired=RenderFrameBridge.Markers;
                Near(Vector3D.Distance(RenderFrameBridge.Current.Anchor.Translation,delayed.Item2),0,"Camera batch pairing");
                Near(Vector3D.Distance(RenderFrameBridge.Crosshair.Position,delayed.Item2+Vector3D.Forward*1000),0,"Ship crosshair lost camera-batch pairing");
                foreach(var marker in paired.Markers)
                {
                    var expected=marker.Name=="Moving ship" ? delayed.Item2+offset : origin+new Vector3D(-30,0,-1000);
                    Near(Vector3D.Distance(marker.Position,expected),0,"POI capture lag or pooled mutation");
                    foreach(float eye in new[] { -.032f,.032f })
                    {
                        var camera=MatrixD.Invert(MatrixD.CreateTranslation(delayed.Item2+new Vector3D(eye,0,0)));
                        var projection=VrMath.Projection(-1,1,-1,1,.05);
                        if(!WorldMarkers.Project(marker.Position,camera,projection,out var actual) ||
                           !WorldMarkers.Project(expected,camera,projection,out var target)) throw new Exception("Moving marker projection lost");
                        Near(Vector2.Distance(actual,target),0,"Moving marker detached from scene");
                    }
                }
            }
            if(WorldMarkers.Read(renderer,MyHudMarkerRender.SignalMode.Off,origin,now)!=null)
                throw new Exception("Signals-off retained markers");
            var aimMessage=new object();
            for(int tick=0;tick<18;tick++)
            {
                var ship=MatrixD.CreateRotationY(tick*.01); ship.Translation=origin+new Vector3D(tick*20,0,0);
                var aim=ShipCrosshair.Read(new Sandbox.Game.Gui.MyHudCrosshair(),ship);
                RenderFrameBridge.Capture(aimMessage,new CameraRig.Frame(ship,Matrix.Identity),aim);
                RenderFrameBridge.Commit(); RenderFrameBridge.Consume(aimMessage);
                Near(Vector3D.Distance(RenderFrameBridge.Crosshair.Position,ShipCrosshair.Aim(ship)),0,"Camera-only update retained stale aim");
            }
            RenderFrameBridge.Capture(aimMessage,null); RenderFrameBridge.Commit(); RenderFrameBridge.Consume(aimMessage);
            if(RenderFrameBridge.Crosshair!=null) throw new Exception("Leaving ship retained crosshair");
            var recycled=new object();
            RenderFrameBridge.Capture(recycled,null);
            RenderFrameBridge.CaptureMarkers(WorldMarkers.Read(renderer,MyHudMarkerRender.SignalMode.FullDisplay,origin,now));
            RenderFrameBridge.Commit(); RenderFrameBridge.Consume(recycled);
            if(RenderFrameBridge.Markers==null) throw new Exception("Native camera without independent rig lost markers");
            var retained=RenderFrameBridge.Markers;
            for(int tick=0;tick<12;tick++)
            {
                RenderFrameBridge.Capture(recycled,null);
                RenderFrameBridge.Commit(); RenderFrameBridge.Consume(recycled);
                if(!ReferenceEquals(RenderFrameBridge.Markers,retained)) throw new Exception("Camera-only update dropped native markers");
            }
            points.Clear();
            var empty=WorldMarkers.Read(renderer,MyHudMarkerRender.SignalMode.FullDisplay,origin,now);
            RenderFrameBridge.Capture(recycled,null); RenderFrameBridge.CaptureMarkers(empty);
            RenderFrameBridge.Commit(); RenderFrameBridge.Consume(recycled);
            if(RenderFrameBridge.Markers.Markers.Length!=0) throw new Exception("Removed marker retained");
            RenderFrameBridge.Capture(recycled,null); RenderFrameBridge.CaptureMarkers(retained);
            WorldMarkers.Reset();
            RenderFrameBridge.Commit(); RenderFrameBridge.Consume(recycled);
            if(RenderFrameBridge.Markers!=null) throw new Exception("World reset retained markers");
            log("PASS marker cadence: camera-only updates retain immutable captures; explicit empty HUD and world reset clear them.");
            log("PASS native POI motion: 180 ticks at 300 m/s, delayed render batches, pooled POIs/camera messages, fixed GPS and stereo. Old 10 Hz capture lag: "+oldLag+" m; paired per-frame capture: 0 m.");
        }
    }
}
