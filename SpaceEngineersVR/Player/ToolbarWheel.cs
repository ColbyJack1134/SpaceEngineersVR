using System;
using System.Drawing;
using System.Linq;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRageMath;
using Color = System.Drawing.Color;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Player.Control;

namespace SpaceEngineersVR.Player
{
    internal static class ToolbarWheel
    {
        internal const string ControlsHint="Hold B · A: assign / G menu · Empty slot: assign · Release B confirms\nLeft stick selects · Left / right trigger: page · Center cancels";
        internal enum SlotResult { Ignored, Assigned, Activated, Unavailable }
        internal sealed class View
        {
            public string Title, Hint;
            public string[] Labels, SubIcons, ItemText;
            public string[][] Icons;
            public bool[] Enabled;
            public bool Variants;
            public int Selected,Group,Page,Pages;
            public Matrix Pose;
            public bool SameAs(View other) => other!=null && Title==other.Title && Variants==other.Variants && Group==other.Group && Page==other.Page && Pages==other.Pages && Selected==other.Selected && Pose==other.Pose &&
                Labels.SequenceEqual(other.Labels) && Enabled.SequenceEqual(other.Enabled) && SubIcons.SequenceEqual(other.SubIcons) &&
                ItemText.SequenceEqual(other.ItemText) && Icons.Zip(other.Icons,(a,b)=>a.SequenceEqual(b)).All(equal=>equal);
        }
        private static volatile View view;
        private static View drawn;
        private static OverlayCanvas canvas;
        private static ShaderResourceView texture;
        private static readonly Font font=new Font("Segoe UI",21,FontStyle.Regular,GraphicsUnit.Pixel);
        private static readonly Font titleFont=new Font("Segoe UI",28,FontStyle.Bold,GraphicsUnit.Pixel);
        private static readonly StringFormat centered=new StringFormat {
            Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisWord };
        private static DateTime nextSnapshot;
        private static bool failed;
        private static readonly ToolbarGesture gesture=new ToolbarGesture(),quickGesture=new ToolbarGesture();
        private static ActionChoice[] quickChoices;
        private static ActionChoice[][] quickPages;
        private static int quickPage,variantPages;
        public static bool QuickPending => Controls.Static.QuickMenu.RawPressed;
        private static int iconRevision;
        private static object owner;
        private static MyToolbar toolbar;
        private static int group, selection = -1;
        private static Matrix pose;

