using System;
using System.Collections.Generic;
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
        internal const string ControlsHint="Hold B · A: assign / G menu · Empty slot: assign · Release B confirms\nEither stick selects · Left / right trigger: page · Center cancels";
        internal const string BumperControlsHint="Bumper · A: assign / G menu · Empty slot: assign · Close to confirm\nEither stick selects · Left / right trigger: page · Center cancels";
        internal static string QuickControlsHint(bool pages,bool bumper=false) => (bumper ? "Bumper · Either stick selects · X: inventory · Close to confirm · B: cancel" :
            "Hold Y · Either stick selects · X: inventory · Release Y confirms · B: cancel")+(pages ? "\nLeft / right trigger: previous / next page":"");
        internal enum SlotResult { Ignored, Assigned, Activated, Unavailable }
        internal sealed class View
        {
            public string Title, Hint;
            public string Footer, Idle;
            public bool Compact;
            public string[] Labels, SubIcons, ItemText;
            public string[][] Icons;
            public bool[] Enabled;
            public bool Variants;
            public int Selected,Group,Page,Pages;
            public Matrix Pose;
            public bool SameAs(View other) => other!=null && Title==other.Title && Footer==other.Footer && Idle==other.Idle && Compact==other.Compact && Variants==other.Variants && Group==other.Group && Page==other.Page && Pages==other.Pages && Selected==other.Selected && Pose==other.Pose &&
                Enumerable.SequenceEqual(Labels,other.Labels) && Enumerable.SequenceEqual(Enabled,other.Enabled) && Enumerable.SequenceEqual(SubIcons,other.SubIcons) &&
                Enumerable.SequenceEqual(ItemText,other.ItemText) && Icons.Zip(other.Icons,(a,b)=>Enumerable.SequenceEqual(a,b)).All(equal=>equal);
        }
        private static volatile View view,external;
        internal static void Show(View value) { if(value==null ? external!=null : !value.SameAs(external)) external=value; }
        private static View drawn;
        private static OverlayCanvas canvas;
        private static ShaderResourceView texture;
        private static readonly Font font=new Font("Segoe UI",21,FontStyle.Regular,GraphicsUnit.Pixel);
        private static readonly Font titleFont=new Font("Segoe UI",28,FontStyle.Bold,GraphicsUnit.Pixel);
        private static readonly StringFormat centered=new StringFormat {
            Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisWord };
        private static readonly StringFormat singleLine=new StringFormat {
            Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap,Trimming=StringTrimming.EllipsisCharacter };
        private static DateTime nextSnapshot;
        private static readonly RenderRecovery recovery=new RenderRecovery("Toolbar wheel");
        private static bool failed => recovery.Failed;
        private static readonly ToolbarGesture gesture=new ToolbarGesture(),quickGesture=new ToolbarGesture();
        private const double TapSeconds=.28;
        private static Button holder;
        private static bool latched;
        private static DateTime openedAt;
        private static ActionChoice[] quickChoices;
        private static ActionChoice[][] quickPages;
        private static int quickPage;
        private static string[] pageTitles;
        private static bool[] variantPages;
        public static bool QuickPending => Controls.Static.QuickMenu.RawPressed;
        internal static bool ShowingToolbar => holder!=null && group==0;
        private static int iconRevision;
        private static object owner;
        private static MyToolbar toolbar;
        private static int group, selection = -1;
        private static readonly RadialNavigation navigation=new RadialNavigation();
        private static Matrix pose;

        public static void Update()
        {
            var controls = Controls.Static;
            if (failed)
            {
                if (InputRouter.RadialOpen) Close();
                if (InputRouter.Gameplay && controls.Unequip.HasPressed)
                {
                    if(ThirdPersonView.Active) { if(!PlacementControls.TryCancelRotation()) ThirdPersonView.Toggle(); }
                    else if(CockpitButtons.HoveredSwitch>=0) CockpitActions.Configure(CockpitButtons.HoveredSwitch);
                    else GameActions.Execute(GameActions.ConfigureToolbarAction);
                }
                return;
            }
            if (InputRouter.RadialOpen)
            {
                if (InputRouter.Mode != InputMode.Radial || Main.MenuOpen || !Player.Headset.pose.isTracked || !Player.HandR.pose.isTracked ||
                    !Player.HandL.pose.isTracked ||
                    !ReferenceEquals(owner, MySession.Static?.ControlledEntity) || MySession.Static?.LocalCharacter?.IsDead != false)
                { Close(); return; }
                var stick=navigation.Update(controls.MenuPage.Position,controls.MenuNavigate.Position,group==0 ? 0:1);
                if(holder!=controls.Unequip && controls.Unequip.HasPressed) { Close(); return; }
                if(group==1 && controls.Jetpack.HasPressed)
                { Close(); GameActions.Execute(GameActions.InventoryAction); return; }
                if (group==0 && controls.Interact.HasPressed)
                {
                    int slot=group==0 ? RadialMath.Sector(stick,Slots(toolbar)) : -1;
                    var target=toolbar;
                    Close();
                    if (InputRouter.Gameplay && ReferenceEquals(target,MyToolbarComponent.CurrentToolbar))
                        SelectSlot(target,slot,true,GameActions.AssignToolbarSlot);
                    return;
                }
                if (Confirming(DateTime.UtcNow))
                {
                    int chosen = RadialMath.Sector(stick, group == 0 ? Slots(toolbar) : 9);
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
                    controls.MenuPage.BlockUntilRelease(); controls.MenuNavigate.BlockUntilRelease(); navigation.Reset(); stick=Vector2.Zero;
                    nextSnapshot=DateTime.MinValue;
                }
                int next=RadialMath.Sector(stick,group==0 ? Slots(toolbar):9);
                if (next!=selection) { selection=next; nextSnapshot=DateTime.MinValue; (navigation.Owner==0 ? Player.HandL:Player.HandR).Vibrate(0,0.025f,90,0.2f); }
                Publish();
                return;
            }
            bool available=InputRouter.Gameplay && !Main.MenuOpen;
            if(available && controls.ActionsWheel.HasPressed) { Open(true,controls.ActionsWheel); return; }
            if(available && controls.ToolbarWheel.HasPressed) { Open(false,controls.ToolbarWheel); return; }
            var quick=quickGesture.Update(available,controls.QuickMenu.HasPressed,controls.QuickMenu.IsPressed,controls.QuickMenu.HasReleased,
                MySession.Static?.ControlledEntity,-1,DateTime.UtcNow);
            if(quick==ToolbarGesture.Action.OpenWheel) { Open(true,controls.QuickMenu); return; }
            var action=gesture.Update(InputRouter.Gameplay && !Main.MenuOpen,
                controls.Unequip.HasPressed,controls.Unequip.IsPressed,controls.Unequip.HasReleased,
                MySession.Static?.ControlledEntity,CockpitBuilding.Active ? -1:CockpitButtons.HoveredSwitch,DateTime.UtcNow,ThirdPersonView.Active && !CockpitBuilding.Active);
            if(action==ToolbarGesture.Action.None) return;
            if(action==ToolbarGesture.Action.Unequip && CockpitBuilding.Active) { CockpitBuilding.Toggle(); return; }
            if(action!=ToolbarGesture.Action.OpenWheel && (DesktopWindow.CloseIfPointed() || SpatialUi.CollapseIfOpen())) return;
            if(action==ToolbarGesture.Action.FirstPerson) { if(!PlacementControls.TryCancelRotation()) ThirdPersonView.Toggle(); InputRouter.Update(); return; }
            if(action==ToolbarGesture.Action.AssignSwitch) { CockpitActions.Configure(gesture.Switch); InputRouter.Update(); return; }
            if(action==ToolbarGesture.Action.Unequip) { if(!PlacementControls.TryCancelRotation()) GameActions.Unequip(); InputRouter.Update(); return; }
            Open(false,controls.Unequip);
        }
        private static bool Confirming(DateTime now) =>
            Confirms(holder==Controls.Static.ActionsWheel || holder==Controls.Static.ToolbarWheel,holder.RawPressed,holder.HasPressed,(now-openedAt).TotalSeconds,ref latched);
        // Holding confirms on release. On the dedicated wheel buttons a quick tap keeps the wheel open until the next tap;
        // Touch Y and B open only on a hold, since a B tap unequips.
        internal static bool Confirms(bool tapLatches,bool held,bool pressed,double seconds,ref bool latched)
        {
            if(latched) return pressed;
            if(held) return false;
            if(tapLatches && seconds<TapSeconds) { latched=true; return false; }
            return true;
        }
        internal static ActionChoice[][] WithRecents(ActionChoice[][] pages,ActionChoice[] recent)
        {
            var last=new ActionChoice[9];
            Array.Copy(recent,last,Math.Min(9,recent.Length));
            return pages.Concat(new[] {last}).ToArray();
        }
        private static void Open(bool quick,Button button)
        {
            holder=button; latched=false; openedAt=DateTime.UtcNow;
            bool jetpack=InputRouter.Mode==InputMode.Jetpack || MySession.Static?.LocalCharacter?.JetpackComp?.TurnedOn==true;
            // The multitool places no blocks, so it keeps the on-foot actions; its own are on the Build Info page.
            quickChoices=quick ? GameActions.WheelActions(PlacementControls.OwnsTools && !Patches.BuildInfoBridge.MultiToolEquipped,InputRouter.Mode==InputMode.Piloting,ThirdPersonView.Active,jetpack):null;
            quickPage=0; quickPages=null; pageTitles=null; variantPages=null;
            if(quick)
            {
                bool symmetry=SymmetrySetupControls.Active;
                var block=symmetry || PlacementControls.ClipboardActive ? Array.Empty<ActionChoice>():BlockActions.Capture(PlacementControls.OwnsTools);
                ActionChoice[] Contextual(ActionChoice[] choices) => choices.Select(choice=>choice==GameActions.Quick[11] || choice==GameActions.Quick[12] ?
                    block.FirstOrDefault(c=>c.Label==choice.Label) ?? choice:choice).ToArray();
                var aimed=new Group("Block actions",block);
                var groups=new List<Group>();
                if(InputRouter.Mode==InputMode.Building && !symmetry)
                {
                    var voxel=VoxelTools.Current;
                    groups.AddRange(ToolGroups(voxel?.Title ?? "Building",quickChoices,voxel!=null ? voxel.Materials() : new[] {new Group("Variants",BlockVariants.Choices(),true)},
                        Contextual(GameActions.WheelActions(false,false,ThirdPersonView.Active,jetpack)),aimed));
                }
                else
                {
                    var paint=symmetry ? Array.Empty<ActionChoice>() : PaintGun.Actions();
                    if(paint.Length>0) groups.Add(new Group("Paint gun",paint));
                    groups.AddRange(new[] {aimed,new Group(symmetry ? "Symmetry setup":"Quick actions",Contextual(quickChoices))});
                    var weaponCore=symmetry ? Array.Empty<ActionChoice>() : GameActions.WeaponCoreWheelActions();
                    if(weaponCore.Length>0) groups.Add(new Group("WeaponCore",weaponCore));
                }
                var buildInfo=symmetry ? Array.Empty<ActionChoice>() : Patches.BuildInfoBridge.Actions();
                if(buildInfo.Length>0) groups.Add(new Group("Build Info",buildInfo));
                var recent=ActionCatalog.Recent();
                if(MySession.Static.ControlledEntity is Sandbox.Game.Entities.MyShipController)
                    recent=recent.Where(action=>action!=GameActions.SymmetryAction && action!=GameActions.SymmetrySetupAction).ToArray();
                quickPages=WithRecents(Layout(groups,out var titles,out var variants),recent);
                pageTitles=titles.Concat(new[] {"Recent searches"}).ToArray(); variantPages=variants.Concat(new[] {false}).ToArray();
                quickChoices=quickPages[0];
            }
            owner=MySession.Static.ControlledEntity; toolbar=MyToolbarComponent.CurrentToolbar;
            group=quick ? 1:0; selection=-1; navigation.Reset();
            pose=HandPose((quick ? Player.HandL:Player.HandR).GripTracking,Player.Headset.pose.deviceToAbsolute.matrix);
            InputRouter.RadialOpen=true; InputRouter.Update(); nextSnapshot=DateTime.MinValue; Publish();
        }

        private const string HolsterIcon=@"Textures\GUI\Icons\HideWeapon.dds";
        // Vanilla keeps its unequip as the slot after the last one, for ship and character toolbars alike.
        internal static int Slots(MyToolbar target) => (target?.SlotCount ?? 9)+(target?.ShowHolsterSlot==true ? 1:0);
        private static bool Holster(MyToolbar target,int slot) => target?.ShowHolsterSlot==true && slot==target.SlotCount;

        internal static SlotResult SelectSlot(MyToolbar target,int slot,bool assign,Action<int> configure)
        {
            if(target==null) return SlotResult.Ignored;
            if(!assign && Patches.BuildInfoBridge.TryPickSlot(slot,target.SlotCount)) return SlotResult.Activated;
            if(Holster(target,slot))
            {
                // Assignment opens the configuration screen with no slot; the holster cannot hold an action.
                if(assign) { configure(-1); return SlotResult.Assigned; }
                target.ActivateItemAtSlot(slot);
                return SlotResult.Activated;
            }
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
            int count = group == 0 ? Slots(toolbar) : 9;
            var labels = new string[count]; var enabled = new bool[count];
            var icons = new string[count][]; var subIcons = new string[count]; var itemText = new string[count];
            for (int i = 0; i < count; i++)
            {
                if (group == 0 && Holster(toolbar,i))
                {
                    labels[i]="Unequip"; icons[i]=new[] { HolsterIcon }; enabled[i]=true;
                }
                else if (group == 0)
                {
                    var item = toolbar?.GetItemAtSlot(i);
                    labels[i] = item?.DisplayName?.ToString() ?? (toolbar!=null ? "Assign slot" : "Unavailable");
                    icons[i] = item==null && toolbar!=null ? new[] { GameActions.ConfigureToolbarAction.Icon } :
                        item?.Icons == null ? new string[0] : (string[])item.Icons.Clone();
                    subIcons[i] = item?.SubIcon;
                    itemText[i] = Patches.BuildInfoBridge.StatusText(item?.IconText?.ToString());
                    string mode=toolbar?.SelectedSlot==i ? Patches.BuildInfoBridge.WeaponMode : null;
                    if(mode!=null) itemText[i]=string.IsNullOrEmpty(itemText[i]) ? mode : itemText[i]+" · "+mode;
                    enabled[i] = toolbar!=null && (item==null || item.Enabled);
                }
                else
                {
                    var choices = quickChoices;
                    int index = i;
                    labels[i] = index < choices.Length ? choices[index]?.Label ?? "" : "";
                    enabled[i] = index < choices.Length && choices[index]?.Enabled==true;
                    icons[i] = index < choices.Length && choices[index]!=null ? new[] { choices[index].Icon } : new string[0];
                    subIcons[i] = index < choices.Length ? choices[index]?.SubIcon : null;
                }
            }
            var next = new View { Pose = pose, Labels = labels, Enabled = enabled, Selected = selection, Icons = icons, SubIcons = subIcons, ItemText = itemText,
                Title = group == 0 ? "Toolbar " + ((toolbar?.CurrentPage ?? 0) + 1) + "/" + (toolbar?.PageCount ?? 0)
                    : pageTitles?[quickPage] ?? "Quick actions",
                Variants=group==1 && variantPages?[quickPage]==true,Group=group,Page=group==0 ? toolbar?.CurrentPage ?? 0 : quickPage,Pages=group==0 ? toolbar?.PageCount ?? 1 : quickPages?.Length ?? 1,
                Hint = group==0 ? (holder==Controls.Static.ToolbarWheel ? BumperControlsHint:ControlsHint) : QuickControlsHint(quickPages?.Length>1,holder==Controls.Static.ActionsWheel) };
            if (!next.SameAs(view)) view=next;
        }

        internal sealed class Group
        {
            internal readonly string Title;
            internal readonly ActionChoice[] Items;
            // Variant and material pages show icons only, with the hovered name in the center.
            internal readonly bool Variants;
            internal Group(string title,ActionChoice[] items,bool variants=false) { Title=title; Items=items; Variants=variants; }
        }
        // Y wheel order: the held tool's variants or materials, aimed-block actions, the tool's actions, general quick actions, recent searches.
        internal static Group[] ToolGroups(string title,ActionChoice[] tool,Group[] variants,ActionChoice[] quick,Group aimed=null) =>
            variants.Concat(aimed!=null ? new[] {aimed}:new Group[0]).Concat(new[] {new Group(title,tool),new Group("Quick actions",quick)}).ToArray();
        internal static ActionChoice[][] Layout(IEnumerable<Group> groups,out string[] titles,out bool[] variants)
        {
            var pages=new List<ActionChoice[]>(); var titleList=new List<string>(); var variantList=new List<bool>();
            foreach(var group in groups)
                foreach(var page in BlockVariants.Pages(group.Items))
                { pages.Add(page); titleList.Add(group.Title); variantList.Add(group.Variants); }
            titles=titleList.ToArray(); variants=variantList.ToArray();
            return pages.ToArray();
        }

        public static void Close(bool resume = true)
        {
            gesture.Reset(); quickGesture.Reset(); navigation.Reset(); selection = -1; view = null; holder=null; latched=false;
            quickChoices=null; quickPages=null;
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
                var current = view ?? external;
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
                float size=current.Compact ? .40f:.52f;
                var sprites=new List<NativeSprite> { Sprite(texture,anchor,viewMatrix,projection,size) };
                var info=BuildInfoPanel.Toolbar.Current;
                if(current.Group==0 && BuildInfoPanel.Fresh(info,DateTime.UtcNow))
                    sprites.Add(BuildInfoPanel.Toolbar.WorldQuad(target.Device,info,BelowRing(anchor,size,info.Height),viewMatrix,projection));
                NativeSprites.Draw(target,sprites,PhysicalSurface.SceneDepth());
            }
            catch (Exception ex) { drawn = null; recovery.Fail(ex, "Toolbar wheel rendering disabled"); RenderRecovery.Quietly(() => canvas?.Hide()); }
        }
        // Centred below the painted ring, whose bottom is 895 of 1024 px.
        internal static MatrixD BelowRing(MatrixD anchor,float size,float height)
        {
            var pose=anchor;
            pose.Translation=anchor.Translation+anchor.Down*(size*383/1024+BuildInfoPanel.Gap+height/2);
            return pose;
        }
        internal static NativeSprite Sprite(ShaderResourceView texture,MatrixD anchor,MatrixD view,MatrixD projection,float size=.52f)
        {
            var sprite=PhysicalSurface.Quad(texture,anchor,new VRageMath.RectangleF(-size/2,size/2,size,size),new Vector4(0,0,1,1),Vector4.One,view,projection);
            sprite.IgnoreSceneDepth=true;
            return sprite;
        }
        internal static void Paint(OverlayCanvas target, View current)
        {
            target.Clear(Color.Transparent);
            var g = target.Graphics;
            float disc=current.Compact ? 140:177,inner=current.Compact ? 152:190,ring=current.Compact ? 281:298;
            using (var center = new SolidBrush(Color.FromArgb(220, 9, 18, 26))) g.FillEllipse(center, 512-disc, 485-disc, disc*2, disc*2);
            // The title sits where the center disc narrows; fit it to the chord at its cap height.
            float titleTop=485-disc+29,titleWidth=2*(float)Math.Sqrt(disc*disc-(disc-37)*(disc-37))-12;
            g.DrawString(current.Title,TextFit.Font(g,current.Title,titleFont,titleWidth,1,18),Brushes.White,
                new System.Drawing.RectangleF(512-titleWidth/2,titleTop,titleWidth,40),centered);
            float iconSize=current.Compact ? 84:112,iconTop=current.Compact ? 400:389;
            if(current.Selected>=0) foreach(string icon in current.Icons[current.Selected]) target.Icon(icon,512-iconSize/2,iconTop,iconSize);
            else if(current.Idle==null) target.Icon(current.Group==0 ? GameActions.ConfigureToolbarAction.Icon : current.Group==1 ? GameActions.InventoryAction.Icon :
                GameActions.Building[0].Icon,512-iconSize/2,iconTop,iconSize);
            string selected=current.Selected<0 ? current.Idle ?? "Cancel" : current.Labels[current.Selected];
            float labelWidth=current.Compact ? 230:280,labelTop=iconTop+iconSize+13;
            g.DrawString(selected,TextFit.Font(g,selected,titleFont,labelWidth,2,20),Brushes.Cyan,new System.Drawing.RectangleF(512-labelWidth/2,labelTop,labelWidth,current.Compact ? 60:76),centered);
            int pages=Math.Min(9,Math.Max(1,current.Pages));
            for(int i=0;(current.Group==0 || current.Pages>1) && i<pages;i++) g.FillEllipse(i==current.Page ? Brushes.Cyan : Brushes.SlateGray,512-(pages*24)/2+i*24,602,13,13);
            g.DrawString(current.Footer ?? (current.Group==0 ? "A: Assign" : "X: Inventory"),font,Brushes.LightGray,new System.Drawing.RectangleF(392,current.Compact ? 568:624,240,28),centered);
            int count=current.Labels.Length;
            float degrees=360f/count;
            var sectors=new System.Drawing.Drawing2D.GraphicsPath[count];
            var centers=new PointF[count];
            var widths=new float[count];
            var labelTops=new float[count];
            var labelFits=new bool[count];
            var numbers=new PointF[count];
            var itemFonts=new Font[count];
            var itemBoxes=new System.Drawing.RectangleF[count];
            try
            {
                float labelSize=font.Size;
                for(int i=0;i<count;i++)
                {
                    var sector=sectors[i]=new System.Drawing.Drawing2D.GraphicsPath();
                    sector.AddArc(102,75,820,820,-90+i*degrees-degrees/2+1,degrees-2);
                    sector.AddArc(512-inner,485-inner,inner*2,inner*2,-90+i*degrees+degrees/2-1,-degrees+2);
                    sector.CloseFigure();
                    double angle=i*Math.PI*2/count;
                    // Upper segments narrow toward the center, where their label sits; move the stack outward.
                    float x=512+(float)Math.Sin(angle)*ring,y=485-(float)Math.Cos(angle)*ring-24*(float)Math.Max(0,Math.Cos(angle));
                    centers[i]=new PointF(x,y);
                    // Slot numbers sit near the rim on the counterclockwise side, inside the segment at every angle.
                    double number=angle-degrees*Math.PI/180*.32;
                    numbers[i]=new PointF(512+(float)Math.Sin(number)*378,485-(float)Math.Cos(number)*378);
                    // Native item text (weapon ammo "0 • 20x") gets its own lines under the icon, wrapping before it gets small; the label follows it.
                    float itemHeight=0;
                    string item=current.ItemText[i];
                    if(!string.IsNullOrEmpty(item))
                    {
                        float one=SectorSpan(sector,x,y+40,y+60),two=SectorSpan(sector,x,y+40,y+84);
                        if(TextFit.TryFont(g,item,font,one,1,17,out itemFonts[i])) itemHeight=24;
                        else { itemHeight=48; itemFonts[i]=TextFit.Font(g,item,font,two,2,14); one=two; }
                        itemBoxes[i]=new System.Drawing.RectangleF(x-one/2,y+38,one,itemHeight);
                    }
                    labelTops[i]=y+38+itemHeight;
                    widths[i]=SectorSpan(sector,x,labelTops[i]+13,labelTops[i]+41);
                    if(ShowsLabel(current) && (labelFits[i]=TextFit.TryFont(g,current.Labels[i],font,widths[i],2,15,out var fitted)))
                        labelSize=Math.Min(labelSize,fitted.Size);
                }
                // One size for every segment keeps the ring even.
                var labelFont=TextFit.Sized(font,labelSize);
                for(int i=0;i<count;i++)
                {
                    float x=centers[i].X,y=centers[i].Y;
                    using(var fill=new SolidBrush(i==current.Selected ? Color.FromArgb(245,28,97,118):Color.FromArgb(225,19,34,45))) g.FillPath(fill,sectors[i]);
                    if(i==current.Selected) using(var outline=new Pen(Color.Cyan,3)) g.DrawPath(outline,sectors[i]);
                    foreach(string icon in current.Icons[i]) target.Icon(icon,x-56,y-68,112,current.Enabled[i]);
                    if(current.SubIcons[i]!=null) target.Icon(current.SubIcons[i],x+24,y+2,40,current.Enabled[i]);
                    // Toolbar slots carry their key number; the tenth slot is key 0.
                    if(current.Group==0 && !current.Variants)
                        g.DrawString(((i+1)%10).ToString(),font,Brushes.LightGray,new System.Drawing.RectangleF(numbers[i].X-20,numbers[i].Y-14,40,28),centered);
                    var labelBrush=current.Enabled[i] ? Brushes.White:Brushes.Gray;
                    if(ShowsLabel(current) && labelFits[i])
                        g.DrawString(current.Labels[i],labelFont,labelBrush,new System.Drawing.RectangleF(x-widths[i]/2,labelTops[i],widths[i],54),centered);
                    // The center already names the selected item; others shorten rather than break a word.
                    else if(ShowsLabel(current) && i!=current.Selected)
                        g.DrawString(current.Labels[i],TextFit.Sized(font,15),labelBrush,new System.Drawing.RectangleF(x-widths[i]/2,labelTops[i],widths[i],27),singleLine);
                    if(itemFonts[i]!=null)
                        g.DrawString(current.ItemText[i],itemFonts[i],Brushes.Cyan,itemBoxes[i],itemBoxes[i].Height>24 ? centered:singleLine);
                }
            }
            finally { foreach(var sector in sectors) sector?.Dispose(); }
            // Instruction text belongs in controller help, not below the wheel.
        }
        // The center names the selected item, so only action segments carry their own label.
        private static bool ShowsLabel(View current) => !current.Variants && current.Group==1;
        // Widest label centered on x whose two text lines stay inside the segment, so text never crosses into a neighbor.
        private static float SectorSpan(System.Drawing.Drawing2D.GraphicsPath sector,float x,float firstLine,float secondLine)
        {
            bool Inside(float px) => sector.IsVisible(px,firstLine) && sector.IsVisible(px,secondLine);
            float half=4;
            while(half<100 && Inside(x-half-4) && Inside(x+half+4)) half+=4;
            return half*2;
        }
        public static void Hide() => canvas?.Hide();
    }
}
