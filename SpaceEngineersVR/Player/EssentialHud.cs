using System;
using System.Drawing;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.ModAPI;
using Sandbox.Game.Weapons;
using System.Globalization;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Util;
using Valve.VR;
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
            public string Selected, Ammo, Prompt, Speed, NaturalGravity, ArtificialGravity;
            public bool Helmet, Jetpack, Dampeners, AutoDampeners, Alternate, Flying;
            public Vector3 Down;
            public bool Piloting;
            public float SpeedLevel,ShipHydrogenLevel,ShipBatteryLevel,ShipLoadLevel;
            public string ShipHydrogen,ShipBattery,ShipLoad,ShipEndurance;
            public bool Broadcasting,Flashlight,Magboots,ShipPower,ShipBroadcasting,ShipPark;
            public bool FoodEnabled,RadiationEnabled,RadiationImmunity,OxygenRefilling,HydrogenRefilling;
            public float FoodLevel,RadiationLevel;
            public string Food,Radiation,OxygenBottles,HydrogenBottles,EnvironmentOxygen,Temperature,ShipMass;
            public bool SameAs(View other) => other!=null && Selected==other.Selected && Ammo==other.Ammo && Prompt==other.Prompt &&
                Speed==other.Speed && SpeedLevel==other.SpeedLevel && ShipHydrogenLevel==other.ShipHydrogenLevel &&
                ShipBatteryLevel==other.ShipBatteryLevel && ShipLoadLevel==other.ShipLoadLevel && NaturalGravity==other.NaturalGravity && ArtificialGravity==other.ArtificialGravity && Helmet==other.Helmet && Jetpack==other.Jetpack &&
                Dampeners==other.Dampeners && AutoDampeners==other.AutoDampeners && Alternate==other.Alternate && Flying==other.Flying && Down==other.Down &&
                Values.SequenceEqual(other.Values) && Icons.SequenceEqual(other.Icons) && Piloting==other.Piloting &&
                ShipHydrogen==other.ShipHydrogen && ShipBattery==other.ShipBattery && ShipLoad==other.ShipLoad && ShipEndurance==other.ShipEndurance &&
                Broadcasting==other.Broadcasting && Flashlight==other.Flashlight && Magboots==other.Magboots &&
                ShipPower==other.ShipPower && ShipBroadcasting==other.ShipBroadcasting && ShipPark==other.ShipPark && ShipMass==other.ShipMass &&
                FoodEnabled==other.FoodEnabled && RadiationEnabled==other.RadiationEnabled && RadiationImmunity==other.RadiationImmunity &&
                FoodLevel==other.FoodLevel && RadiationLevel==other.RadiationLevel && Food==other.Food && Radiation==other.Radiation &&
                OxygenBottles==other.OxygenBottles && HydrogenBottles==other.HydrogenBottles &&
                OxygenRefilling==other.OxygenRefilling && HydrogenRefilling==other.HydrogenRefilling &&
                EnvironmentOxygen==other.EnvironmentOxygen && Temperature==other.Temperature;
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
        internal const float OverlayWidth=1.6f,OverlayHeight=OverlayWidth*560/1280,OverlayY=-.4f,OverlayDepth=1.5f;
        internal static float DisplayScale { get; private set; }=1;

        private static string notice;
        private static DateTime noticeUntil;
        public static void Notify(string message) { notice = message; noticeUntil = DateTime.UtcNow.AddSeconds(3); }
        private static VRage.ModAPI.IMyHudStat Stat(string id) => MyHud.Static == null ? null : MyHud.Stats?.GetStat(MyStringHash.GetOrCompute(id));
        private static bool On(string id) => Stat(id)?.CurrentValue > 0;

        public static void Update()
        {
            long started=FeatureTiming.Start();
            try { UpdateCore(); }
            finally { FeatureTiming.End(FeatureTiming.Area.HudSample,started); }
        }
        private static void UpdateCore()
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
            string selected = block?.DisplayNameText;
            string[] icons = block?.Icons;
            if (string.IsNullOrEmpty(selected))
            {
                selected = weapon?.DefinitionId.SubtypeName;
                if (weapon != null && MyDefinitionManager.Static.GetPhysicalItemForHandItem(weapon.DefinitionId) is MyPhysicalItemDefinition definition)
                { selected = definition.DisplayNameText; icons = definition.Icons; }
            }
            string ammo = piloting ? item?.IconText?.ToString() : weapon?.GunBase is Sandbox.Game.Weapons.MyGunBase gun ? gun.CurrentAmmo.ToString("N0") : "";
            if(!piloting && block==null && weapon==null) { selected=null; icons=null; ammo=null; }
            if (InputRouter.Mode == InputMode.Clipboard) selected = "Blueprint preview";
            var view = new View {
                Selected=selected, Icons=icons == null ? new string[0] : (string[])icons.Clone(), Ammo=ammo,
                Levels=new float[4], Values=new string[4], Helmet=On("player_helmet"), Jetpack=On("player_jetpack"),
                Dampeners=On("controlled_dampeners"), AutoDampeners=Stat("controlled_dampeners")?.CurrentValue==.5f, Alternate=GameActions.AlternateTrigger && !PlacementControls.OwnsTools, Flying=InputRouter.Flying,
                Speed=Stat("controlled_speed")?.GetValueString() ?? "--",
                SpeedLevel=Level("controlled_speed"),
                NaturalGravity=Stat("natural_gravity")?.GetValueString(), ArtificialGravity=Stat("artificial_gravity")?.GetValueString(),
                Prompt=DateTime.UtcNow < noticeUntil ? notice : null };
            view.Piloting=piloting;
            if(piloting) ReadShipEquipment(ship.GridSelectionSystem?.CurrentGuns,view,ResolveDefinition);
            view.ShipHydrogen=hydrogen; view.ShipBattery=battery; view.ShipLoad=Percent("controlled_power_usage");
            view.ShipHydrogenLevel=hydrogenLevel; view.ShipBatteryLevel=batteryLevel; view.ShipLoadLevel=Level("controlled_power_usage");
            view.ShipEndurance=Stat("controlled_estimated_time_remaining_energy")?.GetValueString() ?? "--";
            view.Broadcasting=On("player_broadcasting"); view.Flashlight=piloting ? MySession.Static.ControlledEntity.EnabledLights:On("player_flashlight"); view.Magboots=character.IsMagneticBootsActive;
            view.ShipPower=On("controlled_reactors"); view.ShipBroadcasting=On("controlled_broadcasting"); view.ShipPark=On("controlled_handbreak");
            view.ShipMass=ship?.CubeGrid.IsStatic==true ? "Station" : (Stat("controlled_mass")?.CurrentValue.ToString("N0") ?? "--")+" kg";
            view.OxygenBottles=Stat("player_oxygen_bottles")?.CurrentValue.ToString("0"); view.HydrogenBottles=Stat("player_hydrogen_bottles")?.CurrentValue.ToString("0");
            view.OxygenRefilling=On("player_refilling_oxygen"); view.HydrogenRefilling=On("player_refilling_hydrogen");
            float oxygen=Stat("environment_oxygen_level")?.CurrentValue ?? float.NaN;
            float temperature=Stat("environment_temperature_level")?.CurrentValue ?? float.NaN;
            view.EnvironmentOxygen=float.IsNaN(oxygen) ? null : oxygen<.1f ? "None" : oxygen<.8f ? "Low" : "High";
            view.Temperature=float.IsNaN(temperature) ? null : temperature<.125f ? "Freeze" : temperature<.375f ? "Cold" :
                temperature<.625f ? "Warm" : temperature<.875f ? "Hot" : "Inferno";
            view.FoodEnabled=MySession.Static.Settings.FoodConsumptionRate>0;
            view.RadiationEnabled=MySession.Static.Settings.EnableRadiation;
            view.Food=Stat("player_food")?.GetValueString(); view.FoodLevel=Level("player_food");
            view.Radiation=Stat("player_radiation")?.GetValueString(); view.RadiationLevel=Level("player_radiation");
            view.RadiationImmunity=On("player_radiation_immunity");
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

        private static MyDefinitionBase ResolveDefinition(MyDefinitionId id) =>
            MyDefinitionManager.Static.TryGetDefinition(id,out MyDefinitionBase definition) ? definition:null;
        internal static void ReadShipEquipment(IEnumerable<IMyGunObject<MyDeviceBase>> guns,View view,Func<MyDefinitionId,MyDefinitionBase> resolve)
        {
            view.Selected=null; view.Icons=new string[0]; view.Ammo=null;
            bool selected=false,hasAmmo=false; long ammo=0;
            if(guns==null) return;
            foreach(var gun in guns)
            {
                if(gun==null) continue;
                if(!selected)
                {
                    var definition=resolve(gun.DefinitionId);
                    view.Selected=definition?.DisplayNameText ?? gun.DefinitionId.SubtypeName;
                    view.Icons=definition?.Icons==null ? new string[0]:(string[])definition.Icons.Clone();
                    selected=true;
                }
                if(gun.GunBase is MyGunBase) { hasAmmo=true; ammo+=Math.Max(0,gun.GetTotalAmmunitionAmount()); }
            }
            if(hasAmmo) view.Ammo=ammo.ToString("N0");
        }
        internal static void Paint(OverlayCanvas target, View current)
        {
            target.Clear(Color.Transparent);
            var g=target.Graphics;
            const int left=55,right=1080,lower=8,vitalY=422,shipY=vitalY-115,valueY=vitalY+65,environmentY=valueY+46;
            if(current.Piloting)
            {
                Gauge(target,"Battery",current.ShipBatteryLevel,Compact(current.ShipBattery),left,shipY,colors[3]);
                Gauge(target,"EnergyIcon",current.ShipLoadLevel,current.ShipLoad,left+100,shipY,colors[3],false);
                Gauge(target,"HydrogenIcon",current.ShipHydrogenLevel,Compact(current.ShipHydrogen),left+200,shipY,colors[2]);
                string endurance=(current.ShipEndurance ?? "--").Replace(" min","m").Replace(" h","h").Replace(" d","d");
                string mass=current.ShipMass ?? "--";
                float timeWidth=g.MeasureString(endurance,small).Width,massWidth=g.MeasureString(mass,small).Width;
                float shipX=right-(timeWidth+48+massWidth)/2;
                g.DrawString(endurance,small,Brushes.LightCyan,shipX,environmentY-2);
                target.Icon(NativeSprites.Hud("Mass"),shipX+timeWidth+18,environmentY,23,Color.LightCyan);
                g.DrawString(mass,small,Brushes.LightCyan,shipX+timeWidth+48,environmentY-2);
                StateIcon(target,"GridPowerOnCenter",current.ShipPower,right-66,328+lower);
                StateIcon(target,"GridBroadcastingOnCenter",current.ShipBroadcasting,right-15,312+lower);
                StateIcon(target,"HandbrakeCenter",current.ShipPark,right+36,328+lower);
            }
            else
            {
                StateIcon(target,"PlayerHelmetOn",current.Helmet,right-107,373+lower);
                StateIcon(target,"JetpackOff",current.Jetpack,right-75,329+lower);
                StateIcon(target,"PlayerBroadcastingOnCenter",current.Broadcasting,right-15,312+lower);
                StateIcon(target,"LightCenter",current.Flashlight,right+45,329+lower);
                StateIcon(target,"Magboot",current.Magboots,right+77,373+lower);
            }
            Gauge(target,art[0],current.Levels[0],current.Values[0],left,vitalY,colors[0]);
            Gauge(target,art[3],current.Levels[3],current.Values[3],left+100,vitalY,colors[3]);
            Gauge(target,art[1],current.Levels[1],current.Values[1],left+200,vitalY,colors[1]);
            Gauge(target,art[2],current.Levels[2],current.Values[2],left+300,vitalY,colors[2]);
            Bottles(target,current.OxygenBottles,current.OxygenRefilling,left+200,vitalY+43,colors[1]);
            Bottles(target,current.HydrogenBottles,current.HydrogenRefilling,left+300,vitalY+43,colors[2]);
            if(current.FoodEnabled) Gauge(target,"FoodIcon",current.FoodLevel,current.Food ?? "--",left+400,vitalY,Color.Tan);
            float environmentX=EnvironmentReadout(target,"OxygenIcon",current.EnvironmentOxygen,left,environmentY,colors[1]);
            environmentX=EnvironmentReadout(target,"OusideTemp",current.Temperature,environmentX,environmentY,Color.LightCyan);
            if(current.RadiationEnabled && current.RadiationLevel>0)
            {
                string radiation=current.Radiation ?? "--";
                float textWidth=g.MeasureString(radiation,small).Width;
                target.Icon(NativeSprites.Hud("RadiationIcon"),environmentX,environmentY,24,Color.Goldenrod);
                g.DrawString(radiation,small,Brushes.Goldenrod,environmentX+30,environmentY-2);
                environmentX+=30+textWidth+12;
                if(current.RadiationImmunity) { target.Icon(NativeSprites.Hud("RadiationImmunityIcon"),environmentX,environmentY,24,Color.LightCyan); environmentX+=36; }
                environmentX+=12;
            }
            Speedometer(target,current,right,374+lower);
            GravityReadout(target,current,900,451,1.15f);
            float equipmentX=Math.Max(430,environmentX+14);
            EquipmentReadout(target,current,equipmentX,514,Math.Max(0,900-equipmentX),42,true);
            if(!string.IsNullOrEmpty(current.Prompt)) g.DrawString(current.Prompt,small,Brushes.LightSalmon,
                new System.Drawing.RectangleF(left,(current.Piloting ? shipY : vitalY)-64,480,56));
        }
        private static void Speedometer(OverlayCanvas target,View current,int x,int y)
        {
            int segments=SpeedSegments(current.Speed,current.SpeedLevel);
            for(int i=0;i<11;i++) Arc(target.Graphics,i<segments ? Color.LightCyan:Color.FromArgb(65,170,216,230),x-66,y,132,132,135+i*24,17,5);
            target.Icon(NativeSprites.Hud(current.AutoDampeners ? "DampenersAuto":"DampenersOn"),x-21,y+14,42,37,new Vector4(0,0,1,1),current.Dampeners ? Color.LightCyan:Color.Orange);
            target.Graphics.DrawString(current.AutoDampeners ? "AUTO":current.Dampeners ? "ON":"OFF",small,current.Dampeners ? Brushes.LightCyan:Brushes.Orange,new System.Drawing.RectangleF(x-46,y+48,92,26),centered);
            target.Graphics.DrawString(current.Speed ?? "--",number,Brushes.LightCyan,new System.Drawing.RectangleF(x-55,y+76,110,32),centered);
            target.Graphics.DrawString("m/s",small,Brushes.LightSteelBlue,new System.Drawing.RectangleF(x-46,y+105,92,30),centered);
        }
        internal static void PaintWrist(OverlayCanvas target,View s,float width,float height)
        {
            target.Clear(Color.FromArgb(255,12,20,28));
            if(s==null) return;
            float logicalHeight=width>0 && height>0 ? 640*height/width:337;
            target.DrawAt(0,2,target.Width/640f,(target.Height-4)/logicalHeight,()=> {
                var g=target.Graphics;
                StateIcon(target,"PlayerHelmetOn",s.Helmet,40,3);
                StateIcon(target,s.Piloting ? "GridPowerOnCenter":"JetpackOff",s.Piloting ? s.ShipPower:s.Jetpack,122,3);
                StateIcon(target,s.Piloting ? "GridBroadcastingOnCenter":"PlayerBroadcastingOnCenter",s.Piloting ? s.ShipBroadcasting:s.Broadcasting,204,3);
                StateIcon(target,"LightCenter",s.Flashlight,286,3);
                StateIcon(target,s.Piloting ? "HandbrakeCenter":"Magboot",s.Piloting ? s.ShipPark:s.Magboots,368,3);
                int[] order={0,3,1,2};
                for(int i=0;i<4;i++) Gauge(target,art[order[i]],s.Levels?[order[i]] ?? 0,s.Values?[order[i]] ?? "--",22+i*88,52,colors[order[i]]);
                Bottles(target,s.OxygenBottles,s.OxygenRefilling,198,95,colors[1]);
                Bottles(target,s.HydrogenBottles,s.HydrogenRefilling,286,95,colors[2]);
                Speedometer(target,s,552,36);
                GravityReadout(target,s,490,180);
                if(s.FoodEnabled) Gauge(target,"FoodIcon",s.FoodLevel,s.Food ?? "--",374,52,Color.Tan);
                if(string.IsNullOrEmpty(s.Prompt))
                {
                    float environmentX=EnvironmentReadout(target,"OxygenIcon",s.EnvironmentOxygen,18,310,colors[1]);
                    environmentX=EnvironmentReadout(target,"OusideTemp",s.Temperature,environmentX,310,Color.LightCyan);
                    if(s.RadiationEnabled)
                    {
                        environmentX=EnvironmentReadout(target,"RadiationIcon",s.Radiation,environmentX,310,Color.Goldenrod);
                        if(s.RadiationImmunity) target.Icon(NativeSprites.Hud("RadiationImmunityIcon"),environmentX,310,23,Color.LightCyan);
                    }
                }
                if(s.Piloting)
                {
                    EquipmentReadout(target,s,18,252,374,48);
                    Gauge(target,"Battery",s.ShipBatteryLevel,Compact(s.ShipBattery),22,153,colors[3]);
                    Gauge(target,"EnergyIcon",s.ShipLoadLevel,s.ShipLoad,110,153,colors[3],false);
                    Gauge(target,"HydrogenIcon",s.ShipHydrogenLevel,Compact(s.ShipHydrogen),198,153,colors[2]);
                    using(var rightAligned=new StringFormat { Alignment=StringAlignment.Far,FormatFlags=StringFormatFlags.NoWrap,Trimming=StringTrimming.EllipsisCharacter })
                    {
                        g.DrawString(s.ShipBattery ?? "--",small,Brushes.LightCyan,new System.Drawing.RectangleF(396,234,226,26),rightAligned);
                        g.DrawString(s.ShipEndurance ?? "--",small,Brushes.LightCyan,new System.Drawing.RectangleF(396,259,226,26),rightAligned);
                        string mass=s.ShipMass ?? "--";
                        float massWidth=Math.Min(198,g.MeasureString(mass,small).Width);
                        target.Icon(NativeSprites.Hud("Mass"),622-massWidth-28,286,22,Color.LightCyan);
                        g.DrawString(mass,small,Brushes.LightCyan,new System.Drawing.RectangleF(424,284,198,26),rightAligned);
                    }
                }
                else EquipmentReadout(target,s,18,244,604,56);
                if(!string.IsNullOrEmpty(s.Prompt)) g.DrawString(s.Prompt,small,Brushes.LightSalmon,new System.Drawing.RectangleF(18,307,605,28));
            });
            target.Graphics.FillRectangle(Brushes.White,1020,636,4,4);
        }
        private static void GravityReadout(OverlayCanvas target,View s,float x,float y,float scale=1)
        {
            target.DrawAt(x,y,scale,scale,()=> {
                using(var label=new Font("Segoe UI",18,FontStyle.Regular,GraphicsUnit.Pixel))
                using(var subscript=new Font("Segoe UI",12,FontStyle.Regular,GraphicsUnit.Pixel))
                {
                    string[] labels={"A","P"},values={s.ArtificialGravity,s.NaturalGravity};
                    for(int i=0;i<2;i++)
                    {
                        int row=i*24;
                        target.Graphics.DrawString(labels[i],label,Brushes.LightSteelBlue,0,row);
                        target.Graphics.DrawString("g",subscript,Brushes.LightSteelBlue,12,row+10);
                        target.Graphics.DrawString(":",label,Brushes.LightSteelBlue,22,row);
                        target.Graphics.DrawString((values[i] ?? "--")+" g",label,Brushes.LightCyan,34,row);
                    }
                }
            });
        }
        private static void EquipmentReadout(OverlayCanvas target,View s,float x,float y,float width,float size,bool center=false)
        {
            if(string.IsNullOrEmpty(s.Selected) || width<=size+12) return;
            var g=target.Graphics;
            string suffix=(string.IsNullOrEmpty(s.Ammo) ? "":" ["+s.Ammo+"]")+(s.Alternate ? "  ALT":"");
            using(var format=new StringFormat(StringFormat.GenericTypographic) { Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap })
            {
                float suffixWidth=string.IsNullOrEmpty(suffix) ? 0:g.MeasureString(suffix,small,int.MaxValue,format).Width;
                float nameWidth=Math.Min(g.MeasureString(s.Selected,small,int.MaxValue,format).Width+2,Math.Max(0,width-size-12-suffixWidth));
                if(center) x+=(width-size-12-nameWidth-suffixWidth)/2;
                foreach(string icon in s.Icons ?? new string[0]) target.Icon(icon,x,y,size);
                float textX=x+size+12,textY=y+(size-26)/2;
                if(nameWidth>0) g.DrawString(s.Selected,small,Brushes.LightCyan,new System.Drawing.RectangleF(textX,textY,nameWidth,28),format);
                if(suffixWidth>0) g.DrawString(suffix,small,Brushes.LightCyan,new System.Drawing.RectangleF(textX+nameWidth,textY,Math.Min(suffixWidth+2,width-size-12-nameWidth),28),format);
            }
        }
        private static void StateIcon(OverlayCanvas target,string icon,bool on,int x,int y)
        {
            var color=on ? Color.LightCyan : Color.FromArgb(85,170,195,210);
            // Native status textures have different transparent margins.
            System.Drawing.Rectangle crop;
            switch(icon)
            {
                case "PlayerHelmetOn": crop=new System.Drawing.Rectangle(39,62,108,108); break;
                case "JetpackOff": crop=new System.Drawing.Rectangle(59,64,69,69); break;
                case "PlayerBroadcastingOnCenter": crop=new System.Drawing.Rectangle(40,59,106,73); break;
                case "LightCenter": crop=new System.Drawing.Rectangle(42,74,106,42); break;
                case "Magboot": crop=new System.Drawing.Rectangle(41,85,106,62); break;
                case "GridPowerOnCenter": crop=new System.Drawing.Rectangle(44,40,101,111); break;
                case "GridBroadcastingOnCenter": crop=new System.Drawing.Rectangle(43,43,106,106); break;
                default: crop=new System.Drawing.Rectangle(37,50,117,90); break;
            }
            float scale=28f/Math.Max(crop.Width,crop.Height),w=crop.Width*scale,h=crop.Height*scale;
            target.Icon(NativeSprites.Hud(icon),x+(30-w)/2,y+(30-h)/2,w,h,
                new Vector4(crop.X/190f,crop.Y/190f,crop.Width/190f,crop.Height/190f),color);
            if(on) using(var pen=new Pen(Color.LightCyan,2)) target.Graphics.DrawLine(pen,x+6,y+32,x+24,y+32);
        }
        private static void Bottles(OverlayCanvas target,string count,bool refilling,int x,int y,Color color)
        {
            if(!string.IsNullOrEmpty(count) && count!="0")
            {
                using(var pen=new Pen(color,2))
                {
                    target.Graphics.DrawRectangle(pen,x+13,y+4,7,12);
                    target.Graphics.DrawRectangle(pen,x+15,y+1,3,3);
                }
                target.Graphics.DrawString(count,small,Brushes.LightCyan,x+24,y-3);
            }
            if(refilling) target.Graphics.DrawString("+",small,Brushes.LightCyan,x+46,y-3);
        }
        private static float EnvironmentReadout(OverlayCanvas target,string icon,string value,float x,int y,Color color)
        {
            if(string.IsNullOrEmpty(value)) return x;
            target.Icon(NativeSprites.Hud(icon),x,y,20,color);
            target.Graphics.DrawString(value,small,Brushes.LightCyan,x+25,y-2);
            return x+25+target.Graphics.MeasureString(value,small).Width+24;
        }
        private static string Compact(string value) => value==null ? "--" : value.StartsWith("NO ") ? "--" : value.Split(' ')[0];
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
            target.Graphics.DrawString((value ?? "--").Replace("%",""),small,Brushes.LightCyan,new System.Drawing.RectangleF(x-10,y+65,80,29),centered);
        }
        private static string Percent(string id)
        {
            var stat=Stat(id);
            return stat==null || stat.MaxValue<=0 ? "--" : (100*stat.CurrentValue/stat.MaxValue).ToString("0")+"%";
        }
        private static MatrixD HeadProjection(EVREye eye)
        {
            float l=0,r=0,t=0,b=0;
            OpenVR.System.GetProjectionRaw(eye,ref l,ref r,ref t,ref b);
            return MatrixD.Invert(OpenVR.System.GetEyeToHeadTransform(eye).ToMatrix())*VrMath.Projection(l,r,t,b,.05);
        }
        internal static float FitScale(MatrixD left,MatrixD right)
        {
            if(!Fits(0,left) || !Fits(0,right) || Fits(1,left) && Fits(1,right)) return 1;
            float lo=0,hi=1;
            for(int i=0;i<18;i++)
            {
                float mid=(lo+hi)/2;
                if(Fits(mid,left) && Fits(mid,right)) lo=mid; else hi=mid;
            }
            return lo;
        }
        private static bool Fits(float scale,MatrixD projection)
        {
            // Keep the shared plane inside both eye frusta, including asymmetric/canted eyes.
            for(int i=0;i<4;i++)
            {
                var point=new Vector4D((i%2==0 ? -.5 : .5)*OverlayWidth*scale,
                    (OverlayY+(i<2 ? -.5 : .5)*OverlayHeight)*scale,-OverlayDepth,1);
                var clip=Vector4D.Transform(point,projection);
                if(!clip.X.IsValid() || !clip.Y.IsValid() || !clip.W.IsValid() || clip.W<=0 ||
                    Math.Abs(clip.X)>.9*clip.W || Math.Abs(clip.Y)>.9*clip.W) return false;
            }
            return true;
        }
        public static void Draw()
        {
            long started=FeatureTiming.Start();
            try { DrawCore(); }
            finally { FeatureTiming.End(FeatureTiming.Area.HudPaint,started); }
        }
        private static void DrawCore()
        {
            if (failed) return;
            try
            {
                var current=snapshot;
                if (!HelmetHud.Visible || !Common.Config.ShowVitals || Main.MenuOpen || InputRouter.RadialOpen || current==null) { canvas?.Hide(); drawn=null; return; }
                if (ReferenceEquals(current,drawn) && iconRevision==NativeSprites.Revision) return;
                if (canvas==null)
                {
                    float scale=FitScale(HeadProjection(EVREye.Eye_Left),HeadProjection(EVREye.Eye_Right));
                    DisplayScale=scale;
                    canvas=new OverlayCanvas("Essential HUD",1280,560,OverlayWidth*scale);
                    canvas.Position(Matrix.CreateTranslation(0,OverlayY*scale,-OverlayDepth),true);
                    Logger.Info("HUD field-of-view fit: "+scale.ToString("0.000",CultureInfo.InvariantCulture));
                }
                Paint(canvas,current);
                canvas.Upload(); drawn=current; iconRevision=NativeSprites.Revision;
            }
            catch (Exception ex) { failed=true; Logger.Warning(ex,"Essential HUD disabled"); canvas?.Hide(); }
        }
        public static void Reset() { snapshot=null; notice=null; nextSample=DateTime.MinValue; }
        public static void Hide() => canvas?.Hide();
    }
}
