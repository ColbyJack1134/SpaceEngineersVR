using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Wrappers;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    public static class RegressionTests
    {
        private static void Near(double actual,double expected,string name,double epsilon=0.00001)
        {
            if (double.IsNaN(actual) || Math.Abs(actual-expected)>epsilon) throw new Exception(name+": "+actual+" != "+expected);
        }
        public static void Run(Action<string> log)
        {
            KeyboardInputTests.Run(log);
            var cameraMessageA = new object(); var cameraMessageB = new object();
            var frameA = new CameraRig.Frame(MatrixD.CreateTranslation(100, 0, 0), Matrix.Identity);
            var frameB = new CameraRig.Frame(MatrixD.CreateTranslation(101, 0, 0), Matrix.CreateTranslation(0.1f, 0, 0));
            RenderFrameBridge.Capture(cameraMessageA, frameA);
            RenderFrameBridge.Commit();
            RenderFrameBridge.Capture(cameraMessageB, frameB);
            RenderFrameBridge.Commit();
            RenderFrameBridge.Consume(cameraMessageA);
            if (!ReferenceEquals(RenderFrameBridge.Current, frameA)) throw new Exception("Renderer picked a newer simulation pose than its body batch");
            if (!ReferenceEquals(RenderFrameBridge.ForCurrentOwner(frameB),frameA)) throw new Exception("Same-owner render handoff reverted to latest simulation");
            var ejectedFrame=new CameraRig.Frame(MatrixD.CreateTranslation(101,1.63,0),Matrix.Identity,1);
            if (!ReferenceEquals(RenderFrameBridge.ForCurrentOwner(ejectedFrame),ejectedFrame)) throw new Exception("Ejection transition lost validated height before new render packet");
            if (RenderFrameBridge.ForCurrentOwner(null)!=null) throw new Exception("Previous character camera survived owner removal");
            RenderFrameBridge.Consume(cameraMessageB);
            if (!ReferenceEquals(RenderFrameBridge.Current, frameB)) throw new Exception("Camera batch lost paired roomscale origin");
            RenderFrameBridge.Capture(cameraMessageA, null);
            RenderFrameBridge.Commit();
            RenderFrameBridge.Consume(cameraMessageA);
            if (RenderFrameBridge.Current != null) throw new Exception("Pooled message retained prior character camera");
            log("PASS render camera handoff: delayed batches, paired origin and pooled message reset");

            var markerProjection=VrMath.Projection(-1,1,-1,1,0.05);
            Vector3D markerOrigin=new Vector3D(1000000,2000000,3000000);
            var leftView=MatrixD.CreateTranslation(-markerOrigin+new Vector3D(0.032,0,0));
            var rightView=MatrixD.CreateTranslation(-markerOrigin-new Vector3D(0.032,0,0));
            if (!WorldMarkers.Project(markerOrigin+new Vector3D(0,0,-2),leftView,markerProjection,out var leftPoint) ||
                !WorldMarkers.Project(markerOrigin+new Vector3D(0,0,-2),rightView,markerProjection,out var rightPoint) || leftPoint.X<=rightPoint.X)
                throw new Exception("World marker lost per-eye stereo parallax");
            if (WorldMarkers.Project(markerOrigin+Vector3D.Backward,leftView,markerProjection,out _) ||
                WorldMarkers.Project(new Vector3D(double.NaN,0,0),leftView,markerProjection,out _))
                throw new Exception("Invalid/behind-eye marker projected into view");
            log("PASS stereo markers: large world coordinates, per-eye parallax, behind-eye/invalid rejection");
            MarkerTests.Run(log);

            var gate = new SpaceEngineersVR.Player.Control.InputGate();
            gate.Update(true, true);
            if (gate.Held) throw new Exception("Startup must not accept a held control");
            gate.Update(true, false);
            gate.Update(true, true);
            if (!gate.Held || !gate.Pressed) throw new Exception("Fresh press lost");
            gate.Update(true, true);
            if (gate.Pressed) throw new Exception("Held button repeated its press");
            gate.Block();
            gate.Update(false, false);
            gate.Update(true, true);
            if (gate.Held || gate.Pressed) throw new Exception("Held input leaked across inactive action sets");
            gate.Update(true, false);
            gate.Update(true, true);
            if (!gate.Pressed) throw new Exception("Release did not rearm input");
            gate.Update(true, false);
            if (!gate.Released || gate.Held) throw new Exception("Release edge lost");
            gate.Update(true, true);
            gate.Update(false, false);
            gate.Update(true, true);
            if (gate.Held) throw new Exception("Input reactivation must require a physical release");
            log("PASS input ownership: startup, held press, context/tracking reset, inactive set, release and rearm");
            LocomotionInputTests.Run(log);
            InteractionRayTests.Run(log);
            PointerIntentTests.Run(log);
            StereoStateTests.Run(log);
            AlignmentTests.Run(log);
            RenderingPatchTests.Run(log);
            ThirdPersonTests.Run(log);
            ResolutionTests.Run(log);
            PerformanceTests.Run(log);
            GameplayFeatureTests.Run(log);
            PlacementTests.Run(log);
            StickPlacementTests.Run(log);
            InteractionTests.Run(log);
            BuildOrientationTests.Run(log);

            foreach (int count in new[] { 8, 9, 10 })
                for (int sector = 0; sector < count; sector++)
                {
                    double angle = sector * Math.PI * 2 / count;
                    int selected = RadialMath.Sector(new Vector2((float)Math.Sin(angle), (float)Math.Cos(angle)), count);
                    if (selected != sector) throw new Exception("Radial sector/wrap mismatch");
                }
            if (RadialMath.Sector(Vector2.Zero, 9) != -1 || RadialMath.Sector(new Vector2(float.NaN, 1), 9) != -1 ||
                RadialMath.Sector(new Vector2(0.2f, 0.2f), 9) != -1)
                throw new Exception("Radial neutral/invalid input must cancel");
            log("PASS radial selection: native slot counts, full-circle wrap, neutral and invalid cancellation");

            var actions = new Player.Control.ActionFrame();
            var actionId = VRage.Utils.MyStringId.GetOrCompute("SEVR regression action");
            actions.Queue(actionId);
            actions.Advance(true);
            if (!actions.Read(actionId, VRage.Input.MyControlStateType.NEW_PRESSED) ||
                !actions.Read(actionId, VRage.Input.MyControlStateType.PRESSED)) throw new Exception("Native action press lost");
            actions.Queue(actionId); actions.Advance(true);
            if (actions.Read(actionId, VRage.Input.MyControlStateType.NEW_PRESSED)) throw new Exception("Native hold repeated its press");
            actions.Advance(true);
            if (!actions.Read(actionId, VRage.Input.MyControlStateType.NEW_RELEASED)) throw new Exception("Native action release lost");
            actions.Queue(actionId); actions.Advance(false); actions.Advance(true);
            if (actions.Read(actionId, VRage.Input.MyControlStateType.PRESSED)) throw new Exception("Disabled native pulse leaked");
            actions.Queue(actionId); actions.Reset(); actions.Advance(true);
            if (actions.Read(actionId, VRage.Input.MyControlStateType.NEW_PRESSED)) throw new Exception("Reset retained queued native action");
            log("PASS native action frame: press/hold/release, disabled pulse discard and transition cancellation");

            var flightMove = FlightAxes.Translation(Vector2.Zero, new Vector2(0.4f, 0.7f), 0.8f, 0.2f, 0, 0);
            Near(flightMove.X, 0.4, "flight lateral"); Near(flightMove.Y, 0.6, "flight up/down cancellation"); Near(flightMove.Z, -0.7, "flight forward");
            Near(FlightAxes.Translation(Vector2.Zero, Vector2.Zero, 0, 1, 0, 1).Y, -1, "flight down");
            Near(FlightAxes.Translation(Vector2.Zero, Vector2.Zero, 0, 1, 0, 1).Z, 1, "flight backward");
            FlightAxes.Rotation(new Vector2(0.4f, 0.6f), false, false, 10, 1, out Vector2 rotation, out float roll);
            Near(rotation.X, 6, "validated jetpack pitch"); Near(rotation.Y, 4, "flight yaw"); Near(roll, 0, "unmodified roll");
            FlightAxes.Rotation(new Vector2(0.4f, 0.6f), true, true, 10, 1, out rotation, out roll);
            Near(rotation.X, -6, "ship pitch convention"); Near(rotation.Y, 0, "roll owns horizontal axis"); Near(roll, 4, "flight roll");
            log("PASS flight axes: XYZ translation, opposing thrust, pitch conventions, yaw/roll ownership");
            RollSensitivityTests.Run(log);

            // OpenVR raw top/bottom are signed Y bounds, not negated camera-space Y.
            // Independently checked against IVRSystem.GetProjectionMatrix in VrProbe.
            MatrixD projection=VrMath.Projection(-0.9f,1.1f,-0.8f,1.2f,0.05);
            Vector3D top=Vector3D.Transform(new Vector3D(0,1.2,-1),projection);
            Vector3D bottom=Vector3D.Transform(new Vector3D(0,-0.8,-1),projection);
            Vector3D left=Vector3D.Transform(new Vector3D(-0.9,0,-1),projection);
            Vector3D right=Vector3D.Transform(new Vector3D(1.1,0,-1),projection);
            Near(top.Y,1,"top edge"); Near(bottom.Y,-1,"bottom edge");
            Near(left.X,-1,"left edge"); Near(right.X,1,"right edge");
            Near(Vector3D.Transform(new Vector3D(0,0,-0.05),projection).Z,1,"reversed near depth");
            log("PASS asymmetric OpenVR projection and reversed depth");

            Matrix head=Matrix.CreateRotationY(0.7f); head.Translation=new Vector3(1.2f,1.1f,-0.6f);
            Matrix originInverse=Matrix.Invert(VrMath.TrackingOrigin(head));
            MatrixD baseWorld=MatrixD.CreateRotationY(-0.4); baseWorld.Translation=new Vector3D(1e9,2e9,-3e9);
            MatrixD view=MatrixD.Invert(baseWorld);
            MatrixD leftWorld=MatrixD.Invert(VrMath.EyeView(view,head,originInverse,Matrix.CreateTranslation(-0.032f,0,0)));
            MatrixD rightWorld=MatrixD.Invert(VrMath.EyeView(view,head,originInverse,Matrix.CreateTranslation(0.032f,0,0)));
            Near(Vector3D.Distance(leftWorld.Translation,rightWorld.Translation),0.064,"IPD at large world coordinates");
            Near(Vector3D.Distance((leftWorld.Translation+rightWorld.Translation)*0.5,baseWorld.Translation),0,"seated recenter");
            Near(VrMath.Deadzone(0.1f),0,"deadzone"); Near(VrMath.Deadzone(1),1,"full input");
            log("PASS seated recenter, 64mm stereo separation at large coordinates, input deadzone");

            // A rotation about the tracked head position must not orbit the tracking-room origin.
            Matrix turned=Matrix.CreateRotationX(0.3f)*Matrix.CreateRotationY(1.2f);
            turned.Translation=head.Translation;
            MatrixD turnedWorld=MatrixD.Invert(VrMath.EyeView(view,turned,originInverse,Matrix.Identity));
            Near(Vector3D.Distance(turnedWorld.Translation,baseWorld.Translation),0,"head rotation pivot",0.00001);
            Matrix leaned=turned; leaned.Translation+=new Vector3(0.1f,0.05f,-0.02f);
            MatrixD leanedWorld=MatrixD.Invert(VrMath.EyeView(view,leaned,originInverse,Matrix.Identity));
            Near(Vector3D.Distance(leanedWorld.Translation,turnedWorld.Translation),Math.Sqrt(0.01+0.0025+0.0004),"physical lean scale",0.00001);
            Near(turnedWorld.Right.Length(),1,"rigid camera right");
            Near(Vector3D.Dot(turnedWorld.Right,turnedWorld.Up),0,"rigid camera orthogonality");
            log("PASS head rotation stays at the engineer anchor; physical lean is 1:1 with no camera shear");

            // Recenter at arbitrary facing must preserve full view orientation, including
            // a tilted flight frame. Repeat across +/-180 to catch quadrant/sign mistakes.
            foreach(double yaw in new[] {-3.1,-1.8,-0.2,1.7,3.1})
            {
                Matrix tracked=Matrix.CreateRotationX(0.2f)*Matrix.CreateRotationY((float)yaw);
                tracked.Translation=new Vector3(1,1.6f,-2);
                Matrix oldFloor=Matrix.CreateRotationY(-0.4f);
                Matrix newFloor=VrMath.TrackingOrigin(tracked);
                MatrixD anchor=MatrixD.CreateRotationZ(0.25)*MatrixD.CreateRotationY(0.8);
                MatrixD before=(MatrixD)(tracked*Matrix.Invert(oldFloor))*anchor;
                MatrixD after=(MatrixD)(tracked*Matrix.Invert(newFloor))*VrMath.RecenterAnchor(anchor,oldFloor,newFloor);
                Near(Vector3D.Distance(before.Forward,after.Forward),0,"recenter preserves facing",0.000001);
                Near(Vector3D.Distance(before.Up,after.Up),0,"recenter preserves tilt",0.000001);
            }
            log("PASS recenter preserves world facing across 180-degree turns and tilted flight frames");

            // Cockpit detachment must not overwrite a validated height with an
            // ejection/seated pose. New characters, suits and worlds recalibrate.
            var heightCache=new EyeHeightCalibration();
            var engineer=new object(); var suit=new object();
            Near(heightCache.Get(engineer,suit,1.72,1.7,1.9),1.72,"initial eye calibration");
            Near(heightCache.Get(engineer,suit,0,1.7,1.9),1.72,"cockpit ejection retains standing eyes");
            Near(heightCache.Get(engineer,suit,0.7,1.7,1.9),1.72,"seated animation cannot lower saved eyes");
            Near(heightCache.Get(new object(),suit,0,1.7,1.9),1.7,"spawn in cockpit uses bind height");
            Near(heightCache.Get(engineer,new object(),0,1.55,1.75),1.55,"suit change recalibrates");
            heightCache.Clear();
            Near(heightCache.Get(engineer,suit,0,1.7,1.9),1.7,"world reset clears old calibration");
            Near(heightCache.Get(new object(),suit,double.NaN,double.NaN,1.8),1.62,"missing skeleton uses collision fallback");
            log("PASS eye-height recovery across cockpit ejection, seated startup, character/suit changes and world reset");

            // Physical body yaw is captured after following: it must never feed back into the rig.
            MatrixD rig=MatrixD.Identity;
            for(int tick=0;tick<20000;tick++)
            {
                MatrixD body=MatrixD.CreateRotationY(Math.Sin(tick*0.013)*2.5);
                rig=VrMath.Level(VrMath.AdvanceAnchor(rig,body,body),Vector3D.Up);
            }
            Near(Vector3D.Distance(rig.Forward,Vector3D.Forward),0,"body following cannot accumulate camera yaw",1e-9);
            Near(Vector3D.Distance(rig.Up,Vector3D.Up),0,"body following cannot accumulate roll",1e-9);
            MatrixD stick=VrMath.AdvanceAnchor(rig,MatrixD.Identity,MatrixD.CreateRotationY(0.4));
            Near(Vector3D.Distance(stick.Forward,MatrixD.CreateRotationY(0.4).Forward),0,"stick turn remains effective",1e-9);
            MatrixD flight=MatrixD.CreateRotationZ(0.3)*MatrixD.CreateRotationX(0.2);
            Near(Vector3D.Distance(VrMath.AdvanceAnchor(rig,MatrixD.Identity,flight).Up,flight.Up),0,"intentional flight rotation preserved",1e-9);
            Matrix consumed=VrMath.TrackingOrigin(head);
            Vector3 travel=new Vector3(0.2f,0,-0.1f);
            Matrix movedHead=head; movedHead.Translation+=Vector3.TransformNormal(travel,consumed);
            consumed.Translation+=Vector3.TransformNormal(travel,consumed);
            MatrixD movedAnchor=baseWorld; movedAnchor.Translation+=Vector3D.TransformNormal(travel,baseWorld);
            MatrixD walked=MatrixD.Invert(VrMath.EyeView(MatrixD.Invert(movedAnchor),movedHead,Matrix.Invert(consumed),Matrix.Identity));
            Near(Vector3D.Distance(walked.Translation,movedAnchor.Translation),0,"roomscale consumed exactly once",0.00001);
            log("PASS 20,000 body-follow turns without yaw/roll drift, stick/flight rotation and roomscale consumption");

            // Standing sway affects the eyes without touching the capsule. The old
            // 1cm cutoff consumed the whole offset at once; the new boundary is continuous.
            for(int tick=0;tick<600;tick++)
            {
                var sway=new Vector3((float)Math.Sin(tick*0.03)*0.02f,0.01f,(float)Math.Cos(tick*0.04)*0.01f);
                Near(VrMath.RoomscaleTravel(sway).Length(),0,"standing sway must not move capsule",1e-8);
            }
            Near(VrMath.RoomscaleTravel(new Vector3(VrMath.RoomscaleBodyRadius+0.000001f,0,0)).X,
                0.000001,"crossing sway boundary must not snap",1e-8);
            if(VrMath.HasStepRise(0.005) || VrMath.HasStepRise(0.01) || !VrMath.HasStepRise(0.2))
                throw new Exception("Step-up must reject floor skin but accept a real stair");

            Matrix initialFloor=Matrix.CreateRotationY(0.7f);
            Matrix floor=initialFloor;
            MatrixD physical=MatrixD.CreateRotationY(-0.4); physical.Translation=new Vector3D(1e9,2e9,-3e9);
            MatrixD initialPhysical=physical;
            for(int tick=0;tick<1200;tick++)
            {
                Matrix tracked=Matrix.CreateRotationY(0.7f);
                tracked.Translation=Vector3.TransformNormal(new Vector3((float)Math.Sin(tick*0.01)*0.4f,
                    (float)Math.Sin(tick*0.03)*0.005f,tick*0.001f),initialFloor);
                Vector3 remaining=VrMath.Affine(tracked*Matrix.Invert(floor)).Translation;
                Vector3 transfer=VrMath.RoomscaleTravel(remaining);
                physical.Translation+=Vector3D.TransformNormal(transfer,physical);
                floor.Translation+=Vector3.TransformNormal(transfer,floor);
                // The entity repeatedly loses/regains its render-only foot offset,
                // just as it does when roomscale writes followed by engine IK alternate.
                MatrixD visual=physical; visual.Translation+=visual.Up*(tick%2==0 ? 0.08 : -0.03);
                MatrixD cameraAnchor=VrMath.PhysicsAnchoredBody(visual,physical.Translation);
                MatrixD actualEye=MatrixD.Invert(VrMath.EyeView(MatrixD.Invert(cameraAnchor),tracked,Matrix.Invert(floor),Matrix.Identity));
                MatrixD expectedEye=MatrixD.Invert(VrMath.EyeView(MatrixD.Invert(initialPhysical),tracked,Matrix.Invert(initialFloor),Matrix.Identity));
                Near(Vector3D.Distance(actualEye.Translation,expectedEye.Translation),0,"roomscale transfer and visual foot offsets cannot teleport eyes",0.00002);
            }
            log("PASS idle sway, continuous body-follow boundary, flat-floor step rejection and 1,200 roomscale transfers with changing visual foot offsets");

            Vector3 directed=VrMath.DirectedMove(Vector3.Forward,Vector3.Right);
            Near(directed.X,1,"head-directed rightward movement"); Near(directed.Z,0,"head-directed forward axis");
            Near(VrMath.DirectedMove(Vector3.Up,Vector3.Right).Y,1,"vertical thrust preserved");
            Matrix panel=Matrix.CreateTranslation(0,1,-2);
            Matrix hand=Matrix.CreateTranslation(0,1,0);
            if (!VrMath.PanelHit(hand,panel,2.4f,1.35f,out Vector2 uv)) throw new Exception("panel centre miss");
            Near(uv.X,0.5,"panel centre x"); Near(uv.Y,0.5,"panel centre y");
            hand.Translation=new Vector3(-1.2f,1.675f,0);
            if (!VrMath.PanelHit(hand,panel,2.4f,1.35f,out uv)) throw new Exception("panel corner miss");
            Near(uv.X,0,"panel left"); Near(uv.Y,0,"panel top");
            hand=Matrix.CreateRotationY(MathHelper.Pi); hand.Translation=new Vector3(0,1,0);
            if(VrMath.PanelHit(hand,panel,2.4f,1.35f,out uv)) throw new Exception("backward ray accepted");
            log("PASS directional locomotion and panel pointer projection");

            Type environment=AccessTools.TypeByName("VRageRender.MyEnvironment");
            Type matricesType=AccessTools.Field(environment,"Matrices").FieldType;
            object actual=FormatterServices.GetUninitializedObject(matricesType);
            var wrapper=new EnvironmentMatrices(actual);
            wrapper.FovH=0.9f;
            wrapper.ViewFrustumClippedD=new BoundingFrustumD(MatrixD.Identity);
            var snapshot=wrapper.Capture();
            wrapper.FovH=1.4f;
            wrapper.ViewFrustumClippedD=new BoundingFrustumD(MatrixD.CreateScale(2));
            wrapper.Restore(snapshot);
            Near(wrapper.FovH,0.9,"camera restore");
            Near(wrapper.ViewFrustumClippedD.Matrix.M11,1,"frustum restore");
            log("PASS renderer camera snapshot/restore on the installed engine type");

            Type sceneType=AccessTools.TypeByName("VRage.Render11.Scene.MyScene11");
            Type sceneEnvironment=AccessTools.Field(sceneType,"Environment")?.FieldType ?? AccessTools.Property(sceneType,"Environment")?.PropertyType;
            if (sceneEnvironment == null || AccessTools.Field(sceneEnvironment,"CameraPosition")?.FieldType != typeof(Vector3D))
                throw new Exception("Scene camera accessor is incompatible");
            object scene = FormatterServices.GetUninitializedObject(sceneType);
            var sceneCamera = new SceneCamera(scene);
            sceneCamera.Position = new Vector3D(1,2,3);
            object engineEnvironment = AccessTools.Field(sceneType,"Environment").GetValue(scene);
            var enginePosition = (Vector3D)AccessTools.Field(sceneEnvironment,"CameraPosition").GetValue(engineEnvironment);
            Near(enginePosition.Y,2,"scene struct camera write");
            log("PASS scene camera accessor writes through the engine's value-type environment");

            ArmTests.Run(log);
            PhysicalControlTests.Run(log);
            SpatialUiTests.Run(log);
            CockpitTests.Run(log);
            CockpitRigTests.Run(log);
            CockpitStateTests.Run(log);
            CockpitProbeTests.Run(log);

            VRage.ObjectBuilders.MyObjectBuilderType.RegisterFromAssembly(typeof(VRage.Game.MyDefinitionId).Assembly);
            VRage.ObjectBuilders.MyObjectBuilderType.RegisterFromAssembly(Assembly.Load("SpaceEngineers.ObjectBuilders"));
            var harmony=new Harmony("SpaceEngineersVR.BootstrapTests");
            try
            {
                harmony.PatchAll(typeof(Plugin.Main).Assembly);
                int count=harmony.GetPatchedMethods().Count();
                if(count<3) throw new Exception("Expected input, weapon and aiming patches; found "+count);
                log("PASS installed "+count+" gameplay Harmony patches (no game/VR execution); render patching is tested in-game");
            }
            finally { harmony.UnpatchAll(harmony.Id); }
        }
    }
}
