using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Util;
using Sandbox.Game.Entities.Character.Components;
using VRageMath;
using VRageRender;

namespace SpaceEngineersVR.Patches
{
    internal static class PaintGunPatches
    {
        private static readonly List<MethodBase> patched=new List<MethodBase>();
        private static readonly HashSet<Type> attempted=new HashSet<Type>();
        private static readonly FieldInfo placementFrame=AccessTools.Field(typeof(MyDefaultPlacementProvider),"m_lastUpdate");
        private static DateTime retry;
        private static Harmony owner;
        private static FieldInfo gamepad;
        internal static void RefreshRegistrations()
        {
            if(!Main.VrActive || MySession.Static==null || DateTime.UtcNow<retry) return;
            retry=DateTime.UtcNow.AddSeconds(1);
            var type=ModReflection.LiveType("Digi.PaintGun.PaintGunMod","Instance");
            if(type!=null && attempted.Add(type)) Attach(type);
        }
        internal static void Attach(Type mod,Func<string,Type> types=null,Harmony harmony=null)
        {
            Type Mod(string name) => types!=null ? types(name):mod.Assembly.GetType("Digi.PaintGun."+name,false);
            MethodInfo Find(string type,string name,params Type[] parameters) { var t=Mod(type); return t==null ? null:AccessTools.Method(t,name,parameters); }
            var targets=new (MethodInfo method,string prefix,string postfix,string finalizer)[] {
                (Find("Features.Tool.LocalToolHandler","UpdateInput",typeof(bool),typeof(bool),typeof(bool)),nameof(BeginAim),null,nameof(EndAim)),
                (Find("Systems.GameConfig","UpdateInput",typeof(bool),typeof(bool),typeof(bool)),nameof(KeyboardReads),null,null),
                (Find("Features.Palette.PaletteHUD","DrawPaletteHUD"),nameof(Hidden),null,null),
                (Find("Features.ColorPickerGUIWarning","DrawText"),nameof(Hidden),null,null),
                (Find("Features.Tool.SelectionGUI","SetGUIVisible",typeof(bool)),nameof(HidePanel),null,null),
                (Find("Features.Tool.SelectionGUI","DrawSelectionLines",typeof(MatrixD).MakeByRefType(),typeof(BoundingBoxD).MakeByRefType(),typeof(Color),typeof(float)),nameof(BeginOutline),null,nameof(EndOutline)),
                (Find("Features.Notifications","Show",typeof(int),typeof(string),typeof(string),typeof(int)),nameof(Notice),null,null)};
            var material=Mod("Features.Palette.PaintMaterial");
            var config=Mod("Systems.GameConfig");
            gamepad=config==null ? null:AccessTools.Field(config,"UsingGamepad");
            if(targets.Any(t=>t.method==null) || material==null || gamepad?.FieldType!=typeof(bool) || placementFrame==null)
            { Logger.Info("Paint Gun members not found; the mod keeps its desktop controls in VR"); return; }
            int start=patched.Count;
            try
            {
                owner=harmony ?? Common.Plugin.Harmony;
                foreach(var (method,prefix,postfix,finalizer) in targets)
                {
                    owner.Patch(method,Hook(prefix),Hook(postfix),finalizer:Hook(finalizer));
                    patched.Add(method);
                }
                PaintGun.Bind(ModReflection.StaticReader(mod,"Instance"),material);
                Logger.Info("Paint Gun adapter attached to "+mod.Assembly.GetName().Name);
            }
            catch(Exception ex) { Rollback(start); PaintGun.Bind(null,null); Logger.Warning(ex,"Paint Gun adapter unavailable"); }
        }
        private static HarmonyMethod Hook(string name) => name==null ? null:new HarmonyMethod(typeof(PaintGunPatches),name);
        private static void Rollback(int start)
        {
            foreach(var method in patched.Skip(start).ToArray())
                foreach(string name in new[] {nameof(BeginAim),nameof(EndAim),nameof(KeyboardReads),nameof(Hidden),nameof(HidePanel),nameof(Notice),nameof(BeginOutline),nameof(EndOutline)})
                    owner.Unpatch(method,AccessTools.Method(typeof(PaintGunPatches),name));
            patched.RemoveRange(start,patched.Count-start);
        }
        internal static void Reset()
        {
            Rollback(0); attempted.Clear(); retry=DateTime.MinValue; PaintGun.Bind(null,null);
        }

        // The placement ray is cached per frame.
        private static void BeginAim(out bool __state)
        {
            __state=PaintGun.Aiming;
            PaintGun.Aiming=Main.VrActive;
            if(PaintGun.Aiming) InvalidatePlacement();
        }
        private static Exception EndAim(Exception __exception,bool __state)
        {
            if(PaintGun.Aiming && !__state) InvalidatePlacement();
            PaintGun.Aiming=__state;
            return __exception;
        }
        private static void InvalidatePlacement()
        {
            if(MyBlockBuilderBase.PlacementProvider is MyDefaultPlacementProvider provider) placementFrame.SetValue(provider,int.MinValue);
        }
        // After joystick input the mod reads its trigger from a gamepad axis.
        private static bool KeyboardReads(object __instance)
        {
            if(!Main.VrActive) return true;
            gamepad.SetValue(__instance,false);
            return false;
        }
        private static bool Hidden() => !Main.VrActive;
        private static void HidePanel(ref bool set) { if(Main.VrActive) set=false; }
        // The mod repeats each outline at 1% size and distance from the camera to show it through walls; in VR that copy floats at the face.
        [ThreadStatic] private static double seeThrough;
        [ThreadStatic] private static Vector3D camera;
        private static void BeginOutline(ref MatrixD worldMatrix,ref BoundingBoxD localBB,out double __state)
        {
            __state=seeThrough;
            var main=MySector.MainCamera;
            if(!Main.VrActive || main==null) return;
            camera=main.Position;
            seeThrough=SeeThroughReach(Vector3D.Transform(localBB.Center,worldMatrix),localBB.HalfExtents.Length(),camera);
        }
        private static Exception EndOutline(Exception __exception,double __state) { seeThrough=__state; return __exception; }
        // The copy lies within 1% of (distance + radius); the outline itself only that close with the camera inside the box.
        internal static double SeeThroughReach(Vector3D center,double radius,Vector3D camera) => .02*(Vector3D.Distance(center,camera)+radius);
        internal static bool SeeThroughCopy(Vector3D point,Vector3D camera,double reach) => Vector3D.DistanceSquared(point,camera)<reach*reach;
        internal static bool Keep(MyBillboard billboard) => seeThrough<=0 || !(SeeThroughCopy(billboard.Position0,camera,seeThrough) && SeeThroughCopy(billboard.Position1,camera,seeThrough) &&
            SeeThroughCopy(billboard.Position2,camera,seeThrough) && (billboard is MyTriangleBillboard || SeeThroughCopy(billboard.Position3,camera,seeThrough)));
        // Only errors; the other notices name keyboard keys.
        private static bool Notice(string text,string font) => !Main.VrActive || text==null || font=="Red";
    }

    // Deep paint; patched at load so the mod's scripts cannot inline past it.
    [HarmonyPatch(typeof(MyCharacterWeaponPositionComponent),nameof(MyCharacterWeaponPositionComponent.IsInIronSight),MethodType.Getter)]
    internal static class PaintGunDeepPatch
    {
        private static void Postfix(ref bool __result) { if(PaintGun.Aiming) __result=PaintGun.Deep; }
    }
}
