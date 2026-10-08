using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using SpaceEngineersVR.Plugin;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class ArthurLcdBridge
    {
        private static readonly LcdPointing rightPose=new LcdPointing(),leftPose=new LcdPointing();
        internal static bool PoseFor(Controller controller) => Allowed && controller.pose.isTracked && (controller==Player.HandR ? rightPose:leftPose).Active(DateTime.UtcNow);
        private static readonly LcdInput rightInput=new LcdInput(),leftInput=new LcdInput();
        private static readonly List<MethodBase> patched=new List<MethodBase>();
        private static Type moduleType;
        private static MethodInfo intersection,localMatrix,clickState;
        private static FieldInfo modules,pendingModules;
        private static object selected,owner,nativeModule;
        private static Controller hand;
        private static LcdInput selectedInput;
        private static Matrix origin;
        private static MatrixD aim,screenWorld;
        private static Vector3D rayStart,rayEnd;
        private static bool dispatch,querying,failed,flushing;
        private static long prepared,dispatched;
        private static DateTime retry;
        private static LcdInput Input(Controller controller) => controller==Player.HandR ? rightInput:leftInput;
        internal static bool OwnsInput => rightInput.Reserved || leftInput.Reserved;
        internal static bool PointingFor(Controller controller) => selected!=null && controller==hand;
        internal static bool Owns(Controller controller) => controller!=null && Input(controller).Reserved;
        internal static string Status => selected==null ? OwnsInput ? "release":"idle" : (hand==Player.HandR ? "right":"left")+"/"+(near ? "near":"ray")+"/"+(selectedInput.Primary ? "primary":selectedInput.Secondary ? "secondary":OwnsInput ? "reserved":"hover");
        internal static bool Ready => moduleType!=null && !failed;
        private static object Member(object value,string name) => CockpitRender.Member(value,name);
        private static MethodInfo Method(Type type,string name) => AccessTools.Method(type,name) ?? throw new MissingMethodException(type.FullName,name);
        private static void Patch(MethodBase method,string prefix=null,string postfix=null,string transpiler=null,string finalizer=null)
        {
            Common.Plugin.Harmony.Patch(method,prefix==null ? null:new HarmonyMethod(typeof(ArthurLcdBridge),prefix),
                postfix==null ? null:new HarmonyMethod(typeof(ArthurLcdBridge),postfix),
                transpiler==null ? null:new HarmonyMethod(typeof(ArthurLcdBridge),transpiler),
                finalizer==null ? null:new HarmonyMethod(typeof(ArthurLcdBridge),finalizer));
            patched.Add(method);
        }
        internal static void RefreshRegistrations()
        {
            if(failed || moduleType!=null || DateTime.UtcNow<retry || MySession.Static==null) return;
            retry=DateTime.UtcNow.AddSeconds(1);
            var type=AppDomain.CurrentDomain.GetAssemblies().Reverse().Where(a=>
                a.GetType("LcdMod.Client.LcdModClientComponent",false) is Type client && AccessTools.Property(client,"Instance")?.GetValue(null)!=null)
                .Select(a=>a.GetType("LcdMod.Client.Modules.EyeTracking.EyeTrackingModule",false)).FirstOrDefault(t=>t!=null);
            if(type!=null) Attach(type);
        }
        internal static void Attach(Type type)
        {
            try
            {
                var assembly=type.Assembly;
                var script=assembly.GetType("LcdMod.Client.SurfaceScripts.Abstract.SurfaceScriptBase",true);
                var geometry=assembly.GetType("LcdMod.Client.ScreenAreas.ScreenAreaGeometry",true);
                intersection=AccessTools.Method(geometry,"TryGetScreenPointIntersection",new[] {script,typeof(Vector3D),typeof(Vector3D),typeof(Vector2).MakeByRefType()});
                localMatrix=AccessTools.Method(geometry,"TryGetScreenLocalMatrix",new[] {script,typeof(Matrix).MakeByRefType()});
                modules=AccessTools.Field(type,"_modules"); pendingModules=AccessTools.Field(type,"_pendingModules");
                clickState=Method(type,"UpdateClickState");
                if(intersection==null || localMatrix==null || modules==null || pendingModules==null) throw new MissingMemberException("Arthur LCD interaction interface");
                Patch(Method(type,"Update"),nameof(UpdatePrefix),nameof(UpdatePostfix),nameof(UpdateTranspiler),nameof(UpdateFinalizer));
                Patch(Method(type,"TryGetCameraRay"),nameof(CameraPrefix));
                Patch(intersection,nameof(IntersectionPrefix));
                foreach(string name in new[] {"HoldingClick","HoldingRightClick","HoldingMiddleClick","HoldingBackClick","HoldingForwardClick"})
                    Patch(AccessTools.PropertyGetter(type,name) ?? throw new MissingMemberException(name),nameof(ButtonPrefix));
                Patch(Method(type,"UpdateScrollState"),nameof(ScrollPrefix));
                var inputBlock=assembly.GetType("LcdMod.Client.Modules.InputBlock.InputBlockModule",true);
                Patch(Method(inputBlock,"Update"),nameof(InputBlockPrefix));
                moduleType=type;
                ResetInput();
                Logger.Info("Arthur LCD VR adapter attached to "+assembly.GetName().Name);
            }
            catch(Exception ex)
            {
                foreach(var method in patched) Common.Plugin.Harmony.Unpatch(method,HarmonyPatchType.All,Common.Plugin.Harmony.Id);
                patched.Clear(); failed=true;
                Logger.Warning(ex,"Arthur LCD VR adapter unavailable; LCD rendering retained");
            }
        }
        private static void ResetInput()
        {
            rightInput.Cancel(); leftInput.Cancel();
            FlushNative(); selected=owner=null; hand=null; selectedInput=null;
        }
        private static void FlushNative()
        {
            if(nativeModule==null || clickState==null || flushing) return;
            flushing=true;
            try { clickState.Invoke(nativeModule,new object[] {null,null,null}); }
            catch(Exception ex) { Logger.Warning(ex,"Arthur LCD native input cancellation failed"); }
            finally { flushing=false; }
        }
        internal static void Reset()
        {
            ResetInput(); rightPose.Reset(); leftPose.Reset(); nativeModule=null; dispatch=querying=false;
            foreach(var method in patched) Common.Plugin.Harmony.Unpatch(method,HarmonyPatchType.All,Common.Plugin.Harmony.Id);
            patched.Clear(); moduleType=null; failed=false; retry=DateTime.MinValue;
            prepared=dispatched=0;
        }
        private static bool Allowed => Main.VrActive && !failed && InputRouter.Gameplay && !Main.MenuOpen && !ThirdPersonView.Active &&
            MySession.Static?.LocalCharacter?.IsDead==false && MySession.Static.LocalCharacter.CurrentWeapon==null &&
            Player.Headset.pose.isTracked && !CockpitControls.Adjusting && !PlacementControls.OwnsTools;
        private static bool Free(Controller controller) => controller.pose.isTracked && !CockpitControls.Held(controller) &&
            !CockpitTouch.Owns(controller) && !HelmetHud.Consumes(controller) && !FloatingWindows.PointingFor(controller) &&
            !TouchScreenBridge.PointingFor(controller) && !HandInteraction.PhysicalOwns(controller) &&
            (controller!=Player.HandR || !SpatialUi.OwnsRight && !SpatialUi.RayTargeted);
        private static bool TryPlane(object screen,out MatrixD plane,out MatrixD world)
        {
            var block=Member(screen,"Block") as IMyTerminalBlock;
            plane=world=MatrixD.Identity;
            if(block==null || block.Closed || !block.HasLocalPlayerAccess()) return false;
            int index=(int)Member(screen,"RotationOrSurfaceIndex");
            object[] args={screen,Matrix.Identity};
            if(!(bool)localMatrix.Invoke(null,args)) return false;
            // Arthur's UV geometry is authored in the native block frame.
            world=CockpitRender.SurfaceWorld(block,index);
            plane=(Matrix)(args[1])*world;
            return plane.IsValid();
        }
        internal static MatrixD NativeRayFrame(MatrixD surfaceWorld,MatrixD blockWorld) => MatrixD.Invert(surfaceWorld)*blockWorld;
        private static bool Hit(object screen,MatrixD world,Vector3D from,Vector3D direction)
        {
            var block=(IMyTerminalBlock)Member(screen,"Block");
            var transform=NativeRayFrame(world,block.WorldMatrix);
            object[] args={screen,Vector3D.Transform(from,transform),Vector3D.TransformNormal(direction,transform),Vector2.Zero};
            querying=true;
            try { return (bool)intersection.Invoke(null,args); }
            finally { querying=false; }
        }
        internal static bool HitOwner(IMyTerminalBlock block,IHitInfo hit)
        {
            if(ReferenceEquals(hit.HitEntity,block)) return true;
            return hit.HitEntity is IMyCubeGrid grid && ReferenceEquals(grid.GetCubeBlock(grid.WorldToGridInteger(hit.Position))?.FatBlock,block);
        }
        private static void Select(object module)
        {
            object best=null; Controller bestHand=null; MatrixD bestAim=MatrixD.Identity,bestWorld=MatrixD.Identity;
            Vector3D bestEnd=Vector3D.Zero; float bestDistance=float.MaxValue; bool bestDirect=false;
            var previous=selected;
            bool rightHit=false,leftHit=false;
            if(Allowed)
            {
                bool firing=SeatFit.Eligible(SeatFit.Seat) && Controls.Static.Primary.IsPressed && !Controls.Static.Primary.HasPressed && !rightInput.Captured;
                var list=((IEnumerable)modules.GetValue(module)).Cast<object>().Concat(((IEnumerable)pendingModules.GetValue(module)).Cast<object>()).Distinct().ToArray();
                foreach(var controller in new[] {Player.HandR,Player.HandL})
                {
                    if(!Free(controller)) continue;
                    bool cursor=!(hand!=null && Free(hand) && Input(hand).Reserved && hand!=controller || firing && controller==Player.HandR);
                    MatrixD pointer=SpatialUi.DeviceWorld(controller.AimTracking);
                    if(TrackedArms.TryFreePointPose(controller,out var finger)) pointer=finger;
                    foreach(var screen in list)
                    {
                        long tick=(long)Member(screen,"LastRunTick");
                        if(tick==long.MinValue || MySession.Static.GameplayFrameCounter-tick>30 || !TryPlane(screen,out var plane,out var world)) continue;
                        var normal=plane.Backward;
                        var head=SpatialUi.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix).Translation;
                        if(Vector3D.Dot(normal,head-plane.Translation)<.015) continue;
                        Vector3D tip=pointer.Translation;
                        if(Vector3D.Distance(tip,plane.Translation)<.8 && TrackedArms.TryFreeFingertip(controller,out var actual)) tip=actual;
                        float depth=(float)Vector3D.Dot(normal,tip-plane.Translation);
                        bool direct=depth>=-.018f && depth<.07f && Hit(screen,world,tip+normal*.07,-normal);
                        if(!direct && (controller==Player.HandL && !PointerHand.LeftRayAllowed || !Hit(screen,world,pointer.Translation,pointer.Forward))) continue;
                        float denominator=(float)Vector3D.Dot(normal,pointer.Forward);
                        float distance=direct ? depth : Math.Abs(denominator)<1e-6f ? float.MaxValue : (float)Vector3D.Dot(normal,plane.Translation-pointer.Translation)/denominator;
                        if(!direct && (distance<0 || distance>3)) continue;
                        var point=direct ? tip-normal*depth : pointer.Translation+pointer.Forward*distance;
                        if(MyAPIGateway.Physics!=null && MyAPIGateway.Physics.CastRay(pointer.Translation,point,out IHitInfo obstacle) &&
                            obstacle.HitEntity!=MySession.Static.LocalCharacter && !HitOwner((IMyTerminalBlock)Member(screen,"Block"),obstacle) && Vector3D.Distance(pointer.Translation,obstacle.Position)+.025<Vector3D.Distance(pointer.Translation,point)) continue;
                        if(controller==Player.HandR) rightHit=true; else leftHit=true;
                        if(!cursor) continue;
                        float priority=direct ? Math.Abs(depth)-1:distance;
                        if(priority>=bestDistance) continue;
                        best=screen; bestHand=controller; bestWorld=world; bestEnd=point; bestDistance=priority; bestDirect=direct;
                        bestAim=direct ? MatrixD.CreateWorld(tip+normal*.07,-normal,plane.Up):pointer;
                    }
                }
            }
            rightPose.Update(Allowed && Player.HandR.pose.isTracked,rightHit,DateTime.UtcNow);
            leftPose.Update(Allowed && Player.HandL.pose.isTracked,leftHit,DateTime.UtcNow);
            var controlled=MySession.Static?.ControlledEntity;
            if(!ReferenceEquals(previous,best) || hand!=bestHand || !ReferenceEquals(owner,controlled) || origin!=Player.PlayerToAbsolute.matrix)
            {
                ResetInput();
            }
            selected=best; hand=bestHand; owner=controlled; origin=Player.PlayerToAbsolute.matrix;
            selectedInput=hand==null ? null:Input(hand);
            if(best==null) return;
            aim=bestAim; screenWorld=bestWorld; rayStart=bestAim.Translation; rayEnd=bestEnd;
            near=bestDirect;
        }
        private static bool near;
        private static object FindNativeModule()
        {
            if(moduleType==null) return null;
            var client=moduleType.Assembly.GetType("LcdMod.Client.LcdModClientComponent",true);
            var instance=AccessTools.Property(client,"Instance")?.GetValue(null);
            var session=instance==null ? null:Member(instance,"_session");
            return session==null ? null:(Member(session,"RegisteredModules") as IEnumerable)?.Cast<object>().FirstOrDefault(moduleType.IsInstanceOfType);
        }
        internal static void Update()
        {
            var controls=Controls.Static;
            try
            {
                if(!failed && moduleType!=null)
                {
                    if(nativeModule==null) nativeModule=FindNativeModule();
                    if(nativeModule!=null) Select(nativeModule);
                }
                foreach(var controller in new[] {Player.HandR,Player.HandL})
                {
                    bool right=controller==Player.HandR;
                    var input=Input(controller);
                    input.Update(input.Read(controls,right,InputRouter.Flying),!failed && PointingFor(controller),near,DateTime.UtcNow);
                    if(input.Pressed) controller.Vibrate(0,.025f,100,.28f);
                }
                prepared++;
            }
            catch(Exception ex) { failed=true; ResetInput(); Logger.Warning(ex,"Arthur LCD VR input disabled; LCD rendering retained"); }
            finally { if(!Allowed) { rightPose.Reset(); leftPose.Reset(); } rightInput.Consume(controls,true); leftInput.Consume(controls,false); }
        }
        private static bool UpdatePrefix(object __instance)
        {
            if(!Main.VrActive) return true;
            if(!ReferenceEquals(nativeModule,__instance))
            {
                ResetInput(); nativeModule=__instance; dispatched=prepared;
                return false;
            }
            if(failed || prepared==dispatched) return false;
            dispatched=prepared;
            try
            {
                if(!Allowed || selected!=null && (!Free(hand) || !ReferenceEquals(owner,MySession.Static?.ControlledEntity) || origin!=Player.PlayerToAbsolute.matrix)) ResetInput();
                dispatch=true;
                return true;
            }
            catch(Exception ex) { failed=true; ResetInput(); Logger.Warning(ex,"Arthur LCD VR interaction disabled; LCD rendering retained"); return false; }
        }
        private static void UpdatePostfix() { dispatch=false; }
        private static Exception UpdateFinalizer(Exception __exception)
        {
            dispatch=false;
            if(__exception!=null) { failed=true; ResetInput(); }
            return __exception;
        }
        internal static bool LookAllowed(bool native) => Main.VrActive && dispatch || native;
        private static IEnumerable<CodeInstruction> UpdateTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach(var instruction in instructions)
            {
                yield return instruction;
                if(instruction.operand is MethodInfo method && (method.Name=="IsPressed" || method.Name=="get_RequiresAlt"))
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(ArthurLcdBridge),method.Name=="IsPressed" ? nameof(LookAllowed):nameof(RequiresAlt)));
            }
        }
        private static bool RequiresAlt(bool native) => Main.VrActive && dispatch ? false:native;
        private static bool CameraPrefix(ref Vector3D __0,ref Vector3D __1,ref bool __result)
        {
            if(!Main.VrActive) return true;
            __0=aim.Translation; __1=aim.Forward; __result=selected!=null && !failed; return false;
        }
        private static bool IntersectionPrefix(object __0,ref Vector3D __1,ref Vector3D __2,ref bool __result)
        {
            if(!Main.VrActive || querying || !dispatch) return true;
            if(!ReferenceEquals(__0,selected)) { __result=false; return false; }
            var transform=NativeRayFrame(screenWorld,((IMyTerminalBlock)Member(selected,"Block")).WorldMatrix);
            __1=Vector3D.Transform(__1,transform); __2=Vector3D.TransformNormal(__2,transform); return true;
        }
        private static bool ButtonPrefix(MethodBase __originalMethod,ref bool __result)
        {
            if(flushing) { __result=false; return false; }
            if(!Main.VrActive) return true;
            __result=Allowed && selected!=null && selectedInput!=null && (__originalMethod.Name=="get_HoldingClick" ? selectedInput.Primary:__originalMethod.Name=="get_HoldingRightClick" && selectedInput.Secondary);
            return false;
        }
        private static bool InputBlockPrefix(object __instance)
        {
            if(!Main.VrActive) return true;
            Method(__instance.GetType(),"SetInputBlocked").Invoke(__instance,new object[] {false});
            return false;
        }
        private static bool ScrollPrefix(object __0)
        {
            if(!Main.VrActive) return true;
            if(!dispatch || !Allowed || !ReferenceEquals(__0,selected) || selected==null || selectedInput==null) return false;
            int value=selectedInput.TakeScroll();
            if(value!=0) Method(selected.GetType(),"MouseScroll").Invoke(selected,new object[] {value});
            return false;
        }
        internal static void Draw()
        {
            if(selected==null || hand==null || failed || !InputRouter.Gameplay || Main.MenuOpen) return;
            float pressure=(hand==Player.HandR ? Controls.Static.PointerPressure:Controls.Static.LeftTriggerPressure).RawPosition.X;
            if(pressure<.06f) return;
            var color=hand==Player.HandL ? PhysicalSurface.LeftLaser:new Color(85,235,255).ToVector4();
            MySimpleObjectDraw.DrawLine(rayStart,rayEnd,MyStringId.GetOrCompute("Square"),ref color,.002f);
        }
    }
}
