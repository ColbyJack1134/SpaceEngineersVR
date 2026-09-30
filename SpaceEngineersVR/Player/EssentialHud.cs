using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRage.Utils;
using VRageMath;
using Color = System.Drawing.Color;

namespace SpaceEngineersVR.Player
{
    internal static class EssentialHud
    {
        internal sealed class View
        {
            public float[] Levels;
            public string[] Values, Icons;
            public string Selected, Ammo, Prompt, Speed, Gravity;
            public bool Helmet, Jetpack, Dampeners, Alternate, Flying;
            public Vector3 Down;
            public bool Piloting;
            public float SpeedLevel,ShipHydrogenLevel,ShipBatteryLevel,ShipLoadLevel;
            public string ShipHydrogen,ShipBattery,ShipLoad,ShipEndurance;
            public bool SameAs(View other) => other!=null && Selected==other.Selected && Ammo==other.Ammo && Prompt==other.Prompt &&
                Speed==other.Speed && SpeedLevel==other.SpeedLevel && ShipHydrogenLevel==other.ShipHydrogenLevel &&
                ShipBatteryLevel==other.ShipBatteryLevel && ShipLoadLevel==other.ShipLoadLevel && Gravity==other.Gravity && Helmet==other.Helmet && Jetpack==other.Jetpack &&
                Dampeners==other.Dampeners && Alternate==other.Alternate && Flying==other.Flying && Down==other.Down &&
                Values.SequenceEqual(other.Values) && Icons.SequenceEqual(other.Icons) && Piloting==other.Piloting &&
                ShipHydrogen==other.ShipHydrogen && ShipBattery==other.ShipBattery && ShipLoad==other.ShipLoad && ShipEndurance==other.ShipEndurance;
        }
        private static volatile View snapshot;
        public static View Current => snapshot;
        private static DateTime nextShipSample;
        private static MyShipController shipOwner;
        private static string battery="--",hydrogen="--";
        private static float batteryLevel,hydrogenLevel;
        private static View drawn;
        private static OverlayCanvas canvas;
        private static DateTime nextSample;
        private static readonly Font small = new Font("Segoe UI", 21, FontStyle.Regular, GraphicsUnit.Pixel);
        private static readonly Font number = new Font("Segoe UI", 24, FontStyle.Regular, GraphicsUnit.Pixel);
        private static readonly StringFormat centered=new StringFormat { Alignment=StringAlignment.Center };
        private static readonly Color[] colors={ Color.FromArgb(125,224,159),Color.FromArgb(115,207,247),Color.FromArgb(246,178,101),Color.FromArgb(244,216,120) };
        private static readonly string[] stats = { "player_health", "player_oxygen", "player_hydrogen", "player_energy" };
        private static readonly string[] art = { "HealthIcon", "OxygenIcon", "HydrogenIcon", "EnergyIcon" };
        private static int iconRevision;
        private static bool failed;
        public static string Notice { get; set; }
        private static DateTime noticeUntil;
        public static void Notify(string message) { Notice = message; noticeUntil = DateTime.UtcNow.AddSeconds(3); }
        private static VRage.ModAPI.IMyHudStat Stat(string id) => MyHud.Static == null ? null : MyHud.Stats?.GetStat(MyStringHash.GetOrCompute(id));
        private static bool On(string id) => Stat(id)?.CurrentValue > 0;