        public static void Update()
        {
            var controls = Controls.Static;
            if (failed)
            {
                if (InputRouter.RadialOpen) Close();
                if (InputRouter.Gameplay && controls.Unequip.HasPressed)
                {
                    if(ThirdPersonView.Active) ThirdPersonView.Toggle();
                    else if(CockpitButtons.HoveredSwitch>=0) CockpitActions.Configure(CockpitButtons.HoveredSwitch);
                    else GameActions.Execute(GameActions.ConfigureToolbarAction);
                }
                return;
            }
            if (InputRouter.RadialOpen)
            {
                if (InputRouter.Mode != InputMode.Radial || Main.MenuOpen || !Player.Headset.pose.isTracked || !Player.HandR.pose.isTracked ||
                    !Player.HandL.pose.isTracked || !MenuPointer.GameFocused ||
                    !ReferenceEquals(owner, MySession.Static?.ControlledEntity) || MySession.Static?.LocalCharacter?.IsDead != false)
                { Close(); return; }
                if(group==1 && controls.Unequip.HasPressed) { Close(); return; }
                if(group==1 && controls.Jetpack.HasPressed)
                { Close(); GameActions.Execute(GameActions.InventoryAction); return; }
                if (group==0 && controls.Interact.HasPressed)
                {
                    int slot=group==0 ? RadialMath.Sector(controls.MenuPage.Position,toolbar?.SlotCount ?? 9) : -1;
                    var target=toolbar;
                    Close();
                    if (InputRouter.Gameplay && ReferenceEquals(target,MyToolbarComponent.CurrentToolbar))
                        SelectSlot(target,slot,true,GameActions.AssignToolbarSlot);
                    return;
                }
                if (!(group==0 ? controls.Unequip.RawPressed : controls.QuickMenu.RawPressed))
                {
                    int chosen = RadialMath.Sector(group==0 ? controls.MenuPage.Position : controls.MenuNavigate.Position, group == 0 ? toolbar?.SlotCount ?? 9 : 9);
                    int chosenGroup = group;
                    var choices=quickChoices;
                    var chosenToolbar = toolbar;
                    Close();
                    if (!InputRouter.Gameplay) return;
                    if (chosen < 0) return;
                    if (chosenGroup == 0)
                    {
                        if(!ReferenceEquals(chosenToolbar,MyToolbarComponent.CurrentToolbar)) return;
                        var result=SelectSlot(chosenToolbar,chosen,false,GameActions.AssignToolbarSlot);
                        if(result==SlotResult.Unavailable) EssentialHud.Notify("Action unavailable");
                        else if(result==SlotResult.Activated)
                        {
                            GameActions.Reset();
                            InputRouter.Update();
                        }
                    }
                    else
                    {
                        if (chosen < choices.Length && choices[chosen]?.Enabled==true) GameActions.Execute(choices[chosen]);
                    }
                    return;
                }
                int pageDelta=(controls.Primary.HasPressed ? 1:0)-(controls.WheelNextPage.HasPressed ? 1:0);
                if(pageDelta!=0)
                {
                    if(group==0 && toolbar!=null)
                        toolbar.SwitchToPage((toolbar.CurrentPage+toolbar.PageCount+pageDelta)%Math.Max(1,toolbar.PageCount));
                    else if(group==1 && quickPages?.Length>1)
                    {
                        quickPage=(quickPage+quickPages.Length+pageDelta)%quickPages.Length;
                        quickChoices=quickPages[quickPage];
                    }
                    selection=-1;
                    (group==0 ? controls.MenuPage:controls.MenuNavigate).BlockUntilRelease();
                    nextSnapshot=DateTime.MinValue;
                }
                int next=RadialMath.Sector(group==0 ? controls.MenuPage.Position:controls.MenuNavigate.Position,group==0 ? toolbar?.SlotCount ?? 9:9);
                if (next!=selection) { selection=next; nextSnapshot=DateTime.MinValue; (group==0 ? Player.HandL:Player.HandR).Vibrate(0,0.025f,90,0.2f); }
                Publish();
                return;
            }
            var quick=quickGesture.Update(InputRouter.Gameplay && !Main.MenuOpen,
                controls.QuickMenu.HasPressed,controls.QuickMenu.IsPressed,controls.QuickMenu.HasReleased,
                MySession.Static?.ControlledEntity,-1,DateTime.UtcNow,alternate:InputRouter.Mode==InputMode.Jetpack &&
                    (controls.ThrustRoll.RawPressed || controls.RightGripPressure.RawPosition.X>.55f));
            if(quick==ToolbarGesture.Action.Unequip && InputRouter.Flying)
                GameActions.Execute(quickGesture.Alternate ? GameActions.RelativeDampeners:GameActions.Dampeners);
            if(controls.Dampener.HasPressed) GameActions.Execute(GameActions.Dampeners);
            if(quick==ToolbarGesture.Action.OpenWheel) { Open(true); return; }
            var action=gesture.Update(InputRouter.Gameplay && !Main.MenuOpen,
                controls.Unequip.HasPressed,controls.Unequip.IsPressed,controls.Unequip.HasReleased,
                MySession.Static?.ControlledEntity,CockpitBuilding.Active ? -1:CockpitButtons.HoveredSwitch,DateTime.UtcNow,ThirdPersonView.Active && !ThirdPersonView.Character && !CockpitBuilding.Active);
            if(action==ToolbarGesture.Action.None) return;
            if(action==ToolbarGesture.Action.Unequip && CockpitBuilding.Active) { CockpitBuilding.Toggle(); return; }
            if(action!=ToolbarGesture.Action.OpenWheel && SpatialUi.CollapseIfOpen()) return;
            if(action==ToolbarGesture.Action.FirstPerson) { ThirdPersonView.Toggle(); InputRouter.Update(); return; }
            if(action==ToolbarGesture.Action.AssignSwitch) { CockpitActions.Configure(gesture.Switch); InputRouter.Update(); return; }
            if(action==ToolbarGesture.Action.Unequip) { GameActions.Unequip(); InputRouter.Update(); return; }
            Open(false);
        }
        private static void Open(bool quick)
        {
            quickChoices=quick ? GameActions.WheelActions(PlacementControls.OwnsTools,InputRouter.Mode==InputMode.Piloting,ThirdPersonView.Active,InputRouter.Mode==InputMode.Jetpack || MySession.Static?.LocalCharacter?.JetpackComp?.TurnedOn==true):null;
            quickPage=variantPages=0; quickPages=null;
            if(quick)
            {
                var variants=PlacementControls.Mode==InputMode.Building ? BlockVariants.Choices() : Array.Empty<ActionChoice>(); variantPages=(variants.Length+8)/9;
                quickPages=BlockVariants.Pages(variants,quickChoices);
                quickChoices=quickPages[0];
            }
            owner=MySession.Static.ControlledEntity; toolbar=MyToolbarComponent.CurrentToolbar;
            group=quick ? 1:0; selection=-1;
            pose=HandPose((quick ? Player.HandL:Player.HandR).GripTracking,Player.Headset.pose.deviceToAbsolute.matrix);
            InputRouter.RadialOpen=true; InputRouter.Update(); nextSnapshot=DateTime.MinValue; Publish();
        }

