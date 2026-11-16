using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.Gui;
using SharpDX.Direct3D11;
using VRage.Game.Gui;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class HudEmp
    {
        internal sealed class Phantom
        {
            internal Vector3D Position;
            internal float[] Slices;
        }
        internal sealed class Frame
        {
            internal Phantom[] Markers;
            internal DateTime Time;
        }
        private static readonly FieldInfo markers=AccessTools.Field(typeof(MyHudEmpDistortion),"m_markers");
        private static readonly Type marker=markers.FieldType.GetElementType();
        private static readonly FieldInfo position=AccessTools.Field(marker,"WorldPosition"),visible=AccessTools.Field(marker,"Visible");
        private static readonly Func<float,float,float> random=AccessTools.MethodDelegate<Func<float,float,float>>(AccessTools.Method(typeof(MyHudEmpDistortion),"NextFloat"));
        private static volatile Frame captured;
        internal static Frame Current => captured;
        internal static int Tick => Sandbox.MySandboxGame.TotalGamePlayTimeInMilliseconds/40;
        internal static IEnumerable<NativeSprite> Slices(NativeSprite sprite,int? tick,float scale)
        {
            if(!tick.HasValue) { yield return sprite; yield break; }
            int seed=unchecked(tick.Value*397 ^ (int)sprite.Bounds.X*613 ^ (int)sprite.Bounds.Y*997);
            for(int i=0;i<6;i++)
            {
                var slice=sprite;
                // A captured tick keeps both eyes identical without sharing the game's
                // mutable random generator.
                float offset=(VRage.Library.Utils.MyHashRandomUtils.UniformFloatFromSeed(unchecked(seed+i*7919))*2-1)*4*scale;
                slice.Bounds=new RectangleF(sprite.Bounds.X+offset,sprite.Bounds.Y+sprite.Bounds.Height*i/6,sprite.Bounds.Width,sprite.Bounds.Height/6);
                slice.UV.Y+=sprite.UV.W*i/6; slice.UV.W/=6;
                yield return slice;
            }
        }
        internal static void Reset() { captured=null; }
        internal static void Capture()
        {
            if(!Plugin.Main.VrActive || !MyHudEmpDistortion.IsActive || HelmetHud.NativeSuppressed) { captured=null; return; }
            var result=new List<Phantom>();
            foreach(var item in (Array)markers.GetValue(null))
            {
                if(!(bool)visible.GetValue(item)) continue;
                var point=(Vector3D)position.GetValue(item);
                if(!point.IsValid()) continue;
                var slices=new float[6];
                for(int i=0;i<slices.Length;i++) slices[i]=random(-4,4)/24;
                result.Add(new Phantom {Position=point,Slices=slices});
            }
            captured=new Frame {Markers=result.ToArray(),Time=DateTime.UtcNow};
        }
        internal static void UpdateHidden(MyGuiScreenHudSpace hud)
        {
            if(HelmetHud.NativeSuppressed) { Reset(); return; }
            if(!MyHud.IsHudMinimal && !MyHud.MinimalHud && !MyHud.CutsceneHud) return;
            RemoteHud.Discarding++;
            try { MyHudEmpDistortion.Draw(hud); }
            finally { RemoteHud.Discarding--; }
        }
        internal static void Draw(Texture2D target,Frame frame,MatrixD view,MatrixD projection,Func<Vector3D,bool> inView)
        {
            if(frame==null || (DateTime.UtcNow-frame.Time).TotalSeconds>1) return;
            var output=new List<NativeSprite>();
            foreach(var phantom in frame.Markers)
            {
                if(!inView(phantom.Position) || !MarkerBillboard.TryCreatePixels(phantom.Position,24,view,projection,target.Description.Width,out var board)) continue;
                for(int i=0;i<phantom.Slices.Length;i++)
                {
                    var sprite=SignalPainter.Artwork(SignalPainter.Atlas(MyHudTexturesEnum.Target_neutral,new VRageMath.Color(227,62,63).ToVector4()));
                    sprite.UV.Y+=sprite.UV.W*i/phantom.Slices.Length; sprite.UV.W/=phantom.Slices.Length;
                    if(board.Project(new RectangleF(-.5f+phantom.Slices[i],-.5f+(float)i/phantom.Slices.Length,1,1f/phantom.Slices.Length),view,projection,ref sprite)) output.Add(sprite);
                }
            }
            NativeSprites.Draw(target,output);
        }
        private static string Reading(string value,string stat) => MyHudEmpDistortion.ScrambleReading(value,(int)MyStringHash.GetOrCompute(stat));
        private static void Reading(ref float level,ref string value,string stat)
        {
            int seed=(int)MyStringHash.GetOrCompute(stat);
            level=MyHudEmpDistortion.ScrambleReading(level,0,1,seed);
            value=MyHudEmpDistortion.ScrambleReading(value,seed);
        }
        internal static void Apply(EssentialHud.View view)
        {
            for(int i=0;i<EssentialHud.Stats.Length;i++) Reading(ref view.Levels[i],ref view.Values[i],EssentialHud.Stats[i]);
            Reading(ref view.SpeedLevel,ref view.Speed,"controlled_speed");
            Reading(ref view.ShipHydrogenLevel,ref view.ShipHydrogen,"controlled_hydrogen_capacity");
            Reading(ref view.ShipBatteryLevel,ref view.ShipBattery,"controlled_battery");
            Reading(ref view.ShipLoadLevel,ref view.ShipLoad,"controlled_power_usage");
            Reading(ref view.FoodLevel,ref view.Food,"player_food");
            Reading(ref view.RadiationLevel,ref view.Radiation,"player_radiation");
            Reading(ref view.InventoryLevel,ref view.Inventory,"player_inventory_capacity");
            Reading(ref view.ShipToolLevel,ref view.ShipTool,"controlled_tool_capacity");
            view.ShipEndurance=Reading(view.ShipEndurance,"controlled_estimated_time_remaining_energy");
            view.ShipMass=Reading(view.ShipMass,"controlled_mass");
            view.NaturalGravity=Reading(view.NaturalGravity,"natural_gravity");
            view.ArtificialGravity=Reading(view.ArtificialGravity,"artificial_gravity");
            view.OxygenBottles=Reading(view.OxygenBottles,"player_oxygen_bottles");
            view.HydrogenBottles=Reading(view.HydrogenBottles,"player_hydrogen_bottles");
            view.EnvironmentOxygen=Reading(view.EnvironmentOxygen,"environment_oxygen_level");
            view.Temperature=Reading(view.Temperature,"environment_temperature_level");
            view.Ammo=Reading(view.Ammo,"controlled_ammo");
            view.EmpTick=Tick;
        }
    }
}