        public static void Update()
        {
            if (!Main.WorldAvailable || MySession.Static?.LocalCharacter == null) { snapshot = null; return; }
            if (DateTime.UtcNow < nextSample) return;
            nextSample = DateTime.UtcNow.AddMilliseconds(100);
            var character = MySession.Static.LocalCharacter;
            var toolbar = MyToolbarComponent.CurrentToolbar;
            var item = toolbar?.SelectedSlot.HasValue == true ? toolbar.GetItemAtSlot(toolbar.SelectedSlot.Value) : null;
            var block = MyCubeBuilder.Static?.IsActivated == true ? MyCubeBuilder.Static.CurrentBlockDefinition : null;
            var weapon = character.CurrentWeapon;
            bool piloting = MySession.Static.ControlledEntity is MyShipController;
            var ship=MySession.Static.ControlledEntity as MyShipController;
            if(ship!=null && (ship!=shipOwner || DateTime.UtcNow>=nextShipSample))
            {
                shipOwner=ship; nextShipSample=DateTime.UtcNow.AddSeconds(.5);
                var cells=ship.CubeGrid.GetFatBlocks().OfType<Sandbox.ModAPI.Ingame.IMyBatteryBlock>().ToArray();
                double max=cells.Sum(b=>(double)b.MaxStoredPower),stored=cells.Sum(b=>(double)b.CurrentStoredPower);
                battery=max>0 ? (100*stored/max).ToString("0")+"%  "+stored.ToString("0.0")+" MWh" : "NO BATTERY";
                batteryLevel=max>0 ? (float)(stored/max) : 0;
                bool hasTanks=ship.CubeGrid.GetFatBlocks().OfType<Sandbox.Game.Entities.Blocks.MyGasTank>()
                    .Any(t=>t.BlockDefinition.StoredGasId.SubtypeName=="Hydrogen");
                hydrogen=hasTanks ? Percent("controlled_hydrogen_capacity") : "NO TANK";
                hydrogenLevel=hasTanks ? Level("controlled_hydrogen_capacity") : 0;
            }
            string selected = block?.DisplayNameText ?? item?.DisplayName?.ToString();
            string[] icons = block?.Icons ?? item?.Icons;
            if (string.IsNullOrEmpty(selected))
            {
                selected = piloting ? "Ship toolbar" : weapon?.DefinitionId.SubtypeName ?? "Empty hands";
                if (weapon != null && MyDefinitionManager.Static.TryGetDefinition(weapon.DefinitionId, out MyPhysicalItemDefinition definition))
                { selected = definition.DisplayNameText; icons = definition.Icons; }
            }
            string ammo = piloting ? item?.IconText?.ToString() : weapon?.GunBase is Sandbox.Game.Weapons.MyGunBase gun ? "Ammo  " + gun.CurrentAmmo : "";
            string target = "";
            if (InputRouter.Mode == InputMode.Clipboard) selected = "Blueprint preview";
            var view = new View {
                Selected=selected, Icons=icons == null ? new string[0] : (string[])icons.Clone(), Ammo=ammo,
                Levels=new float[4], Values=new string[4], Helmet=On("player_helmet"), Jetpack=On("player_jetpack"),
                Dampeners=On("controlled_dampeners"), Alternate=GameActions.AlternateTrigger && !PlacementControls.OwnsTools, Flying=InputRouter.Flying,
                Speed=Stat("controlled_speed")?.GetValueString() ?? "--",
                SpeedLevel=Level("controlled_speed"),
                Gravity=(Stat("natural_gravity")?.GetValueString() ?? "--") + " / " + (Stat("artificial_gravity")?.GetValueString() ?? "--") + " g",
                Prompt=DateTime.UtcNow < noticeUntil ? Notice : target };
            view.Piloting=piloting;
            view.ShipHydrogen=hydrogen; view.ShipBattery=battery; view.ShipLoad=Percent("controlled_power_usage");
            view.ShipHydrogenLevel=hydrogenLevel; view.ShipBatteryLevel=batteryLevel; view.ShipLoadLevel=Level("controlled_power_usage");
            view.ShipEndurance=Stat("controlled_estimated_time_remaining_energy")?.GetValueString() ?? "--";
            for (int i=0;i<4;i++)
            {
                var stat=Stat(stats[i]);
                view.Values[i]=stat?.GetValueString() ?? "--";
                view.Levels[i]=stat == null || stat.MaxValue<=0 ? 0 : MathHelper.Clamp(stat.CurrentValue/stat.MaxValue,0,1);
            }
            if (view.Flying)
            {
                Vector3 gravity=Sandbox.Game.GameSystems.MyGravityProviderSystem.CalculateTotalGravityInPoint(character.PositionComp.GetPosition());
                if (gravity.LengthSquared()>0.001f)
                    view.Down=Vector3D.TransformNormal(Vector3D.Normalize(gravity),MatrixD.Transpose(MySession.Static.ControlledEntity.Entity.WorldMatrix.GetOrientation()));
            }
            if (!view.SameAs(snapshot)) snapshot=view;
        }