        internal static SlotResult SelectSlot(MyToolbar target,int slot,bool assign,Action<int> configure)
        {
            if(target==null) return SlotResult.Ignored;
            bool valid=slot>=0 && slot<target.SlotCount;
            var item=valid ? target.GetItemAtSlot(slot) : null;
            if(assign || (valid && item==null))
            {
                // Assignment must never activate or clear the current item.
                configure(valid ? slot : -1);
                return SlotResult.Assigned;
            }
            if(!valid) return SlotResult.Ignored;
            if(!item.Enabled) return SlotResult.Unavailable;
            target.ActivateItemAtSlot(slot);
            return SlotResult.Activated;
        }

        private static void Publish()
        {
            if (DateTime.UtcNow < nextSnapshot) return;
            nextSnapshot = DateTime.UtcNow.AddMilliseconds(100);
            int count = group == 0 ? toolbar?.SlotCount ?? 9 : 9;
            var labels = new string[count]; var enabled = new bool[count];
            var icons = new string[count][]; var subIcons = new string[count]; var itemText = new string[count];
            for (int i = 0; i < count; i++)
            {
                if (group == 0)
                {
                    var item = toolbar?.GetItemAtSlot(i);
                    labels[i] = item?.DisplayName?.ToString() ?? (toolbar!=null ? "Assign slot" : "Unavailable");
                    icons[i] = item==null && toolbar!=null ? new[] { GameActions.ConfigureToolbarAction.Icon } :
                        item?.Icons == null ? new string[0] : (string[])item.Icons.Clone();
                    subIcons[i] = item?.SubIcon;
                    itemText[i] = item?.IconText?.ToString();
                    enabled[i] = toolbar!=null && (item==null || item.Enabled);
                }
                else
                {
                    var choices = quickChoices;
                    int index = i;
                    labels[i] = index < choices.Length ? choices[index]?.Label ?? "" : "";
                    enabled[i] = index < choices.Length && choices[index]?.Enabled==true;
                    icons[i] = index < choices.Length && choices[index]!=null ? new[] { choices[index].Icon } : new string[0];
                }
            }
            var next = new View { Pose = pose, Labels = labels, Enabled = enabled, Selected = selection, Icons = icons, SubIcons = subIcons, ItemText = itemText,
                Title = group == 0 ? "Toolbar " + ((toolbar?.CurrentPage ?? 0) + 1) + "/" + (toolbar?.PageCount ?? 0)
                    : quickPages?.Length>1 ? (variantPages>0 ? "Building " : "Quick actions ")+(quickPage+1)+" / "+quickPages.Length : "Quick actions",
                Variants=group==1 && quickPage<variantPages,Group=group,Page=group==0 ? toolbar?.CurrentPage ?? 0 : quickPage,Pages=group==0 ? toolbar?.PageCount ?? 1 : quickPages?.Length ?? 1,
                Hint = group==0 ? ControlsHint : "Hold Y · Right stick selects · X: inventory · Release Y confirms · B: cancel"+(quickPages?.Length>1 ? "\nLeft / right trigger: previous / next page" : "") };
            if (!next.SameAs(view)) view=next;
        }

        public static void Close(bool resume = true)
        {
            gesture.Reset(); quickGesture.Reset(); selection = -1; view = null;
            InputRouter.RadialOpen = false;
            InputRouter.Reset();
            if (resume) InputRouter.Update();
        }

