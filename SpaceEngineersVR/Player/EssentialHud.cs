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
using SpaceEngineers.Game.EntityComponents.GameLogic.Buffs;
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
            public string Selected, Ammo, Speed, NaturalGravity, ArtificialGravity;
            public bool Helmet, Jetpack, Dampeners, AutoDampeners, Alternate, Flying;
            public Vector3 Down;
            public bool Piloting;
            public float SpeedLevel,ShipHydrogenLevel,ShipBatteryLevel,ShipLoadLevel;
            public string ShipHydrogen,ShipBattery,ShipLoad,ShipEndurance;
            public bool Broadcasting,Flashlight,Magboots,ShipPower,ShipBroadcasting,ShipPark;
            public bool FoodEnabled,RadiationEnabled,RadiationImmunity,OxygenRefilling,HydrogenRefilling;
            public float FoodLevel,RadiationLevel;
            public string Food,Radiation,OxygenBottles,HydrogenBottles,EnvironmentOxygen,Temperature,ShipMass;
            public float InventoryLevel,ShipToolLevel;
            public string Inventory,ShipTool,ShipToolIcon;
            public bool InventoryFull,BuffsEnabled;
            public int BuffLevel;
            public Notice[] Notices=new Notice[0];
            public bool Takeover,Transmitting;
            public bool SameAs(View other) => other!=null && Selected==other.Selected && Ammo==other.Ammo && Takeover==other.Takeover && Transmitting==other.Transmitting &&
                Speed==other.Speed && SpeedLevel==other.SpeedLevel && ShipHydrogenLevel==other.ShipHydrogenLevel &&
                ShipBatteryLevel==other.ShipBatteryLevel && ShipLoadLevel==other.ShipLoadLevel && NaturalGravity==other.NaturalGravity && ArtificialGravity==other.ArtificialGravity && Helmet==other.Helmet && Jetpack==other.Jetpack &&
                Dampeners==other.Dampeners && AutoDampeners==other.AutoDampeners && Alternate==other.Alternate && Flying==other.Flying && Down==other.Down &&
                Enumerable.SequenceEqual(Values,other.Values) && Enumerable.SequenceEqual(Icons,other.Icons) && Piloting==other.Piloting &&
                ShipHydrogen==other.ShipHydrogen && ShipBattery==other.ShipBattery && ShipLoad==other.ShipLoad && ShipEndurance==other.ShipEndurance &&
                Broadcasting==other.Broadcasting && Flashlight==other.Flashlight && Magboots==other.Magboots &&
                ShipPower==other.ShipPower && ShipBroadcasting==other.ShipBroadcasting && ShipPark==other.ShipPark && ShipMass==other.ShipMass &&
                FoodEnabled==other.FoodEnabled && RadiationEnabled==other.RadiationEnabled && RadiationImmunity==other.RadiationImmunity &&
                FoodLevel==other.FoodLevel && RadiationLevel==other.RadiationLevel && Food==other.Food && Radiation==other.Radiation &&
                OxygenBottles==other.OxygenBottles && HydrogenBottles==other.HydrogenBottles &&
                OxygenRefilling==other.OxygenRefilling && HydrogenRefilling==other.HydrogenRefilling &&
                EnvironmentOxygen==other.EnvironmentOxygen && Temperature==other.Temperature &&
                InventoryLevel==other.InventoryLevel && Inventory==other.Inventory && InventoryFull==other.InventoryFull &&
                ShipToolLevel==other.ShipToolLevel && ShipTool==other.ShipTool && ShipToolIcon==other.ShipToolIcon &&
                BuffsEnabled==other.BuffsEnabled && BuffLevel==other.BuffLevel && Enumerable.SequenceEqual(Notices,other.Notices);
        }
        private static volatile View snapshot;
        public static View Current => snapshot;
        private static DateTime nextShipSample;
        private static MyShipController shipOwner;
        private static string battery="--",hydrogen="--";
        private static float batteryLevel,hydrogenLevel;
        private static View drawn;
        private static int drawnMode;
        private static OverlayCanvas canvas;
        private static DateTime nextSample;
        private static readonly Font small = new Font("Segoe UI", 21, FontStyle.Regular, GraphicsUnit.Pixel);
        private static readonly Font number = new Font("Segoe UI", 24, FontStyle.Regular, GraphicsUnit.Pixel);
        private static readonly StringFormat centered=new StringFormat { Alignment=StringAlignment.Center };
        private static readonly Color[] colors={ Color.FromArgb(125,224,159),Color.FromArgb(115,207,247),Color.FromArgb(246,178,101),Color.FromArgb(244,216,120) };
        private static readonly string[] stats = { "player_health", "player_oxygen", "player_hydrogen", "player_energy" };
        private static readonly string[] art = { "HealthIcon", "OxygenIcon", "HydrogenIcon", "EnergyIcon" };
        private static readonly Type[] buffs={typeof(MyMaxHealthBuff),typeof(MyWeldingSpeedBuff),
            typeof(MyGrindingSpeedBuff),typeof(MyMovementSpeedBuff),
            typeof(MyOxygenConsumptionRateBuff)};
        private static int iconRevision;
        private static readonly RenderRecovery recovery=new RenderRecovery("Essential HUD");
        private static bool failed => recovery.Failed;
        internal const float OverlayWidth=1.6f,OverlayHeight=OverlayWidth*560/1280,OverlayY=-.4f,OverlayDepth=1.5f;
        internal static float DisplayScale { get; private set; }=1;

        private static string notice;
        private static DateTime noticeUntil;
        private static readonly NoticeTakeover takeover=new NoticeTakeover();
        private static readonly Dictionary<string,DateTime> seen=new Dictionary<string,DateTime>();
        private sealed class Entry { public Notice Notice; public DateTime? Gone; }
        // Ordered by first appearance, so the newest notice is last.
        private static readonly List<Entry> entries=new List<Entry>();
        private const int VitalY=422,ShipY=VitalY-115,MicrophoneX=905,MicrophoneY=VitalY-44,MicrophoneSize=36;
        internal const double NoticeRepeat=20,NoticeFade=1;
        // Room for every live source (native lines, one plugin notice, chat), so trimming only drops fading entries.
        internal const int MaxNotices=NativeNotices.MaxLines+1+NativeNotices.ChatMessages,ChatRows=3,NoticeRow=28,NoticeWidth=700;
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
            var session=MySession.Static;
            var character=session?.LocalCharacter;
            var controlled=session?.ControlledEntity;
            if (!Main.WorldAvailable || character==null || character.IsDead || controlled?.Entity==null)
            { snapshot = null; return; }
            if (DateTime.UtcNow < nextSample) return;
            nextSample = DateTime.UtcNow.AddMilliseconds(100);
            var toolbar = MyToolbarComponent.CurrentToolbar;
            var item = toolbar?.SelectedSlot.HasValue == true ? toolbar.GetItemAtSlot(toolbar.SelectedSlot.Value) : null;
            var block = MyCubeBuilder.Static?.IsActivated == true ? MyCubeBuilder.Static.CurrentBlockDefinition : null;
            var weapon = character.CurrentWeapon;
            bool piloting = controlled is MyShipController;
            var ship=controlled as MyShipController;
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
            string ammo = piloting ? Patches.BuildInfoBridge.StatusText(item?.IconText?.ToString()) : weapon?.GunBase is Sandbox.Game.Weapons.MyGunBase gun ? WeaponAmmo.Count(gun) : "";
            if(!piloting && block==null && weapon==null) { selected=null; icons=null; ammo=null; }
            if (InputRouter.Mode == InputMode.Clipboard) selected = "Blueprint preview";
            if (VoxelTools.Current is VoxelTool tool && tool.Selected(out var material, out var materialIcon))
            { selected = material; icons = materialIcon == null ? null : new[] { materialIcon }; }
            var view = new View {
                Selected=selected, Icons=icons == null ? new string[0] : (string[])icons.Clone(), Ammo=ammo,
                Levels=new float[4], Values=new string[4], Helmet=On("player_helmet"), Jetpack=On("player_jetpack"),
                Dampeners=On("controlled_dampeners"), AutoDampeners=Stat("controlled_dampeners")?.CurrentValue==.5f, Alternate=ClearTerrain.Active, Flying=InputRouter.Flying,
                Speed=Stat("controlled_speed")?.GetValueString() ?? "--",
                SpeedLevel=Level("controlled_speed"),
                NaturalGravity=Stat("natural_gravity")?.GetValueString(), ArtificialGravity=Stat("artificial_gravity")?.GetValueString() };
            view.Piloting=piloting;
            if(piloting) ReadShipEquipment(ship.GridSelectionSystem?.CurrentGuns,view,ResolveDefinition);
            if(piloting && Patches.BuildInfoBridge.ShipToolFill(out float toolFill,out string toolIcon))
            { view.ShipToolLevel=toolFill; view.ShipTool=Math.Round(toolFill*100)+"%"; view.ShipToolIcon=toolIcon; }
            view.ShipHydrogen=hydrogen; view.ShipBattery=battery; view.ShipLoad=Percent("controlled_power_usage");
            view.ShipHydrogenLevel=hydrogenLevel; view.ShipBatteryLevel=batteryLevel; view.ShipLoadLevel=Level("controlled_power_usage");
            view.ShipEndurance=Stat("controlled_estimated_time_remaining_energy")?.GetValueString() ?? "--";
            view.Broadcasting=On("player_broadcasting"); view.Flashlight=piloting ? controlled.EnabledLights:On("player_flashlight"); view.Magboots=character.IsMagneticBootsActive;
            view.ShipPower=On("controlled_reactors"); view.ShipBroadcasting=On("controlled_broadcasting"); view.ShipPark=On("controlled_handbreak");
            view.ShipMass=ship?.CubeGrid.IsStatic==true ? "Station" : (Stat("controlled_mass")?.CurrentValue.ToString("N0") ?? "--")+" kg";
            view.OxygenBottles=Stat("player_oxygen_bottles")?.CurrentValue.ToString("0"); view.HydrogenBottles=Stat("player_hydrogen_bottles")?.CurrentValue.ToString("0");
            view.OxygenRefilling=On("player_refilling_oxygen"); view.HydrogenRefilling=On("player_refilling_hydrogen");
            float oxygen=Stat("environment_oxygen_level")?.CurrentValue ?? float.NaN;
            float temperature=Stat("environment_temperature_level")?.CurrentValue ?? float.NaN;
            view.EnvironmentOxygen=float.IsNaN(oxygen) ? null : oxygen<.1f ? "None" : oxygen<.8f ? "Low" : "High";
            view.Temperature=float.IsNaN(temperature) ? null : temperature<.125f ? "Freeze" : temperature<.375f ? "Cold" :
                temperature<.625f ? "Warm" : temperature<.875f ? "Hot" : "Inferno";
            view.FoodEnabled=session.Settings.FoodConsumptionRate>0;
            view.RadiationEnabled=session.Settings.EnableRadiation;
            view.Food=Stat("player_food")?.GetValueString(); view.FoodLevel=Level("player_food");
            view.Radiation=Stat("player_radiation")?.GetValueString(); view.RadiationLevel=Level("player_radiation");
            view.RadiationImmunity=On("player_radiation_immunity");
            view.Inventory=Percent("player_inventory_capacity"); view.InventoryLevel=Level("player_inventory_capacity");
            view.InventoryFull=On("player_inventory_full");
            view.BuffsEnabled=MySurvivalBuffsProgression.BuffsEnabled;
            if(view.BuffsEnabled)
                foreach(var type in buffs)
                    if(character.Components.TryGet(type,out var component) && component is MySurvivalBuffBase buff)
                        view.BuffLevel=Math.Max(view.BuffLevel,buff.Level);
            view.Transmitting=MyHud.VoiceChat?.Visible==true;
            ReadNotices(view);
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
                    view.Down=Vector3D.TransformNormal(Vector3D.Normalize(gravity),MatrixD.Transpose(controlled.Entity.WorldMatrix.GetOrientation()));
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
        internal static void Paint(OverlayCanvas target,View current,bool vitals=true,bool notifications=true,bool chat=true)
        {
            target.Clear(Color.Transparent);
            if(vitals) Vitals(target,current);
            NoticeLines(target,current,notifications,chat);
            if(current.Transmitting) Microphone(target);
        }
        private static void Vitals(OverlayCanvas target,View current)
        {
            var g=target.Graphics;
            const int left=55,right=1080,lower=8,vitalY=VitalY,shipY=ShipY,valueY=vitalY+65,environmentY=valueY+46;
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
            InventoryGauge(target,current,left+400,vitalY);
            if(current.FoodEnabled) Gauge(target,"FoodIcon",current.FoodLevel,current.Food ?? "--",left+500,vitalY,Color.Tan);
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
            environmentX=BuffReadout(target,current,environmentX,environmentY);
            Speedometer(target,current,right,374+lower);
            GravityReadout(target,current,900,451,1.15f);
            float equipmentX=Math.Max(430,environmentX+14);
            EquipmentReadout(target,current,equipmentX,514,Math.Max(0,900-equipmentX),42,true,false);
        }
        internal static IEnumerable<Notice> Shown(IEnumerable<Notice> notices,bool notifications,bool chat) =>
            notices.Where(n=>n.Style==NoticeStyle.Chat ? chat:notifications);
        // Newest at the bottom, stacked upward from the same baseline with or without vitals.
        private static void NoticeLines(OverlayCanvas target,View current,bool notifications,bool chat)
        {
            var g=target.Graphics;
            float edge=(current.Piloting ? ShipY : VitalY)-46+NoticeRow;
            using(var single=new StringFormat { FormatFlags=StringFormatFlags.NoWrap,Trimming=StringTrimming.EllipsisCharacter })
            using(var wrapped=new StringFormat { FormatFlags=StringFormatFlags.LineLimit,Trimming=StringTrimming.EllipsisWord })
                foreach(var notice in Enumerable.Reverse(Shown(current.Notices,notifications,chat).ToArray()))
                {
                    bool isChat=notice.Style==NoticeStyle.Chat;
                    int rows=isChat ? Math.Max(1,Math.Min(ChatRows,(int)Math.Round(ChatHeight(g,notice,small,NoticeWidth,wrapped)/NoticeRow))):1;
                    edge-=rows*NoticeRow;
                    if(edge<0) break;
                    var bounds=new System.Drawing.RectangleF(55,edge,NoticeWidth,rows*NoticeRow);
                    if(isChat) DrawChat(g,notice,small,bounds,wrapped);
                    else using(var brush=new SolidBrush(NoticeColor(notice))) g.DrawString(notice.Text,small,brush,bounds,single);
                }
        }
        // Same layout and colours as the wrist chat panel: sender column, message wrapped beside it.
        private static float NameWidth(Graphics g,Notice notice,Font font,float width) =>
            string.IsNullOrEmpty(notice.Sender) ? 0:Math.Min(g.MeasureString(notice.Sender+": ",font).Width,width*.4f);
        private static float ChatHeight(Graphics g,Notice notice,Font font,float width,StringFormat format) =>
            g.MeasureString(notice.Body,font,(int)(width-NameWidth(g,notice,font,width)),format).Height;
        private static void DrawChat(Graphics g,Notice notice,Font font,System.Drawing.RectangleF bounds,StringFormat format)
        {
            float name=NameWidth(g,notice,font,bounds.Width);
            using(var sender=new SolidBrush(Faded(notice.SenderColor,notice.Alpha)))
            using(var body=new SolidBrush(Faded(notice.BodyColor,notice.Alpha)))
            using(var label=new StringFormat { FormatFlags=StringFormatFlags.NoWrap,Trimming=StringTrimming.EllipsisCharacter })
            {
                if(name>0) g.DrawString(notice.Sender+": ",font,sender,new System.Drawing.RectangleF(bounds.X,bounds.Y,name,font.GetHeight(g)+2),label);
                g.DrawString(notice.Body,font,body,new System.Drawing.RectangleF(bounds.X+name,bounds.Y,bounds.Width-name,bounds.Height),format);
            }
        }
        private static Color Faded(Color color,float alpha) => Color.FromArgb((int)(color.A*MathHelper.Clamp(alpha,0,1)),color);
        private static Color NoticeColor(Notice notice) => Color.FromArgb((int)(255*MathHelper.Clamp(notice.Alpha,0,1)),
            notice.Style==NoticeStyle.Warning ? Color.LightSalmon : notice.Style==NoticeStyle.Title ? colors[3] : Color.LightCyan);
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
            // Keeps edge rows clear of the rounded corners and bezel.
            const float inset=.92f;
            target.DrawAt(target.Width*(1-inset)/2,target.Height*(1-inset)/2,inset*target.Width/640f,inset*target.Height/logicalHeight,()=> {
                var g=target.Graphics;
                if(s.Takeover) { PaintTakeover(target,s,logicalHeight); return; }
                StateIcon(target,"PlayerHelmetOn",s.Helmet,40,3);
                StateIcon(target,s.Piloting ? "GridPowerOnCenter":"JetpackOff",s.Piloting ? s.ShipPower:s.Jetpack,122,3);
                StateIcon(target,s.Piloting ? "GridBroadcastingOnCenter":"PlayerBroadcastingOnCenter",s.Piloting ? s.ShipBroadcasting:s.Broadcasting,204,3);
                StateIcon(target,"LightCenter",s.Flashlight,286,3);
                StateIcon(target,s.Piloting ? "HandbrakeCenter":"Magboot",s.Piloting ? s.ShipPark:s.Magboots,368,3);
                if(s.Transmitting) target.Icon(VoiceMarkers.Icon,451,5,28,Color.White);
                int[] order={0,3,1,2};
                // On foot the personal gauges wrap to the empty second row; piloting needs that row for ship gauges.
                bool wrap=!s.Piloting;
                int pitch=!wrap && s.FoodEnabled ? 78 : 88;
                for(int i=0;i<4;i++) Gauge(target,art[order[i]],s.Levels?[order[i]] ?? 0,s.Values?[order[i]] ?? "--",22+i*pitch,52,colors[order[i]]);
                Bottles(target,s.OxygenBottles,s.OxygenRefilling,22+2*pitch,95,colors[1]);
                Bottles(target,s.HydrogenBottles,s.HydrogenRefilling,22+3*pitch,95,colors[2]);
                Speedometer(target,s,552,36);
                GravityReadout(target,s,490,180);
                int extraX=wrap ? 22:22+4*pitch,extraY=wrap ? 153:52;
                InventoryGauge(target,s,extraX,extraY);
                if(s.FoodEnabled) Gauge(target,"FoodIcon",s.FoodLevel,s.Food ?? "--",extraX+pitch,extraY,Color.Tan);
                if(s.Notices.Length==0)
                {
                    float environmentX=EnvironmentReadout(target,"OxygenIcon",s.EnvironmentOxygen,18,310,colors[1]);
                    environmentX=EnvironmentReadout(target,"OusideTemp",s.Temperature,environmentX,310,Color.LightCyan);
                    if(s.RadiationEnabled)
                    {
                        environmentX=EnvironmentReadout(target,"RadiationIcon",s.Radiation,environmentX,310,Color.Goldenrod);
                        if(s.RadiationImmunity) { target.Icon(NativeSprites.Hud("RadiationImmunityIcon"),environmentX,310,23,Color.LightCyan); environmentX+=47; }
                    }
                    BuffReadout(target,s,environmentX,310);
                }
                if(s.Piloting)
                {
                    EquipmentReadout(target,s,18,252,374,48);
                    Gauge(target,"Battery",s.ShipBatteryLevel,Compact(s.ShipBattery),22,153,colors[3]);
                    Gauge(target,"EnergyIcon",s.ShipLoadLevel,s.ShipLoad,22+pitch,153,colors[3],false);
                    Gauge(target,"HydrogenIcon",s.ShipHydrogenLevel,Compact(s.ShipHydrogen),22+2*pitch,153,colors[2]);
                    ToolGauge(target,s,22+3*pitch,153);
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
                if(s.Notices.Length>0)
                    using(var format=new StringFormat { FormatFlags=StringFormatFlags.NoWrap,Trimming=StringTrimming.EllipsisCharacter })
                    {
                        var last=s.Notices.Last(); var bounds=new System.Drawing.RectangleF(18,307,605,28);
                        if(last.Style==NoticeStyle.Chat) DrawChat(g,last,small,bounds,format);
                        else using(var brush=new SolidBrush(NoticeColor(last))) g.DrawString(last.Text,small,brush,bounds,format);
                    }
            });
            target.Graphics.FillRectangle(Brushes.White,1020,636,4,4);
        }
        private static void PaintTakeover(OverlayCanvas target,View s,float height)
        {
            var g=target.Graphics;
            using(var title=new Font("Segoe UI",34,FontStyle.Bold,GraphicsUnit.Pixel))
            using(var body=new Font("Segoe UI",30,FontStyle.Regular,GraphicsUnit.Pixel))
            using(var format=new StringFormat { Alignment=StringAlignment.Near,Trimming=StringTrimming.EllipsisWord,FormatFlags=StringFormatFlags.LineLimit })
            {
                var sizes=s.Notices.Select(n=>(n.Style==NoticeStyle.Chat ? ChatHeight(g,n,body,600,format):g.MeasureString(n.Text,n.Style==NoticeStyle.Title ? title:body,600,format).Height)+10).ToArray();
                int first=0;
                while(first<sizes.Length-1 && sizes.Skip(first).Sum()>height-8) first++;
                float y=Math.Max(8,(height-sizes.Skip(first).Sum())/2);
                for(int i=first;i<s.Notices.Length && y<height;i++)
                {
                    var bounds=new System.Drawing.RectangleF(20,y,600,Math.Min(sizes[i],height-y));
                    if(s.Notices[i].Style==NoticeStyle.Chat) DrawChat(g,s.Notices[i],body,bounds,format);
                    else using(var brush=new SolidBrush(NoticeColor(s.Notices[i])))
                        g.DrawString(s.Notices[i].Text,s.Notices[i].Style==NoticeStyle.Title ? title:body,brush,bounds,format);
                    y+=sizes[i];
                }
            }
        }
        private static void ReadNotices(View view)
        {
            var current=NativeNotices.Read();
            NativeNotices.ReadChat(current);
            var now=DateTime.UtcNow;
            if(now<noticeUntil) current.Insert(0,new Notice(notice,NoticeStyle.Warning));
            bool fresh=false;
            foreach(var entry in entries) if(entry.Gone==null && current.All(n=>n.Text!=entry.Notice.Text)) entry.Gone=now;
            foreach(var item in current)
            {
                var entry=entries.FirstOrDefault(e=>e.Notice.Text==item.Text);
                if(entry==null) entries.Add(new Entry {Notice=item});
                else entry.Gone=null;
                if(!seen.TryGetValue(item.Text,out var last) || (now-last).TotalSeconds>NoticeRepeat) fresh=true;
                seen[item.Text]=now;
            }
            entries.RemoveAll(e=>e.Gone.HasValue && (now-e.Gone.Value).TotalSeconds>=NoticeFade);
            while(entries.Count>MaxNotices) { int gone=entries.FindIndex(e=>e.Gone.HasValue); entries.RemoveAt(Math.Max(0,gone)); }
            foreach(string old in seen.Where(p=>(now-p.Value).TotalSeconds>NoticeRepeat).Select(p=>p.Key).ToArray()) seen.Remove(old);
            // Alpha steps of 0.1 limit how often a fade republishes the HUD.
            view.Notices=entries.Select(e=>e.Notice.WithAlpha(
                e.Gone.HasValue ? (float)Math.Round(1-(now-e.Gone.Value).TotalSeconds/NoticeFade,1) : 1)).ToArray();
            view.Takeover=takeover.Update(now,fresh,entries.Count>0 && !Common.Config.DoNotDisturb);
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
        // The visor shows clear terrain on the build orientation panel instead of the item row.
        private static void EquipmentReadout(OverlayCanvas target,View s,float x,float y,float width,float size,bool center=false,bool clearTerrain=true)
        {
            if(string.IsNullOrEmpty(s.Selected) || width<=size+12) return;
            var g=target.Graphics;
            string suffix=(string.IsNullOrEmpty(s.Ammo) ? "":" ["+s.Ammo+"]")+(s.Alternate && clearTerrain ? " · Clear terrain":"");
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
        // Vanilla draws its voice icon at the bottom left while the local player transmits; here beside the first vital.
        private static void Microphone(OverlayCanvas target) =>
            target.Icon(VoiceMarkers.Icon,MicrophoneX,MicrophoneY,MicrophoneSize,Color.White);
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
        private static void InventoryGauge(OverlayCanvas target,View s,int x,int y)
        {
            if(string.IsNullOrEmpty(s.Inventory)) return;
            // Native HUD turns the backpack bar red above 79 percent.
            var color=s.InventoryFull || s.InventoryLevel>.79f ? Color.OrangeRed : Color.FromArgb(176,196,222);
            Gauge(target,"Backpack",s.InventoryLevel,s.Inventory,x,y,color,false);
        }
        private static float BuffReadout(OverlayCanvas target,View s,float x,int y)
        {
            if(!s.BuffsEnabled) return x;
            var color=s.BuffLevel>0 ? Color.FromArgb(244,216,120) : Color.FromArgb(120,170,195,210);
            target.Icon(NativeSprites.Hud("ActiveBuff"),x,y-1,22,24,new Vector4(34f/192,23f/192,124f/192,134f/192),color);
            string level=s.BuffLevel.ToString(CultureInfo.InvariantCulture);
            target.Graphics.DrawString(level,small,Brushes.LightCyan,x+27,y-2);
            return x+27+target.Graphics.MeasureString(level,small).Width+24;
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
            Ring(target,level,value,x,y,color);
            if(icon=="Battery") target.Icon(NativeSprites.Hud(icon),x+17,y+21,26,13,new Vector4(27f/128,45f/128,73f/128,37f/128),color);
            else if(icon=="Backpack") target.Icon(NativeSprites.Hud(icon),x+21,y+15,18,26,new Vector4(0,0,1,1),color);
            else target.Icon(NativeSprites.Hud(icon),x+17,y+15,26,color);
        }
        private static void Ring(OverlayCanvas target,float level,string value,int x,int y,Color color)
        {
            Arc(target.Graphics,Color.FromArgb(65,color),x,y,60,60,135,270,4);
            if(level>0) Arc(target.Graphics,color,x,y,60,60,135,270*MathHelper.Clamp(level,0,1),4);
            target.Graphics.DrawString((value ?? "--").Replace("%",""),small,Brushes.LightCyan,new System.Drawing.RectangleF(x-10,y+65,80,29),centered);
        }
        // Build Info's ship drill or grinder fill, with the icon cropped from the left end of its 1536x128 bar texture; it warns above 70 percent.
        private static void ToolGauge(OverlayCanvas target,View s,int x,int y)
        {
            if(s.ShipTool==null) return;
            Ring(target,s.ShipToolLevel,s.ShipTool,x,y,s.ShipToolLevel>.7f ? Color.OrangeRed : Color.FromArgb(136,218,240));
            if(s.ShipToolIcon.IndexOf("Grinder",StringComparison.OrdinalIgnoreCase)>=0) target.Icon(s.ShipToolIcon,x+10,y+16,40,24,new Vector4(23f/1536,41f/128,74f/1536,45f/128),Color.White);
            else target.Icon(s.ShipToolIcon,x+8,y+21,44,15,new Vector4(4f/1536,48f/128,89f/1536,31f/128),Color.White);
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
                var current=snapshot; float alpha=HelmetHud.HudAlpha; var config=Common.Config;
                bool vitals=config.ShowVitals,notifications=config.ShowNotifications,chat=config.ShowChat;
                if (alpha<=0 || Main.MenuOpen || InputRouter.RadialOpen || current==null || !vitals && !current.Transmitting && !Shown(current.Notices,notifications,chat).Any())
                { canvas?.Hide(); drawn=null; shownAlpha=-1; return; }
                int mode=(vitals ? 1:0)|(notifications ? 2:0)|(chat ? 4:0);
                if (ReferenceEquals(current,drawn) && mode==drawnMode && iconRevision==NativeSprites.Revision)
                { if(alpha!=shownAlpha) { canvas?.Show(alpha); shownAlpha=alpha; } return; }
                if (canvas==null)
                {
                    float scale=FitScale(HeadProjection(EVREye.Eye_Left),HeadProjection(EVREye.Eye_Right));
                    DisplayScale=scale;
                    canvas=new OverlayCanvas("Essential HUD",1280,560,OverlayWidth*scale);
                    canvas.Position(Matrix.CreateTranslation(0,OverlayY*scale,-OverlayDepth),true);
                    Logger.Info("HUD field-of-view fit: "+scale.ToString("0.000",CultureInfo.InvariantCulture));
                }
                Paint(canvas,current,vitals,notifications,chat);
                canvas.Upload(); canvas.Show(alpha); shownAlpha=alpha; drawn=current; drawnMode=mode; iconRevision=NativeSprites.Revision;
                recovery.Succeeded();
            }
            catch (Exception ex) { drawn=null; recovery.Fail(ex,"Essential HUD disabled"); RenderRecovery.Quietly(()=>canvas?.Hide()); }
        }
        public static void Reset() { snapshot=null; notice=null; nextSample=DateTime.MinValue; seen.Clear(); entries.Clear(); takeover.Reset(); NativeNotices.Reset(); }
        private static float shownAlpha=-1;
        public static void Hide() { canvas?.Hide(); shownAlpha=-1; }
    }
}