        internal static void Paint(OverlayCanvas target, View current)
        {
            target.Clear(Color.Transparent);
            var g=target.Graphics;
            // Lower-right cluster: life support above suit fuel/power; motion to its left.
            if(current.Piloting)
            {
                Gauge(target,"Battery",current.ShipBatteryLevel,Compact(current.ShipBattery),1010,328,colors[3]);
                Gauge(target,"EnergyIcon",current.ShipLoadLevel,current.ShipLoad,1100,328,colors[3],false);
                Gauge(target,"HydrogenIcon",current.ShipHydrogenLevel,Compact(current.ShipHydrogen),1100,436,colors[2]);
                g.DrawString(current.ShipEndurance ?? "--",small,Brushes.LightCyan,new System.Drawing.RectangleF(980,454,114,68),centered);
                CompactVital(target,0,current,1175,344);
                CompactVital(target,1,current,1175,420);
            }
            else
            {
                Gauge(target,art[0],current.Levels[0],current.Values[0],1010,328,colors[0]);
                Gauge(target,art[1],current.Levels[1],current.Values[1],1100,328,colors[1]);
                Gauge(target,art[3],current.Levels[3],current.Values[3],1010,436,colors[3]);
                Gauge(target,art[2],current.Levels[2],current.Values[2],1100,436,colors[2]);
            }
            int speedSegments=SpeedSegments(current.Speed,current.SpeedLevel);
            for(int i=0;i<11;i++)
                Arc(g,i<speedSegments ? Color.LightCyan : Color.FromArgb(65,170,216,230),
                    862,406,96,96,135+i*24,17,5);
            g.DrawString(current.Speed,number,Brushes.LightCyan,new System.Drawing.RectangleF(863,430,94,32),centered);
            g.DrawString("m/s",small,Brushes.LightSteelBlue,new System.Drawing.RectangleF(863,459,94,30),centered);
            target.Icon(NativeSprites.Hud("Dampeners"),873,359,32,30,new Vector4(43f/192,46f/192,106f/192,99f/192),
                current.Dampeners ? Color.LightCyan : Color.Orange);
            if(!current.Piloting)
            {
                target.Icon(NativeSprites.Hud("JetpackOff"),926,359,30,30,new Vector4(59f/190,64f/190,69f/190,69f/190),
                    current.Jetpack ? Color.LightCyan : Color.FromArgb(100,150,165,170));
                foreach(string icon in current.Icons) target.Icon(icon,771,462,36);
                if(!string.IsNullOrEmpty(current.Ammo)) g.DrawString(current.Ammo.Replace("Ammo  ",""),small,Brushes.LightCyan,809,474);
            }
            if(current.Alternate) g.DrawString("ALT",small,Brushes.Orange,770,510);
            if(!string.IsNullOrEmpty(current.Prompt)) g.DrawString(current.Prompt,small,Brushes.LightCyan,
                new System.Drawing.RectangleF(250,526,780,32),centered);
        }
        private static string Compact(string value) => value==null ? "--" : value.StartsWith("NO ") ? "--" : value.Split(' ')[0];
        private static void CompactVital(OverlayCanvas target,int index,View current,int x,int y)
        {
            var color=current.Levels[index]<.2f && current.Values[index]!="--" ? Color.OrangeRed : colors[index];
            target.Icon(NativeSprites.Hud(art[index]),x,y,25,color);
            target.Graphics.DrawString(current.Values[index] ?? "--",small,Brushes.LightCyan,x+30,y-1);
            Arc(target.Graphics,Color.FromArgb(65,color),x-5,y-5,35,35,135,270,2);
            if(current.Levels[index]>0) Arc(target.Graphics,color,x-5,y-5,35,35,135,270*current.Levels[index],2);
        }
        internal static int SpeedSegments(string displayed,float level)
        {
            // Native speed text is rounded and localized. Sub-display physics
            // drift must not light a segment while that same readout says zero.
            if(!level.IsValid() || level<=0 ||
                (double.TryParse(displayed,NumberStyles.Float,CultureInfo.CurrentCulture,out double speed) && speed==0)) return 0;
            return (int)Math.Ceiling(MathHelper.Clamp(level,0,1)*11);
        }
        private static float Level(string id)
        {
            var stat=Stat(id);
            return stat==null || stat.MaxValue<=0 ? 0 : MathHelper.Clamp(stat.CurrentValue/stat.MaxValue,0,1);
        }
        private static void Arc(Graphics g,Color color,float x,float y,float w,float h,float start,float sweep,float thickness)
        {
            using(var pen=new Pen(color,thickness) { StartCap=System.Drawing.Drawing2D.LineCap.Round,EndCap=System.Drawing.Drawing2D.LineCap.Round })
                g.DrawArc(pen,x,y,w,h,start,sweep);
        }
        private static void Gauge(OverlayCanvas target,string icon,float level,string value,int x,int y,Color color,bool warnLow=true)
        {
            if(warnLow && level<.2f && value!="--") color=Color.OrangeRed;
            Arc(target.Graphics,Color.FromArgb(65,color),x,y,60,60,135,270,4);
            if(level>0) Arc(target.Graphics,color,x,y,60,60,135,270*MathHelper.Clamp(level,0,1),4);
            if(icon=="Battery") target.Icon(NativeSprites.Hud(icon),x+17,y+21,26,13,new Vector4(27f/128,45f/128,73f/128,37f/128),color);
            else target.Icon(NativeSprites.Hud(icon),x+17,y+15,26,color);
            target.Graphics.DrawString(value ?? "--",small,Brushes.LightCyan,new System.Drawing.RectangleF(x-10,y+65,80,29),centered);
        }
        private static string Percent(string id)
        {
            var stat=Stat(id);
            return stat==null || stat.MaxValue<=0 ? "--" : (100*stat.CurrentValue/stat.MaxValue).ToString("0")+"%";
        }
        public static void Draw()
        {
            if (failed) return;
            try
            {
                var current=snapshot;
                if (!HelmetHud.Visible || Main.MenuOpen || InputRouter.RadialOpen || current==null) { canvas?.Hide(); drawn=null; return; }
                if (ReferenceEquals(current,drawn) && iconRevision==NativeSprites.Revision) return;
                if (canvas==null)
                {
                    canvas=new OverlayCanvas("Essential HUD",1280,560,1.6f);
                    canvas.Position(Matrix.CreateTranslation(0,-0.40f,-1.5f),true);
                }
                Paint(canvas,current);
                canvas.Upload(); drawn=current; iconRevision=NativeSprites.Revision;
            }
            catch (Exception ex) { failed=true; Logger.Warning(ex,"Essential HUD disabled"); canvas?.Hide(); }
        }
        public static void Reset() { snapshot=null; Notice=null; nextSample=DateTime.MinValue; }
        public static void Hide() => canvas?.Hide();
    }
}
