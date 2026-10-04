using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using SpaceEngineersVR.Player.Control;
using SpaceEngineersVR.Plugin;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Optional adapter to the installed TouchScreenAPI. No mod source is bundled.
    // Native text, sprites, LCD selection, app logic and PB API remain mod/game-owned.
    internal static class TouchScreenBridge
    {
        private const long Channel=2668820525;
        private static readonly InteractionPress primary=new InteractionPress();
        private static readonly InputGate secondary=new InputGate();
        private static readonly SurfaceTouch touch=new SurfaceTouch();
        private static readonly Dictionary<object,Screen> screens=new Dictionary<object,Screen>();
        private static object session,manager,selected,owner;
        private static FieldInfo listField,currentField;
        private static PropertyInfo enabledProperty;
        private static MethodInfo updateManager,updateButtons;
        private static bool failed,registered,down,secondaryDown;
        private static DateTime retry;
        private static Vector3D hitPoint,rayOrigin;
        private static Matrix origin;
        private static Dictionary<string,Delegate> api;
        public static bool OwnsInput { get; private set; }
        public static bool Pointing { get; private set; }
        public static bool PointingLeft { get; private set; }
        internal static bool PointingFor(Controller hand) => hand==Player.HandL ? PointingLeft:Pointing;
        private static Controller activeHand;
        public static bool Ready => manager!=null && !failed;
        private sealed class Screen
        {
            public object Value;
            public IMyTerminalBlock Block;
            public PropertyInfo Enabled,Coords,Aiming,OnScreen,Intersection,Cursor,Distance;
            public FieldInfo Mouse1,Mouse2,Mouse3;
            public MethodInfo Update,Coordinates,ButtonUpdate;
            public int Index;
            public Screen(object value)
            {
                Value=value; var t=value.GetType();
                Block=(IMyTerminalBlock)Property(t,"Block").GetValue(value);
                Index=(int)Property(t,"Index").GetValue(value);
                Enabled=Property(t,"Enabled"); Coords=Property(t,"Coords"); Aiming=Property(t,"IsPlayerAiming");
                OnScreen=Property(t,"IsOnScreen"); Intersection=Property(t,"Intersection"); Cursor=Property(t,"CursorPosition"); Distance=Property(t,"InteractiveDistance");
                Mouse1=Field(t,"Mouse1"); Mouse2=Field(t,"Mouse2"); Mouse3=Field(t,"Mouse3");
                Update=Method(t,"UpdateAtSimulation"); Coordinates=Method(t,"UpdateScreenCoord");
                ButtonUpdate=Method(Mouse1.FieldType,"Update");
                if(Intersection.GetSetMethod(true)==null || Aiming.GetSetMethod(true)==null) throw new MissingMethodException("TouchScreenAPI setters");
            }
            public SurfaceView Plane()
            {
                object coords=Coords.GetValue(Value); var t=coords.GetType();
                return PlaneFor((Vector3)Field(t,"TopLeft").GetValue(coords),(Vector3)Field(t,"BottomLeft").GetValue(coords),
                    (Vector3)Field(t,"BottomRight").GetValue(coords),Block.WorldMatrix);
            }
            public void Buttons(bool active,bool first,bool second)
            {
                ButtonUpdate.Invoke(Mouse1.GetValue(Value),new object[] { first,active });
                ButtonUpdate.Invoke(Mouse2.GetValue(Value),new object[] { second,active });
                ButtonUpdate.Invoke(Mouse3.GetValue(Value),new object[] { false,active });
            }
        }
        private static PropertyInfo Property(Type t,string name) => AccessTools.Property(t,name) ?? throw new MissingMemberException(t.FullName,name);
        private static FieldInfo Field(Type t,string name) => AccessTools.Field(t,name) ?? throw new MissingFieldException(t.FullName,name);
        private static MethodInfo Method(Type t,string name) => AccessTools.Method(t,name) ?? throw new MissingMethodException(t.FullName,name);
        internal static SurfaceView PlaneFor(Vector3 tl,Vector3 bl,Vector3 br,MatrixD world)
        {
            Vector3 right=br-bl,up=tl-bl;
            float width=right.Length(),height=up.Length();
            if(!tl.IsValid() || !bl.IsValid() || !br.IsValid() || width<.005f || height<.005f) return null;
            right/=width; up/=height;
            float skew=Vector3.Dot(right,up);
            if(Math.Abs(skew)>.95f) return null;
            // Some calibrated LCDs are trapezoids (Fighter surface 5). Use dual
            // axes so local X/Y equal the mod's dot-product screen coordinates.
            // Orthonormalizing them would shift the pointer away from its pixels.
            var pose=MatrixD.Identity;
            pose.Right=(right-up*skew)/(1-skew*skew);
            pose.Up=(up-right*skew)/(1-skew*skew);
            pose.Backward=Vector3.Normalize(Vector3.Cross(right,up));
            pose.Translation=tl+pose.Right*width/2-pose.Up*height/2;
            pose*=world;
            return new SurfaceView { Pose=pose,Width=width,Height=height };
        }
        private static void Message(object message)
        {
            if(message is Dictionary<string,Delegate> dict) api=dict.Count==0 ? null : dict;
        }
        public static object Create(Sandbox.ModAPI.Ingame.IMyTextSurface surface,VRage.Game.ModAPI.Ingame.IMyCubeBlock block)
        {
            if(api==null || !api.TryGetValue("CreateTouchScreen",out var create)) return null;
            return create.DynamicInvoke(block,surface);
        }
        public static void Remove(Sandbox.ModAPI.Ingame.IMyTextSurface surface,VRage.Game.ModAPI.Ingame.IMyCubeBlock block)
        {
            if(api!=null && api.TryGetValue("RemoveTouchScreen",out var remove)) remove.DynamicInvoke(block,surface);
        }
        public static bool Tap(object screen,out Vector2 position)
        {
            position=Vector2.Zero;
            if(screen==null || api==null) return false;
            if(!api.TryGetValue("TouchScreen_GetMouse1",out var mouse) || !api.TryGetValue("ButtonState_JustPressed",out var pressed) ||
                !api.TryGetValue("TouchScreen_GetCursorPosition",out var cursor)) return false;
            position=(Vector2)cursor.DynamicInvoke(screen);
            return (bool)pressed.DynamicInvoke(mouse.DynamicInvoke(screen));
        }
        public static void Reset()
        {
            foreach(var screen in screens.Values)
                try { screen.Aiming.SetValue(screen.Value,false); screen.Buttons(false,false,false); }
                catch(Exception ex) { Logger.Warning(ex,"Touch screen was already disposed during reset"); }
            primary.Block(); secondary.Block(); touch.Reset(); OwnsInput=Pointing=down=secondaryDown=false;
            selected=owner=null; screens.Clear(); manager=session=null; api=null;
            if(registered && MyAPIGateway.Utilities!=null) MyAPIGateway.Utilities.UnregisterMessageHandler(Channel,Message);
            registered=false; failed=false; retry=DateTime.MinValue;
        }
        private static void Discover()
        {
            if(DateTime.UtcNow<retry || MySession.Static==null || MyAPIGateway.Utilities==null) return;
            retry=DateTime.UtcNow.AddSeconds(1);
            if(!registered) { MyAPIGateway.Utilities.RegisterMessageHandler(Channel,Message); registered=true; }
            if(api==null) MyAPIGateway.Utilities.SendModMessage(Channel,"ApiRequestTouch");
            // The game can retain an unloaded world's mod assembly. Select a live
            // session, rather than stopping at the first assembly with this type.
            var t=AppDomain.CurrentDomain.GetAssemblies().Reverse().Select(a=>a.GetType("Lima.Touch.TouchSession",false))
                .FirstOrDefault(a=>a!=null && Field(a,"Instance").GetValue(null)!=null);
            if(t==null) return;
            var next=Field(t,"Instance").GetValue(null);
            if(next==null || ReferenceEquals(session,next)) return;
            if(updateManager!=null)
            {
                Common.Plugin.Harmony.Unpatch(updateManager,AccessTools.Method(typeof(TouchScreenBridge),nameof(ManagerPrefix)));
                Common.Plugin.Harmony.Unpatch(updateButtons,AccessTools.Method(typeof(TouchScreenBridge),nameof(ButtonsPrefix)));
            }
            manager=Field(t,"TouchMan").GetValue(next); enabledProperty=Property(t,"ModEnabled");
            listField=Field(manager.GetType(),"Screens"); currentField=Field(manager.GetType(),"CurrentScreen");
            var screenType=t.Assembly.GetType("Lima.Touch.TouchScreen",true);
            updateManager=Method(manager.GetType(),"UpdateAtSimulation"); updateButtons=Method(screenType,"UpdateMouseButtons");
            Common.Plugin.Harmony.Patch(updateManager,prefix:new HarmonyMethod(typeof(TouchScreenBridge),nameof(ManagerPrefix)));
            Common.Plugin.Harmony.Patch(updateButtons,prefix:new HarmonyMethod(typeof(TouchScreenBridge),nameof(ButtonsPrefix)));
            // The mod may have suppressed desktop firing before this adapter attached.
            var inputUtils=t.Assembly.GetType("Lima.Utils.InputUtils",true);
            Method(inputUtils,"SetPlayerUseBlacklistState").Invoke(null,new object[] { false });
            session=next; selected=null; screens.Clear(); primary.Block(); secondary.Block(); touch.Reset();
            Logger.Info("TouchScreenAPI VR adapter attached to "+t.Assembly.GetName().Name);
        }
        public static void Update()
        {
            OwnsInput=Pointing=PointingLeft=down=secondaryDown=false;
            if(failed) return;
            try
            {
                Discover();
                if(manager==null) return;
                var list=((IEnumerable)listField.GetValue(manager)).Cast<object>().ToArray();
                foreach(var stale in screens.Keys.Where(k=>!list.Contains(k)).ToArray()) screens.Remove(stale);
                foreach(var value in list) if(!screens.ContainsKey(value)) screens.Add(value,new Screen(value));
                // A held native trigger is firing; screen-owned squeezes already blocked that gate.
                bool firing=SeatFit.Eligible(SeatFit.Seat) && Controls.Static.Primary.IsPressed && !Controls.Static.Primary.HasPressed;
                bool grabbingStick=Controls.Static.RightGripPressure.RawPosition.X>.025f && CockpitControls.NearGrip(Player.HandR) ||
                    Controls.Static.LeftGripPressure.RawPosition.X>.025f && CockpitControls.NearGrip(Player.HandL);
                bool allowed=(bool)enabledProperty.GetValue(session) && Main.VrActive && !ThirdPersonView.Active && InputRouter.Gameplay && !Main.MenuOpen && MenuPointer.GameFocused &&
                    !firing && !grabbingStick && !RemoteView.OwnsInput &&
                    MySession.Static?.LocalCharacter?.IsDead==false && MySession.Static.LocalCharacter.CurrentWeapon==null &&
                    Player.Headset.pose.isTracked && Player.HandR.pose.isTracked && Player.HandL.pose.isTracked &&
                    !CockpitControls.Adjusting && !CockpitTouch.OwnsRight && !CockpitControls.Held(Player.HandL) && !CockpitControls.Held(Player.HandR) &&
                    !PlacementControls.OwnsTools;
                // The mod has one cursor per screen, so one hand owns it: a held press keeps its hand, a fingertip touch beats a laser.
                var valid=new List<KeyValuePair<Screen,SurfaceView>>();
                foreach(var candidate in screens.Values)
                {
                    candidate.Aiming.SetValue(candidate.Value,false);
                    // Pause/dashboard may stop the mod's simulation loop. Clear now,
                    // before re-entry could turn an old press into an app release-click.
                    if(!allowed) candidate.Buttons(false,false,false);
                    if(!allowed || candidate.Block==null || candidate.Block.Closed || !candidate.Block.HasLocalPlayerAccess() ||
                        !(bool)candidate.Enabled.GetValue(candidate.Value)) continue;
                    var plane=candidate.Plane(); if(plane==null) continue;
                    var head=SpatialUi.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix).Translation;
                    if(Vector3D.Dot(plane.Pose.Backward,head-plane.Pose.Translation)<.015) continue;
                    valid.Add(new KeyValuePair<Screen,SurfaceView>(candidate,plane));
                }
                Screen best=null; SurfaceView bestPlane=null; float bestDistance=float.MaxValue; Vector3D intersection=Vector3D.Zero,bestTip=Vector3D.Zero,bestOrigin=Vector3D.Zero;
                bool bestDirect=false,bestPointing=false; Controller bestHand=null;
                foreach(var hand in new[] { Player.HandR,Player.HandL })
                {
                    if(primary.Held && hand!=activeHand) continue;
                    bool left=hand==Player.HandL;
                    MatrixD aim=SpatialUi.DeviceWorld(hand.AimTracking);
                    if(TrackedArms.TryFreePointPose(hand,out var pointing)) aim=pointing;
                    Vector3D tip=aim.Translation,from=aim.Translation;
                    bool rays=!left || PointerHand.LeftRayAllowed;
                    foreach(var entry in valid)
                    {
                        var candidate=entry.Key; var plane=entry.Value;
                        bool near=Vector3D.Distance(tip,plane.Pose.Translation)<Math.Max(plane.Width,plane.Height)*.5+.20;
                        Vector3D finger=tip;
                        if(near && TrackedArms.TryFingertip(hand,out var actual)) finger=actual;
                        var local=PhysicalSurface.Point(plane,finger); var uv=PhysicalSurface.UV(plane,local);
                        bool direct=local.Z>=-.018f && local.Z<.07f && uv.X>=0 && uv.X<=1 && uv.Y>=0 && uv.Y<=1;
                        bool close=near && local.Z>=-.04f && local.Z<.30f && uv.X>=0 && uv.X<=1 && uv.Y>=0 && uv.Y<=1;
                        if(close) { if(left) PointingLeft=true; else Pointing=true; }
                        var ray=(Matrix)(aim*MatrixD.Invert(plane.Pose));
                        Vector2 rayUv=Vector2.Zero;
                        bool hit=rays && VrMath.PanelHit(ray,Matrix.Identity,plane.Width,plane.Height,out rayUv);
                        if(!direct && !hit) continue;
                        Vector2 hitUv=direct ? uv : rayUv;
                        var point=Vector3D.Transform(new Vector3D((hitUv.X-.5f)*plane.Width,(.5f-hitUv.Y)*plane.Height,0),plane.Pose);
                        float distance=(float)Vector3D.Distance(from,point);
                        if(distance>Math.Min(3,(float)candidate.Distance.GetValue(candidate.Value))) continue;
                        // Physics hit before the screen means a wall/block hides the surface.
                        if(MyAPIGateway.Physics!=null && MyAPIGateway.Physics.CastRay(from,point,out IHitInfo obstacle) &&
                            obstacle.HitEntity!=MySession.Static.LocalCharacter && Vector3D.Distance(from,obstacle.Position)+.025<distance) continue;
                        float priority=direct ? Math.Abs(local.Z)-1 : distance;
                        if(priority>=bestDistance) continue;
                        best=candidate; bestPlane=plane; bestDistance=priority; intersection=point; bestDirect=direct;
                        bestHand=hand; bestTip=tip; bestOrigin=from; bestPointing=close;
                    }
                }
                // Existing physical controls have priority over a screen behind their plate.
                if(bestHand!=null && (CockpitTouch.Owns(bestHand) || bestHand==Player.HandR && SpatialUi.Current.Any(s=>s.Hover>=0))) best=null;
                var next=best?.Value; var controlled=MySession.Static?.ControlledEntity;
                if(!ReferenceEquals(selected,next) || !ReferenceEquals(owner,controlled) || origin!=Player.PlayerToAbsolute.matrix || best!=null && bestHand!=activeHand)
                {
                    if(selected!=null && screens.TryGetValue(selected,out var previous)) previous.Buttons(false,false,false);
                    primary.Block(); secondary.Block(); touch.Reset();
                }
                selected=next; owner=controlled; origin=Player.PlayerToAbsolute.matrix;
                if(best==null) { touch.Reset(); return; }
                activeHand=bestHand; rayOrigin=bestOrigin;
                bool leftHand=bestHand==Player.HandL;
                var input=primary.Read(bestHand,primary.Held ? primary.Near:bestDirect);
                bool secondaryRaw=leftHand ? Controls.Static.LeftGripPressure.RawPosition.X>InteractionInput.GripThreshold:Controls.Static.Secondary.RawPressed;
                primary.Update(true,input); secondary.Update(!input.Near,secondaryRaw);
                best.Intersection.SetValue(best.Value,intersection); best.Coordinates.Invoke(best.Value,null); best.Aiming.SetValue(best.Value,true);
                hitPoint=intersection;
                Vector3D touchTip=bestTip;
                if(bestPointing && TrackedArms.TryFingertip(bestHand,out var touchFinger)) touchTip=touchFinger;
                var touchLocal=PhysicalSurface.Point(bestPlane,touchTip); var touchUv=PhysicalSurface.UV(bestPlane,touchLocal);
                int key=touchUv.X>=0 && touchUv.X<=1 && touchUv.Y>=0 && touchUv.Y<=1 ? 0 : -1;
                int clicked=touch.Update(best.Block.EntityId+":"+best.Index,touchLocal,key);
                down=primary.Held || touch.Held>=0; secondaryDown=secondary.Held;
                OwnsInput=down || secondaryDown;
                if(OwnsInput) input.Consume();
                if(leftHand)
                {
                    // Aiming the left hand at a screen claims its trigger and grip until release.
                    Controls.Static.LeftClick.BlockUntilRelease(); Controls.Static.LeftTriggerPressure.BlockUntilRelease(false); Controls.Static.LeftGripPressure.BlockUntilRelease(false);
                    Controls.Static.CrouchOrClimbDown.BlockUntilRelease();
                }
                else
                {
                    Controls.Static.Primary.BlockUntilRelease(); Controls.Static.Secondary.BlockUntilRelease();
                    if(OwnsInput) Controls.Static.ThrustRoll.BlockUntilRelease();
                }
                if(primary.Pressed || secondary.Pressed || clicked>=0) bestHand.Vibrate(0,.025f,100,.28f);
            }
            catch(Exception ex) { failed=true; primary.Block(); secondary.Block(); selected=null; OwnsInput=Pointing=false; Logger.Warning(ex,"TouchScreenAPI VR adapter disabled; LCD rendering retained"); }
        }
        private static bool ManagerPrefix(object __instance)
        {
            if(!Main.VrActive) return true;
            if(!ReferenceEquals(__instance,manager)) return false;
            try
            {
                currentField.SetValue(manager,failed ? null : selected);
                foreach(var screen in screens.Values)
                {
                    if(failed || !ReferenceEquals(screen.Value,selected) || !InputRouter.Gameplay || Main.MenuOpen) screen.Aiming.SetValue(screen.Value,false);
                    screen.Update.Invoke(screen.Value,null);
                }
            }
            catch(Exception ex) { failed=true; selected=null; Logger.Warning(ex,"TouchScreenAPI VR dispatch disabled"); }
            return false;
        }
        private static bool ButtonsPrefix(object __instance)
        {
            if(!Main.VrActive) return true;
            if(screens.TryGetValue(__instance,out var screen))
            {
                bool active=!failed && OwnsInput && ReferenceEquals(__instance,selected) && InputRouter.Gameplay && !Main.MenuOpen;
                screen.Buttons(active,active && down,active && secondaryDown);
            }
            return false;
        }
        public static void Draw()
        {
            bool left=activeHand==Player.HandL;
            if(!left && (SpatialUi.OwnsRight || SpatialUi.RayTargeted || CockpitTouch.OwnsRight || HandInteraction.HoldingRight)) return;
            if(!OwnsInput || !InputRouter.Gameplay || Main.MenuOpen) return;
            var color=left ? PhysicalSurface.LeftLaser : new Color(85,235,255).ToVector4();
            MySimpleObjectDraw.DrawLine(hitPoint-Vector3D.Up*.004,hitPoint+Vector3D.Up*.004,MyStringId.GetOrCompute("Square"),ref color,.007f);
            if(left ? Controls.Static.LeftTriggerPressure.RawPosition.X>.06 : Controls.Static.PointerPressure.RawPosition.X>.06 || Controls.Static.Primary.RawPressed)
                MySimpleObjectDraw.DrawLine(rayOrigin,hitPoint,MyStringId.GetOrCompute("Square"),ref color,.002f);
        }
    }
}