        internal static Matrix HandPose(Matrix hand,Matrix head)
        {
            Vector3 sight=hand.Translation-head.Translation;
            if(sight.LengthSquared()<.01f) sight=head.Forward;
            sight.Normalize();
            Vector3 up=head.Up-sight*Vector3.Dot(head.Up,sight);
            if(up.LengthSquared()<.001f) up=head.Backward-sight*Vector3.Dot(head.Backward,sight);
            up.Normalize();
            // Offset along the viewing plane, not world Y: a low resting hand
            // otherwise still overlaps the lower sectors when viewed from above.
            Vector3 centre=hand.Translation+up*.33f-sight*.08f;
            Vector3 forward=centre-head.Translation;
            if(forward.LengthSquared()<.01f) forward=head.Forward;
            forward.Normalize();
            return Matrix.CreateWorld(centre,forward,Math.Abs(Vector3.Dot(forward,head.Up))>.98f ? head.Backward : head.Up);
        }
        public static void DrawWorld(Texture2D target,MatrixD viewMatrix,MatrixD projection,MatrixD trackingToWorld)
        {
            if (failed) return;
            try
            {
                var current = view;
                if (current == null) { canvas?.Hide(); drawn = null; return; }
                if (canvas == null)
                {
                    canvas = new OverlayCanvas("Toolbar wheel",1024,1024,.52f,false,target.Device,mipMaps:true);
                    texture=new ShaderResourceView(target.Device,canvas.Texture);
                }
                if (!ReferenceEquals(current,drawn) || iconRevision!=NativeSprites.Revision)
                {
                    Paint(canvas,current); canvas.Upload(); target.Device.ImmediateContext.GenerateMips(texture);
                    drawn=current; iconRevision=NativeSprites.Revision;
                }
                // Anchor motion uses every predicted render pose; texture content can update at 10Hz.
                MatrixD anchor=(MatrixD)HandPose((current.Group==1 ? Player.HandL:Player.HandR).RenderGripTracking,Player.Headset.renderPose.deviceToAbsolute.matrix)*trackingToWorld;
                var sprite=Sprite(texture,anchor,viewMatrix,projection);
                NativeSprites.Draw(target,new[] { sprite },PhysicalSurface.SceneDepth());
            }
            catch (Exception ex) { failed = true; Logger.Warning(ex, "Toolbar wheel rendering disabled"); canvas?.Hide(); }
        }
        internal static NativeSprite Sprite(ShaderResourceView texture,MatrixD anchor,MatrixD view,MatrixD projection)
        {
            var sprite=PhysicalSurface.Quad(texture,anchor,new VRageMath.RectangleF(-.26f,.26f,.52f,.52f),new Vector4(0,0,1,1),Vector4.One,view,projection);
            sprite.IgnoreSceneDepth=true;
            return sprite;
        }
        internal static void Paint(OverlayCanvas target, View current)
        {
            target.Clear(Color.Transparent);
            var g = target.Graphics;
            using (var center = new SolidBrush(Color.FromArgb(220, 9, 18, 26))) g.FillEllipse(center, 335, 308, 354, 354);
            g.DrawString(current.Title,titleFont,Brushes.White,new System.Drawing.RectangleF(360,337,304,40),centered);
            if(current.Selected>=0) foreach(string icon in current.Icons[current.Selected]) target.Icon(icon,456,389,112);
            else target.Icon(current.Group==0 ? GameActions.ConfigureToolbarAction.Icon : current.Group==1 ? GameActions.InventoryAction.Icon :
                GameActions.Building[0].Icon,456,389,112);
            g.DrawString(current.Selected<0 ? "Cancel" : current.Labels[current.Selected],titleFont,Brushes.Cyan,
                new System.Drawing.RectangleF(360,514,304,76),centered);
            int pages=Math.Min(9,Math.Max(1,current.Pages));
            for(int i=0;(current.Group==0 || current.Pages>1) && i<pages;i++) g.FillEllipse(i==current.Page ? Brushes.Cyan : Brushes.SlateGray,512-(pages*24)/2+i*24,602,13,13);
            g.DrawString(current.Group==0 ? "A: Assign" : "X: Inventory",font,Brushes.LightGray,new System.Drawing.RectangleF(392,624,240,28),centered);
            for (int i = 0; i < current.Labels.Length; i++)
            {
                double angle = i * Math.PI * 2 / current.Labels.Length;
                float degrees = 360f / current.Labels.Length;
                using (var sector = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    sector.AddArc(102, 75, 820, 820, -90+i*degrees-degrees/2+1, degrees-2);
                    sector.AddArc(322, 295, 380, 380, -90+i*degrees+degrees/2-1, -degrees+2);
                    sector.CloseFigure();
                    using (var fill = new SolidBrush(i == current.Selected ? Color.FromArgb(245, 28, 97, 118) : Color.FromArgb(225, 19, 34, 45))) g.FillPath(fill, sector);
                    if (i == current.Selected) using (var outline = new Pen(Color.Cyan, 3)) g.DrawPath(outline, sector);
                }
                float x = 512 + (float)Math.Sin(angle) * 298, y = 485 - (float)Math.Cos(angle) * 298;
                foreach (string icon in current.Icons[i]) target.Icon(icon, x-56, y-68, 112, current.Enabled[i]);
                if (current.SubIcons[i] != null) target.Icon(current.SubIcons[i], x+24, y+10, 40, current.Enabled[i]);
                g.DrawString((i+1).ToString(),font,Brushes.LightGray,x-72,y-64);
                if(!current.Variants && (current.Group==1 || i==current.Selected)) g.DrawString(current.Labels[i],font,current.Enabled[i] ? Brushes.White : Brushes.Gray,
                    new System.Drawing.RectangleF(x-88,y+38,176,44),centered);
                if(!string.IsNullOrEmpty(current.ItemText[i])) g.DrawString(current.ItemText[i],font,Brushes.Cyan,x-40,y+16);
            }
            // Instruction text belongs in controller help, not below the wheel.
        }
        public static void Hide() => canvas?.Hide();
    }
}
