using System;
using System.Drawing;
using System.Linq;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRage.Game;
using VRageMath;
using Color=System.Drawing.Color;

namespace SpaceEngineersVR.Player
{
    internal static class BlockInspection
    {
        internal sealed class Data
        {
            public float Built,Integrity,Critical,Ownership;
            public bool Preview;
            public int Pcu;
            public MyHudBlockInfo.ComponentInfo[] Components;
        }
        private static Data data;
        private static readonly MyHudBlockInfo info=new MyHudBlockInfo();
        private static DateTime next;
        private static string title,text;
        private static MySlimBlock previous;
        public static SurfaceView Current { get; private set; }
        public static bool ConsumesSecondary { get; private set; }
        internal static bool Visible(bool grip,bool automatic) => grip!=automatic;
        public static void Reset() { Current=null; previous=null; title=text=null; next=DateTime.MinValue; ConsumesSecondary=false; }
        public static void Update()
        {
            Current=null; ConsumesSecondary=false;
            var character=MySession.Static?.LocalCharacter;
            bool grip=Controls.Static.Secondary.RawPressed;
            if(!grip && !Common.Config.InspectWithoutGrip) return;
            if(!InputRouter.Gameplay || Main.MenuOpen || character==null || character.IsSitting ||
                MySession.Static.ControlledEntity!=character ||
                HelmetHud.NearHead(Player.HandR.GripTracking,Player.Headset.pose.deviceToAbsolute.matrix) ||
                SpatialUi.Pointing || SpatialUi.OwnsRight || CockpitTouch.OwnsRight || HandInteraction.OwnsRight || !Player.HandR.pose.isTracked) return;
            if(!TrackedArms.TryFreePointPose(Player.HandR,out var finger) || !HandInteraction.TryInteractionRay(out var ray)) return;
            var block=Target(ray);
            var definition=block?.BlockDefinition ?? (PlacementControls.OwnsTools ? MyCubeBuilder.Static?.CurrentBlockDefinition : null);
            if(definition==null) { title=text=null; return; }
            ConsumesSecondary=grip && InputRouter.Mode==InputMode.Walking;
            if(ConsumesSecondary) Controls.Static.Secondary.BlockUntilRelease();
            if(!Visible(grip,Common.Config.InspectWithoutGrip)) return;
            if(DateTime.UtcNow>=next || previous!=block || title!=definition.DisplayNameText)
            {
                previous=block;
                next=DateTime.UtcNow.AddMilliseconds(100);
                if(block!=null) MySlimBlock.SetBlockComponents(info,block,character.GetInventory());
                else MySlimBlock.SetBlockComponents(info,definition,character.GetInventory());
                title=definition.DisplayNameText;
                data=new Data { Built=block?.BuildLevelRatio ?? 0,Integrity=block==null ? 0:block.Integrity/block.MaxIntegrity,
                    Critical=info.CriticalIntegrity,Ownership=info.OwnershipIntegrity,Preview=block==null,Pcu=definition.PCU,Components=Group(info.Components.ToArray()) };
                text=Describe(block?.BuildLevelRatio ?? 0,block==null ? 0 : block.Integrity/block.MaxIntegrity,
                    block==null,definition.PCU,info.Components.ToArray());
            }
            var head=SpatialUi.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix);
            Current=new SurfaceView { Id="Block inspection",Style=SurfaceStyle.BlockInfo,Title=title,Text=text,
                Block=data,Icons=definition.Icons ?? new string[0],Width=.30f,Height=Height(data.Components.Length),Pose=CockpitTouch.LabelPose(head,finger.Translation,.30f) };
        }
        private static MySlimBlock Target(LineD ray)
        {
            var hit=MyEntities.GetIntersectionWithLine(ref ray,MySession.Static.LocalCharacter,null,ignoreChildren:false,ignoreFloatingObjects:false);
            return hit.HasValue ? BlockForHit(hit.Value.UserObject,hit.Value.Entity):null;
        }
        internal static MySlimBlock BlockForHit(object geometry,VRage.ModAPI.IMyEntity entity)
        {
            if(geometry is MyCube cube) return cube.CubeBlock;
            while(entity!=null && !(entity is MyCubeBlock)) entity=entity.Parent;
            return (entity as MyCubeBlock)?.SlimBlock;
        }
        internal static string Describe(float built,float integrity,bool preview,int pcu,MyHudBlockInfo.ComponentInfo[] components)
        {
            built=MathHelper.Clamp(built,0,1); integrity=MathHelper.Clamp(integrity,0,built);
            var result=new StringBuilder(preview ? "Component cost" : "Built "+built.ToString("P0")+"    Integrity "+integrity.ToString("P0"));
            if(!preview && built-integrity>.001f) result.Append("    Damaged");
            result.AppendLine().Append("PCU ").Append(pcu);
            var rows=components.GroupBy(c=>c.DefinitionId).Select(g=>new {
                Name=g.First().ComponentName,Total=g.Sum(c=>c.TotalCount),Mounted=g.Sum(c=>c.MountedCount),
                Stock=g.Sum(c=>c.StockpileCount),Have=g.Max(c=>c.AvailableAmount) }).ToArray();
            foreach(var row in rows)
            {
                int need=Math.Max(0,row.Total-row.Mounted-row.Stock);
                result.AppendLine().Append(row.Name).Append("  ").Append(row.Mounted+row.Stock).Append('/').Append(row.Total);
                if(need>0) result.Append("  need ").Append(need).Append("  have ").Append(row.Have);
            }
            return result.ToString();
        }
        internal static MyHudBlockInfo.ComponentInfo[] Group(MyHudBlockInfo.ComponentInfo[] components) => components.GroupBy(c=>c.DefinitionId).Select(g=>new MyHudBlockInfo.ComponentInfo {
            DefinitionId=g.Key,ComponentName=g.First().ComponentName,Icons=g.First().Icons,TotalCount=g.Sum(c=>c.TotalCount),
            MountedCount=g.Sum(c=>c.MountedCount),StockpileCount=g.Sum(c=>c.StockpileCount),AvailableAmount=g.Max(c=>c.AvailableAmount) }).ToArray();
        internal static float Height(int rows) => .065f+.0135f*rows;
        internal static void Paint(OverlayCanvas canvas,SurfaceView view)
        {
            var data=view.Block;
            canvas.Clear(Color.Transparent);
            if(data==null) return;
            float height=216+45*data.Components.Length;
            float sx=canvas.Width/1000f,sy=canvas.Height/height;
            var g=canvas.Graphics; var state=g.Save();
            g.ScaleTransform(sx,sy);
            using(var path=PhysicalSurface.Rounded(new System.Drawing.RectangleF(1,1,998,height-2),18))
            using(var fill=new SolidBrush(Color.FromArgb(237,12,24,33))) g.FillPath(fill,path);
            using(var name=new Font("Segoe UI",29,FontStyle.Bold,GraphicsUnit.Pixel))
            using(var font=new Font("Segoe UI",24,FontStyle.Regular,GraphicsUnit.Pixel))
            using(var small=new Font("Segoe UI",20,FontStyle.Regular,GraphicsUnit.Pixel))
            using(var format=new StringFormat { Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap })
            using(var right=new StringFormat { Alignment=StringAlignment.Far })
            {
                g.DrawString(view.Title ?? "",name,Brushes.White,new System.Drawing.RectangleF(86,14,880,43),format);
                float built=MathHelper.Clamp(data.Built,0,1),integrity=MathHelper.Clamp(data.Integrity,0,built);
                g.DrawString(data.Preview ? "Build cost" : "Built "+built.ToString("P0")+"   Integrity "+integrity.ToString("P0"),font,Brushes.LightCyan,86,58);
                NativeBar(g,new System.Drawing.RectangleF(24,105,952,23),built,integrity,data.Critical,data.Ownership);
                g.DrawString("Functional "+data.Critical.ToString("P0"),small,Brushes.LightSteelBlue,24,134);
                if(data.Ownership>0) g.DrawString("Hack "+data.Ownership.ToString("P0"),small,Brushes.LightSteelBlue,360,134);
                if(built-integrity>.001f) g.DrawString("Damage "+(built-integrity).ToString("P0"),small,Brushes.Salmon,976,134,right);
                g.DrawString("Components",small,Brushes.LightSteelBlue,24,170);
                g.DrawString("Installed / Total",small,Brushes.LightSteelBlue,674,170,right);
                g.DrawString("Need",small,Brushes.LightSteelBlue,818,170,right);
                g.DrawString("Have",small,Brushes.LightSteelBlue,976,170,right);
                for(int i=0;i<data.Components.Length;i++)
                {
                    var row=data.Components[i]; float y=201+i*45;
                    int installed=row.MountedCount+row.StockpileCount,need=Math.Max(0,row.TotalCount-installed);
                    using(var fill=new SolidBrush(Color.FromArgb(90,35,55,66))) g.FillRectangle(fill,16,y-1,968,42);
                    g.DrawString(row.ComponentName,font,Brushes.White,new System.Drawing.RectangleF(72,y+2,415,37),format);
                    g.DrawString(installed+" / "+row.TotalCount,font,Brushes.White,674,y+2,right);
                    g.DrawString(need==0 ? "✓":need.ToString(),font,need==0 ? Brushes.LightGreen:Brushes.White,818,y+2,right);
                    g.DrawString(row.AvailableAmount.ToString(),font,row.AvailableAmount>=need ? Brushes.LightGreen:Brushes.Salmon,976,y+2,right);
                }
            }
            g.Restore(state);
            foreach(var icon in view.Icons) canvas.Icon(icon,18*sx,14*sy,58*sx,58*sy,new Vector4(0,0,1,1),Color.White);
            for(int i=0;i<data.Components.Length;i++)
                foreach(var icon in data.Components[i].Icons ?? new string[0]) canvas.Icon(icon,24*sx,(201+i*45)*sy,36*sx,36*sy,new Vector4(0,0,1,1),Color.White);
            // PCU shares the title line so the complete recipe needs no extra footer.
            state=g.Save(); g.ScaleTransform(sx,sy);
            using(var font=new Font("Segoe UI",18,FontStyle.Regular,GraphicsUnit.Pixel))
            using(var right=new StringFormat { Alignment=StringAlignment.Far }) g.DrawString("PCU "+data.Pcu,font,Brushes.LightSteelBlue,976,65,right);
            g.Restore(state);
        }
        internal static void NativeBar(Graphics g,System.Drawing.RectangleF r,float built,float integrity,float critical,float ownership)
        {
            // Native block-info composite: square top and a 64:40 sloped lower-left corner.
            var points=new[] {new PointF(r.Left,r.Top),new PointF(r.Right,r.Top),new PointF(r.Right,r.Bottom),new PointF(r.Left+r.Height*1.6f,r.Bottom)};
            using(var path=new System.Drawing.Drawing2D.GraphicsPath())
            {
                path.AddPolygon(points); var state=g.Save(); g.SetClip(path);
                using(var background=new SolidBrush(Color.FromArgb(68,77,86))) g.FillRectangle(background,r);
                using(var unfinished=new SolidBrush(Color.FromArgb(139,182,196))) g.FillRectangle(unfinished,r.X,r.Y,r.Width*built,r.Height);
                using(var fill=new SolidBrush(integrity>=critical ? Color.FromArgb(122,140,154):Color.FromArgb(115,69,80))) g.FillRectangle(fill,r.X,r.Y,r.Width*integrity,r.Height);
                if(built>integrity) using(var lost=new SolidBrush(Color.FromArgb(220,146,82))) g.FillRectangle(lost,r.X+r.Width*integrity,r.Y,r.Width*(built-integrity),r.Height);
                using(var marker=new Pen(Color.FromArgb(115,69,80),3)) g.DrawLine(marker,r.X+r.Width*critical,r.Y,r.X+r.Width*critical,r.Bottom);
                if(ownership>0) using(var marker=new Pen(Color.FromArgb(56,67,147),3)) g.DrawLine(marker,r.X+r.Width*ownership,r.Y,r.X+r.Width*ownership,r.Bottom);
                g.Restore(state);
            }
        }
    }
}
