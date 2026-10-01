using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.World;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using VRage.Game.Components;
using VRage.Game.Entity;
using Sandbox.ModAPI;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Util;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.Entity.UseObject;
using SpaceEngineersVR.Player.Components;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    public static class HandInteraction
    {
        private static bool disabled;
        private static bool rayVisible;
        private sealed class Contact
        {
            public readonly CockpitTouch.Hand Input=new CockpitTouch.Hand();
            public IMyUseObject Hover,Pressed;
            public Vector3D LocalPoint;
            public MatrixD Wrist;
            public DateTime Grabbed,NextLabel;
            public string Label;
            public bool Pointing;
        }
        private static readonly Contact[] hands={new Contact(),new Contact()};
        private static readonly List<MyEntity> nearby=new List<MyEntity>();
        private static readonly List<IMyUseObject> controls=new List<IMyUseObject>();
        private static DateTime nextDetection;
        private static long owner;
        internal static bool PointingFor(Controller hand) => hands[hand==Player.HandL ? 1 : 0].Pointing;
        internal static void ResetTouch()
        {
            foreach(var h in hands) { h.Input.Reset(); h.Hover=h.Pressed=null; h.Label=null; h.Pointing=false; }
            controls.Clear(); nearby.Clear(); nextDetection=DateTime.MinValue;
        }
        internal const double PressReach=.16;
        internal static bool ShowRay(float pressure,bool wasVisible) => pressure>=(wasVisible ? .025f : .06f);
        private static readonly Action<MyCharacterDetectorComponent, bool> detect =
            AccessTools.MethodDelegate<Action<MyCharacterDetectorComponent, bool>>(
                AccessTools.Method(typeof(MyCharacterDetectorComponent), "DoDetection"));
        public static bool TryWorldPose(Controller controller, out MatrixD world)
        {
            world = MatrixD.Identity;
            if (!Main.VrActive || Main.MenuOpen || !controller.pose.isTracked || MySector.MainCamera == null || MySession.Static?.LocalCharacter == null)
                return false;
            world = CameraRig.DeviceWorld(controller.AimTracking);
            return world.IsValid();
        }

        public static bool TryInteractionRay(out LineD ray)
        {
            ray = default(LineD);
            var character = MySession.Static?.LocalCharacter;
            if (!InputRouter.Gameplay || character == null || character.IsDead || character.IsSitting ||
                MySession.Static.ControlledEntity != character || !TryWorldPose(Player.HandR, out MatrixD pose)) return false;
            if (WeaponHandling.TryPose(character, out MatrixD model, out Vector3D muzzle))
            { pose = model; pose.Translation = muzzle; }
            else if(character.CurrentWeapon==null && TrackedArms.TryPointPose(Player.HandR,out var finger))
                pose=finger;
            ray = RayForPose(pose);
            return true;
        }

        internal static LineD RayForPose(MatrixD pose) =>
            new LineD(pose.Translation, pose.Translation + pose.Forward * MyConstants.DEFAULT_INTERACTIVE_DISTANCE);

        internal static bool SmallControl(MatrixD activation) => activation.IsValid() &&
            activation.Right.Length()<=.6 && activation.Up.Length()<=.6 && activation.Backward.Length()<=.6;
        private static bool Pressable(IMyUseObject target) => target!=null && SmallControl(target.ActivationMatrix) &&
            (target is SpaceEngineers.Game.Entities.UseObjects.MyUseObjectPanelButton ||
             target is Sandbox.Game.Entities.Cube.MyUseObjectTerminal || target is Sandbox.Game.Entities.Cube.MyUseObjectDoorTerminal ||
             target is Sandbox.Game.Entities.Cube.MyUseObjectAdvancedDoorTerminal || target is Sandbox.Game.Entities.Cube.MyUseObjectAirtightDoors);
        internal static Vector3D ClosestControlPoint(MatrixD activation,Vector3D point)
        {
            var local=Vector3D.Transform(point,MatrixD.Invert(activation));
            return Vector3D.Transform(Vector3D.Clamp(local,new Vector3D(-.5),new Vector3D(.5)),activation);
        }
        private static void Gather(Vector3D right,Vector3D left)
        {
            long started=FeatureTiming.Start();
            try
            {
                nearby.Clear(); controls.Clear();
                var sphere=new BoundingSphereD((right+left)*.5,Vector3D.Distance(right,left)*.5+.35);
                MyGamePruningStructure.GetAllEntitiesInSphere(ref sphere,nearby);
                foreach(var entity in nearby)
                    if(!entity.Closed) entity.Components.Get<MyUseObjectsComponentBase>()?.GetInteractiveObjects(controls);
                controls.RemoveAll(target=>!Pressable(target));
                nextDetection=DateTime.UtcNow.AddMilliseconds(100);
            }
            finally { FeatureTiming.End(FeatureTiming.Area.TouchQuery,started); }
        }
        private static bool SurfaceContact(IMyUseObject target,Vector3D from,out Vector3D point)
            => SurfaceContact(target.Owner as MyEntity,target.ActivationMatrix,from,out point);
        internal static bool SurfaceContact(MyEntity entity,MatrixD activation,Vector3D from,out Vector3D point)
        {
            point=ClosestControlPoint(activation,from);
            if(entity==null) return false;
            Vector3D direction=activation.Translation-from;
            if(direction.LengthSquared()<.000001) return false;
            direction.Normalize();
            var line=new LineD(from-direction*.025,activation.Translation+direction*.35);
            if(!entity.GetIntersectionWithLine(ref line,out VRage.Game.Models.MyIntersectionResultLineTriangleEx? hit) || !hit.HasValue) return false;
            point=hit.Value.IntersectionPointInWorldSpace;
            return Vector3D.Distance(from,point)<=PressReach &&
                Vector3D.Distance(point,ClosestControlPoint(activation,point))<.035;
        }
        private static void Consume(int i)
        {
            var c=Controls.Static;
            if(i==0) c.Primary.BlockUntilRelease();
            else
            {
                c.LeftTriggerPressure.BlockUntilRelease(); c.ThrustUp.BlockUntilRelease();
                c.ThrustForward.BlockUntilRelease(); c.JumpOrClimbUp.BlockUntilRelease();
            }
        }
        public static void UpdateTouch()
        {
            long started=FeatureTiming.Start();
            try { SampleTouch(); }
            catch(Exception ex)
            { disabled=true; ResetTouch(); Logger.Warning(ex,"Physical character interaction disabled; native use retained"); }
            finally { FeatureTiming.End(FeatureTiming.Area.Touch,started); }
        }
        private static void SampleTouch()
        {
            var character=MySession.Static?.LocalCharacter;
            bool available=!disabled && character!=null && !character.IsDead && !character.IsSitting && !character.IsOnLadder &&
                MySession.Static.ControlledEntity==character && character.CurrentWeapon==null && InputRouter.Gameplay &&
                MenuPointer.GameFocused && !Main.MenuOpen && Player.Headset.pose.isTracked &&
                !PlacementControls.OwnsTools && !TouchScreenBridge.OwnsInput && !SpatialUi.Pointing;
            var c=Controls.Static;
            if(owner!=character?.EntityId) { ResetTouch(); owner=character?.EntityId ?? 0; available=false; }
            bool rightPose=TrackedArms.TryFreeFingertip(Player.HandR,out var right),leftPose=TrackedArms.TryFreeFingertip(Player.HandL,out var left);
            if(available && (rightPose || leftPose) && DateTime.UtcNow>=nextDetection)
                Gather(rightPose ? right : left,leftPose ? left : right);
            for(int i=0;i<2;i++)
            {
                var h=hands[i]; var hand=i==0 ? Player.HandR : Player.HandL;
                float pressure=i==0 ? c.PointerPressure.RawPosition.X : c.LeftTriggerPressure.RawPosition.X;
                bool down=i==0 ? c.Primary.RawPressed : pressure>.55f;
                bool free=available && (i==0 ? rightPose : leftPose) && !HelmetHud.Consumes(hand);
                h.Pointing=false;
                if(!free)
                {
                    h.Input.Sample(false,pressure,down,null,-1); h.Hover=h.Pressed=null; h.Label=null;
                    if(h.Input.Consumed) Consume(i);
                    continue;
                }
                var tip=i==0 ? right : left;
                IMyUseObject target=null; double nearest=PressReach;
                foreach(var candidate in controls)
                {
                    if(candidate.Owner==null || candidate.Owner.Closed || ReferenceEquals(hands[1-i].Pressed,candidate)) continue;
                    double distance=Vector3D.Distance(tip,ClosestControlPoint(candidate.ActivationMatrix,tip));
                    if(distance<nearest) { nearest=distance; target=candidate; }
                }
                bool reachable=h.Pressed?.Owner!=null && !h.Pressed.Owner.Closed &&
                    Vector3D.Distance(tip,Vector3D.Transform(h.LocalPoint,h.Pressed.WorldMatrix))<.28;
                h.Input.Sample(true,pressure,down,target==null ? null : RuntimeHelpers.GetHashCode(target).ToString(),0,
                    i==0 ? c.Primary.HasPressed : c.LeftTriggerPressure.Position.X>0,reachable,softCapture:false);
                if(h.Input.Pressed)
                {
                    if(SurfaceContact(target,tip,out var point))
                    {
                        h.Pressed=target; h.LocalPoint=Vector3D.Transform(point,MatrixD.Invert(target.WorldMatrix));
                        h.Wrist=TrackedArms.FreeWristWorld(hand)*MatrixD.Invert(target.WorldMatrix); h.Grabbed=DateTime.UtcNow;
                        var action=target.PrimaryAction!=UseActionEnum.None ? target.PrimaryAction : target.SecondaryAction;
                        if(action!=UseActionEnum.None)
                        {
                            // The native use object retains its access checks and multiplayer action dispatch.
                            target.Use(action,character);
                            if(target.PlayIndicatorSound) VRage.Audio.MyAudio.Static?.PlaySound(MySoundPair.GetCueId("HudUse"));
                            CockpitFeedback.Activate(hand);
                        }
                        if(VRGUIManager.IsAnyDialogOpen()) Main.MenuOpen=true;
                        InputRouter.Update();
                    }
                    else h.Input.Reset();
                }
                if(h.Input.Held<0) h.Pressed=null;
                if(h.Input.Consumed) Consume(i);
                bool aiming=i==0 && (ShowRay(pressure,rayVisible) || down);
                bool keepHover=h.Hover?.Owner!=null && !h.Hover.Owner.Closed && Pressable(h.Hover) &&
                    Vector3D.Distance(tip,ClosestControlPoint(h.Hover.ActivationMatrix,tip))<PressReach+.025;
                h.Pointing=target!=null || h.Pressed!=null || aiming || keepHover;
                var hover=h.Pressed ?? target ?? (keepHover ? h.Hover : aiming ? character.GetDetectorComponent()?.UseObject : null);
                if(!ReferenceEquals(hover,h.Hover))
                {
                    if(hover!=null && !h.Input.Consumed) CockpitFeedback.Hover(hand);
                    h.Hover=hover; h.Label=null; h.NextLabel=DateTime.MinValue;
                }
                if(h.Hover!=null && DateTime.UtcNow>=h.NextLabel)
                { h.Label=UseLabel(h.Hover); h.NextLabel=DateTime.UtcNow.AddMilliseconds(200); }
            }
            if(!available) { controls.Clear(); nearby.Clear(); nextDetection=DateTime.MinValue; }
        }
        private static readonly System.Reflection.FieldInfo buttonIndex=AccessTools.Field(typeof(SpaceEngineers.Game.Entities.UseObjects.MyUseObjectPanelButton),"m_index");
        private static string UseLabel(IMyUseObject target)
        {
            if(target is SpaceEngineers.Game.Entities.UseObjects.MyUseObjectPanelButton && target.Owner is SpaceEngineers.Game.Entities.Blocks.MyButtonPanel panel)
            {
                int index=(int)buttonIndex.GetValue(target);
                return panel.Toolbar.GetItemAtIndex(index)?.DisplayName?.ToString() ?? "Configure buttons";
            }
            if(target.Owner is IMyDoor door && target.PrimaryAction==UseActionEnum.Manipulate)
                return door.OpenRatio>0 ? "Close door" : "Open door";
            if(target.PrimaryAction==UseActionEnum.OpenTerminal) return "Terminal · "+target.Owner.DisplayName;
            return null;
        }
        internal static bool TryAttachment(Controller hand,out MatrixD wrist,out Vector3D contact,out float blend)
        {
            var h=hands[hand==Player.HandL ? 1 : 0];
            wrist=MatrixD.Identity; contact=Vector3D.Zero; blend=0;
            if(h.Input.Held<0 || h.Pressed?.Owner==null || h.Pressed.Owner.Closed) return false;
            contact=Vector3D.Transform(h.LocalPoint,h.Pressed.WorldMatrix);
            wrist=h.Wrist*h.Pressed.WorldMatrix;
            blend=MathHelper.Clamp((float)(DateTime.UtcNow-h.Grabbed).TotalSeconds/.09f,0,1);
            return true;
        }
        internal static IEnumerable<SurfaceView> Labels()
        {
            if(Main.MenuOpen || !InputRouter.Gameplay) yield break;
            for(int i=0;i<2;i++)
            {
                var h=hands[i]; var hand=i==0 ? Player.HandR : Player.HandL;
                if(!h.Pointing || string.IsNullOrWhiteSpace(h.Label) || !TrackedArms.TryPointContact(hand,true,out var tip)) continue;
                var head=SpatialUi.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix);
                yield return new SurfaceView { Id="Character use label"+i,Style=SurfaceStyle.Label,Width=.18f,Height=.028f,
                    Title=h.Label,Pose=CockpitTouch.LabelPose(head,tip,.18f) };
            }
        }
        public static void Feedback()
        {
            if(MySession.Static?.LocalCharacter?.GetDetectorComponent()?.UseObject!=null) CockpitFeedback.Activate(Player.HandR);
        }

        public static void RefreshTarget()
        {
            // Native hover updates run every ten ticks. Refresh on an action so a
            // moved controller cannot activate the object highlighted on an old ray.
            if (!TryInteractionRay(out _)) return;
            var detector = MySession.Static.LocalCharacter.GetDetectorComponent();
            if (detector == null) return;
            detect(detector, true);
            if(Common.Config.DeveloperTools) Logger.Info("INTERACTION detector=" + detector.GetType().Name + "; origin=" + detector.StartPosition +
                "; use=" + (detector.UseObject?.GetType().Name ?? "none") +
                "; target=" + (detector.UseObject?.Owner?.DisplayName ?? "none"));
        }
        public static void Update()
        {
            if (disabled) return;
            rayVisible=InputRouter.Gameplay && Player.HandR.pose.isTracked &&
                (ShowRay(Controls.Static.PointerPressure.RawPosition.X,rayVisible) || Controls.Static.Primary.RawPressed);
            try
            {
                foreach (var hand in new[] { Player.HandL, Player.HandR })
                {
                    if (!TryWorldPose(hand,out MatrixD world)) continue;
                    var color = hand == Player.HandR ? new Color(60,220,255) : new Color(255,180,60);
                    var lineColor = color.ToVector4();
                    // Grip marker belongs at the raw tracked controller origin, not at
                    // its tip attachment. Only the aiming ray/tool uses the tip pose.
                    MatrixD grip=CameraRig.DeviceWorld(hand.pose.deviceToAbsolute.matrix);
                    if(Common.Config.DeveloperTools) MySimpleObjectDraw.DrawLine(grip.Translation-grip.Up*0.035,grip.Translation+grip.Up*0.035,
                        MyStringId.GetOrCompute("Square"),ref lineColor,0.025f);
                    if (hand == Player.HandR && rayVisible && TryInteractionRay(out LineD ray))
                    {
                        MySimpleObjectDraw.DrawLine(ray.From, ray.To,
                            MyStringId.GetOrCompute("Square"), ref lineColor, 0.002f);
                    }
                }
            }
            catch(Exception ex) { disabled=true; Logger.Warning(ex,"Hand indicators disabled"); }
        }
        public static bool TryInteract()
        {
            var block = TargetBlock();
            if (block is IMyDoor door) { if (door.OpenRatio > 0) door.CloseDoor(); else door.OpenDoor(); }
            else if (block is IMyLightingBlock light) light.Enabled = !light.Enabled;
            else return false;
            Player.HandR.Vibrate(0,0.06f,120,0.4f);
            return true;
        }
        private static IMyTerminalBlock TargetBlock()
        {
            if (!TryInteractionRay(out LineD ray)) return null;
            // Door/light convenience actions must not override a native cockpit,
            // button panel or other use-object selected along the same ray.
            if (MySession.Static.LocalCharacter.GetDetectorComponent()?.UseObject != null) return null;
            Vector3D from = ray.From;
            Vector3D to = ray.To;
            if (MyAPIGateway.Physics == null || !MyAPIGateway.Physics.CastRay(from,to,out IHitInfo hit)) return null;
            var grid = hit.HitEntity as IMyCubeGrid;
            if (grid == null) return null;
            var cell = grid.RayCastBlocks(from,to);
            var block = cell.HasValue ? grid.GetCubeBlock(cell.Value)?.FatBlock as IMyTerminalBlock : null;
            if (block == null || MyAPIGateway.Session?.Player == null || !block.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId)) return null;
            return block;
        }
    }
}
