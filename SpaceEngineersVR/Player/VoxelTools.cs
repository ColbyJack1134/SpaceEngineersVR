using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.SessionComponents;
using Sandbox.Game.World;
using SpaceEngineersVR.Player.Control;
using VRage;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Voxel tools share the building controls; each adapter maps them onto its own native or mod state.
    internal abstract class VoxelTool
    {
        internal readonly MyBlockBuilderRotationHints Hints=new MyBlockBuilderRotationHints();
        internal abstract bool Active { get; }
        internal abstract string Title { get; }
        internal abstract MyStringId PaintControl { get; }
        internal abstract void Queue(ActionFrame frame,bool primary,bool secondary);
        internal abstract bool TryOrientation(out MatrixD matrix);
        // Control indexes follow the native rotation order: vertical +/-, horizontal +/-, roll +/-.
        internal abstract void Rotate(int control,float dt);
        // Left stick Y in rotate mode moves the tool along the ray or resizes it; the wheel switches between them.
        internal virtual bool Sizing => VoxelTools.StickSizes;
        internal void Adjust(float factor) { if(Sizing) Resize(factor); else Move(factor); }
        protected abstract void Move(float factor);
        protected abstract void Resize(float factor);
        internal abstract ActionChoice[] Actions();
        internal virtual ToolbarWheel.Group[] Materials() => new ToolbarWheel.Group[0];
        internal virtual bool Selected(out string name,out string icon) { name=icon=null; return false; }
        internal void UpdateHints(bool rotating)
        {
            if(TryOrientation(out var matrix)) Hints.CalculateRotationHints(matrix,VoxelTools.HintsDrawn,rotating);
        }
    }

    internal static class VoxelTools
    {
        internal static readonly VoxelHandTool Hand=new VoxelHandTool();
        internal static readonly List<DigiTool> Mods=new List<DigiTool>();
        internal static VoxelTool Current => Hand.Active ? Hand : (VoxelTool)Mods.FirstOrDefault(tool=>tool.Active);
        internal static bool Active => Current!=null;
        internal static bool HintsDrawn => !HelmetHud.NativeSuppressed && Sandbox.MySandboxGame.Config.RotationHints && !VRage.Input.MyInput.Static.IsJoystickLastUsed;
        internal static readonly MyStringId[] RotationControls={
            MyControlsSpace.CUBE_ROTATE_VERTICAL_POSITIVE,MyControlsSpace.CUBE_ROTATE_VERTICAL_NEGATIVE,
            MyControlsSpace.CUBE_ROTATE_HORISONTAL_POSITIVE,MyControlsSpace.CUBE_ROTATE_HORISONTAL_NEGATIVE,
            MyControlsSpace.CUBE_ROTATE_ROLL_POSITIVE,MyControlsSpace.CUBE_ROTATE_ROLL_NEGATIVE};
        private static readonly int[] directions={-1,1,1,-1,1,-1};

        // Same axis choice as MyCubeBuilder.HandleRotationInput and CalculateBlockRotation, so the hint arrows match the stick.
        internal static void HintAxis(MyBlockBuilderRotationHints hints,int control,out int axis,out int sign)
        {
            sign=directions[control];
            if(control<2) { axis=hints.RotationUpAxis; sign*=hints.RotationUpDirection; }
            else if(control<4) { axis=hints.RotationRightAxis; sign*=hints.RotationRightDirection; }
            else { axis=hints.RotationForwardAxis; sign*=hints.RotationForwardDirection; }
            if(axis==2) sign=-sign;
        }
        internal static MatrixD Rotate(MatrixD matrix,int axis,int sign,double angle)
        {
            if(axis<0 || axis>2) return matrix;
            var translation=matrix.Translation; matrix.Translation=Vector3D.Zero;
            var around=axis==0 ? matrix.Right : axis==1 ? matrix.Up : matrix.Forward;
            matrix=MatrixD.Orthogonalize(matrix*MatrixD.CreateFromAxisAngle(around,sign*angle));
            matrix.Translation=translation;
            return matrix;
        }
        internal static string Words(string subtype) => subtype?.Replace('_',' ');

        internal static bool StickSizes;
        internal static readonly ActionChoice StickAction=new ActionChoice(()=>"Left stick: "+(Current?.Sizing==true ? "size":"distance"),
            ()=>StickSizes=!StickSizes,historyKey:"Left stick distance / size");
        internal static void Update(bool rotating)
        {
            var current=Current;
            foreach(var tool in Mods) if(tool!=current) tool.ResetModifiers();
            UpdateModifiers();
            current?.UpdateHints(rotating);
        }

        // Snap lock is the mod's snap control with a modifier held for that press.
        internal static bool LockPending;
        private static bool lockSeen;
        internal static void UpdateModifiers()
        {
            bool pressed=NativeActions.Read(MyControlsSpace.FREE_ROTATION,VRage.Input.MyControlStateType.PRESSED);
            if(LockPending && lockSeen && !pressed) LockPending=false;
            lockSeen=LockPending && (lockSeen || pressed);
        }
        internal static void Lock() { LockPending=true; lockSeen=false; NativeActions.Pulse(MyControlsSpace.FREE_ROTATION); }
    }

    internal sealed class VoxelHandTool : VoxelTool
    {
        private static readonly FieldInfo material=AccessTools.Field(typeof(MySessionComponentVoxelHand),"m_selectedMaterial");
        private static readonly FieldInfo itemMaterial=AccessTools.Field(AccessTools.TypeByName("Sandbox.Game.Screens.Helpers.MyRadialMenuItemVoxelHand"),"Material");
        // The native hand reads the large grid size into statics on first use, and that size is 0 until a world's definitions load.
        private static MySessionComponentVoxelHand Native => Plugin.Main.WorldAvailable ? Loaded() : null;
        [MethodImpl(MethodImplOptions.NoInlining)] internal static MySessionComponentVoxelHand Loaded() => MySessionComponentVoxelHand.Static;
        // The native hand turns one degree per 60 Hz input tick.
        internal const double DegreesPerSecond=60;
        private static MySessionComponentVoxelHand configured;
        private static IMyVoxelBrush repaired;
        internal override bool Active
        {
            get
            {
                var hand=Native;
                if(hand?.Enabled!=true || hand.CurrentShape==null || MySession.Static?.LocalCharacter==null || MySession.Static.ControlledEntity!=MySession.Static.LocalCharacter) return false;
                // Smooth placement along the finger ray; snap and surface projection stay on the wheel.
                if(!ReferenceEquals(configured,hand)) { configured=hand; hand.ProjectToVoxel=false; hand.SnapToVoxel=false; }
                if(!ReferenceEquals(repaired,hand.CurrentShape)) { repaired=hand.CurrentShape; Resize(hand,1,true); }
                return true;
            }
        }
        internal override string Title => "Voxel hand";
        internal override MyStringId PaintControl => MyControlsSpace.VOXEL_PAINT;
        internal override void Queue(ActionFrame frame,bool primary,bool secondary)
        {
            if(primary && secondary) frame.Queue(MyControlsSpace.SECONDARY_TOOL_ACTION);
            else if(primary) frame.Queue(MyControlsSpace.PRIMARY_TOOL_ACTION);
        }
        internal override bool TryOrientation(out MatrixD matrix)
        {
            matrix=MatrixD.Identity;
            var shape=Native?.CurrentShape;
            // Every brush shows the rotate mode icon, since the mode also adjusts distance; only rotatable brushes turn.
            if(shape==null) return false;
            matrix=Native.m_rotation; matrix.Translation=shape.GetPosition();
            return true;
        }
        internal override void Rotate(int control,float dt)
        {
            var hand=Native;
            if(hand?.CurrentShape==null || !hand.CurrentShape.ShowRotationGizmo()) return;
            VoxelTools.HintAxis(Hints,control,out int axis,out int sign);
            hand.m_rotation=VoxelTools.Rotate(hand.m_rotation,axis,sign,MathHelper.ToRadians(DegreesPerSecond)*dt);
            hand.CurrentShape.SetRotation(ref hand.m_rotation);
        }
        // A brush on the surface has no free distance, so the stick always resizes it.
        internal override bool Sizing => base.Sizing || Native?.ProjectToVoxel==true;
        protected override void Move(float factor) { var hand=Native; hand?.SetBrushZoom(hand.GetBrushZoom()*factor); }
        protected override void Resize(float factor) { var hand=Native; if(hand!=null) Resize(hand,factor); }
        // Every native brush slider is a dimension, as the native scale buttons assume.
        // Brush Load fills the sliders unclamped without rebuilding the shape; applying them on activation syncs the preview.
        private static void Resize(MySessionComponentVoxelHand hand,float factor,bool apply=false)
        {
            var shape=hand.CurrentShape;
            if(shape==null) return;
            bool changed=false;
            foreach(var field in Sliders(shape.GetType()))
            {
                var slider=(MyBrushGUIPropertyNumberSlider)field.GetValue(shape);
                float value=MathHelper.Clamp(slider.Value*factor,slider.ValueMin,slider.ValueMax);
                if(value==slider.Value && !apply) continue;
                changed|=value!=slider.Value;
                slider.Value=value; slider.ValueChanged?.Invoke();
            }
            if(changed) hand.Save();
        }
        private static readonly Dictionary<Type,FieldInfo[]> sliders=new Dictionary<Type,FieldInfo[]>();
        internal static FieldInfo[] Sliders(Type brush)
        {
            if(!sliders.TryGetValue(brush,out var fields))
                sliders[brush]=fields=AccessTools.GetDeclaredFields(brush).Where(f=>!f.IsStatic && f.FieldType==typeof(MyBrushGUIPropertyNumberSlider)).ToArray();
            return fields;
        }
        // Pulsed controls are read only while no menu is open; the game opens any resulting screen itself.
        private static ActionChoice Control(string label,MyStringId control) => new ActionChoice(label,()=>NativeActions.Pulse(control));
        private static readonly ActionChoice[] actions={
            Control("Brush settings",MyControlsSpace.VOXEL_HAND_SETTINGS),
            Control("Larger brush",MyControlsSpace.VOXEL_SCALE_UP),Control("Smaller brush",MyControlsSpace.VOXEL_SCALE_DOWN),VoxelTools.StickAction,
            new ActionChoice(()=>"Snap to voxel: "+(Native?.SnapToVoxel==true ? "on":"off"),()=> { if(Native!=null) Native.SnapToVoxel=!Native.SnapToVoxel; },historyKey:"Snap to voxel"),
            new ActionChoice(()=>"Project to surface: "+(Native?.ProjectToVoxel==true ? "on":"off"),()=> { if(Native!=null) Native.ProjectToVoxel=!Native.ProjectToVoxel; },historyKey:"Project to surface"),
            Control("Revert terrain",MyControlsSpace.VOXEL_REVERT)};
        internal override ActionChoice[] Actions() => actions;

        private static IEnumerable<MyRadialMenuSection> Sections() =>
            MyDefinitionManager.Static?.GetRadialMenuDefinition("VoxelHand")?.CurrentSections ?? Enumerable.Empty<MyRadialMenuSection>();
        internal override ToolbarWheel.Group[] Materials() =>
            Sections().Select(section=>new ToolbarWheel.Group(MyTexts.GetString(section.Label),
                section.Items.Where(item=>item.GetType()==itemMaterial?.DeclaringType && item.Enabled()).Select(item=>new ActionChoice(Label(item),()=> {
                    if(Active && item.Enabled()) item.Activate();
                },icon:item.GetIcon(),enabled:()=>Active && item.Enabled())).ToArray(),true)).Where(g=>g.Items.Length>0).ToArray();
        private static string Label(MyRadialMenuItem item) => MyTexts.GetString(MyStringId.GetOrCompute(item.LabelName));
        // The native gamepad radial icons, shared by every voxel picker.
        internal static string Icon(string subtype)
        {
            var item=Item(subtype);
            if(item!=null) return item.GetIcon();
            return MyDefinitionManager.Static?.TryGetVoxelMaterialDefinition(subtype,out var definition)==true ? definition.VoxelHandPreview:null;
        }
        private static MyRadialMenuItem Item(string subtype) => itemMaterial==null || MyDefinitionManager.Static==null ? null :
            Sections().SelectMany(s=>s.Items).FirstOrDefault(i=>i.GetType()==itemMaterial.DeclaringType &&
                ((VRage.ObjectBuilders.SerializableDefinitionId)itemMaterial.GetValue(i)).SubtypeName==subtype);
        internal override bool Selected(out string name,out string icon)
        {
            name=icon=null;
            if(material==null || Native==null) return false;
            var definition=MyDefinitionManager.Static.GetVoxelMaterialDefinition((byte)material.GetValue(Native));
            if(definition==null) return false;
            var item=Item(definition.Id.SubtypeName);
            name=item!=null ? Label(item):VoxelTools.Words(definition.Id.SubtypeName);
            icon=item?.GetIcon() ?? definition.VoxelHandPreview;
            return true;
        }
    }

    // Digi's Concrete Tool and its Terrain Tool fork share one session component layout.
    internal sealed class DigiTool : VoxelTool
    {
        internal readonly Type Type;
        private readonly FieldInfo instance,holding,distance,scale,placeMatrix,terrain,mode,minTerrain,maxTerrain,names;
        private readonly float minDistance,maxDistance,minScale,maxScale,scaleStep;
        internal bool Terrain => terrain!=null;
        private DigiTool(Type type)
        {
            Type=type;
            instance=AccessTools.Field(type,"Instance"); holding=AccessTools.Field(type,"holdingTool");
            distance=AccessTools.Field(type,"placeDistance"); scale=AccessTools.Field(type,"placeScale"); placeMatrix=AccessTools.Field(type,"placeMatrix");
            minDistance=Constant(type,"MIN_DISTANCE"); maxDistance=Constant(type,"MAX_DISTANCE");
            minScale=Constant(type,"MIN_SCALE"); maxScale=Constant(type,"MAX_SCALE"); scaleStep=Constant(type,"SCALE_STEP");
            var names=AccessTools.Field(type,"TERRAIN_NAMES");
            if(names!=null && names.FieldType==typeof(string[]))
            {
                this.names=names; terrain=AccessTools.Field(type,"selectedTerrainIndex"); mode=AccessTools.Field(type,"tool_mode");
                minTerrain=AccessTools.Field(type,"minTerrainIndex"); maxTerrain=AccessTools.Field(type,"maxTerrainIndex");
                if(terrain==null || mode==null || minTerrain==null || maxTerrain==null) terrain=null;
            }
        }
        private static float Constant(Type type,string name)
        {
            var field=AccessTools.Field(type,name);
            return field!=null && field.IsLiteral ? Convert.ToSingle(field.GetRawConstantValue()):float.NaN;
        }
        internal static DigiTool TryCreate(Type type)
        {
            var tool=new DigiTool(type);
            bool valid=tool.instance!=null && tool.instance.IsStatic && tool.holding!=null && tool.distance?.FieldType==typeof(float) &&
                tool.scale?.FieldType==typeof(float) && tool.placeMatrix?.FieldType==typeof(MatrixD) &&
                new[] {tool.minDistance,tool.maxDistance,tool.minScale,tool.maxScale,tool.scaleStep}.All(v=>!float.IsNaN(v));
            return valid ? tool:null;
        }
        private object Mod => instance.GetValue(null);
        internal override bool Active => Mod is object mod && holding.GetValue(mod)!=null &&
            MySession.Static?.LocalCharacter is MyCharacter character && MySession.Static.ControlledEntity==character;
        internal override string Title => Terrain ? "Terrain tool":"Concrete tool";
        internal override MyStringId PaintControl => MyControlsSpace.CUBE_COLOR_CHANGE;
        // Grip held is the mod's Ctrl removal modifier; rotate mode never holds it, since Ctrl also snaps rotation to 90 degrees.
        internal bool Removing { get; private set; }
        internal override void Queue(ActionFrame frame,bool primary,bool secondary)
        {
            Removing=secondary && !PlacementControls.Adjusting;
            if(primary) frame.Queue(MyControlsSpace.PRIMARY_TOOL_ACTION);
        }
        internal override bool TryOrientation(out MatrixD matrix)
        {
            matrix=MatrixD.Identity;
            if(!(Mod is object mod)) return false;
            matrix=(MatrixD)placeMatrix.GetValue(mod);
            return true;
        }
        internal override void Rotate(int control,float dt) => NativeActions.Pulse(VoxelTools.RotationControls[ModControl(control)]);
        // The mod turns horizontal input the opposite way to the cube builder.
        internal static int ModControl(int control) => control==2 ? 3 : control==3 ? 2 : control;
        protected override void Move(float factor)
        {
            if(Mod is object mod) distance.SetValue(mod,MathHelper.Clamp((float)distance.GetValue(mod)*factor,minDistance,maxDistance));
        }
        protected override void Resize(float factor)
        {
            if(Mod is object mod) scale.SetValue(mod,MathHelper.Clamp((float)scale.GetValue(mod)*factor,minScale,maxScale));
        }
        private void Scale(int steps)
        {
            if(Mod is object mod) scale.SetValue(mod,MathHelper.Clamp((float)scale.GetValue(mod)+steps*scaleStep,minScale,maxScale));
        }
        internal void ResetModifiers() => Removing=false;
        private ActionChoice[] actions;
        internal override ActionChoice[] Actions() => actions ?? (actions=new[] {
            new ActionChoice("Larger box",()=>Scale(1)),new ActionChoice("Smaller box",()=>Scale(-1)),VoxelTools.StickAction,
            new ActionChoice("Snap mode",()=>NativeActions.Pulse(MyControlsSpace.FREE_ROTATION)),
            new ActionChoice("Snap lock",VoxelTools.Lock),
            new ActionChoice("Align",()=>NativeActions.Pulse(MyControlsSpace.CUBE_DEFAULT_MOUNTPOINT)),
            new ActionChoice("Tool help",()=>NativeActions.Pulse(MyControlsSpace.SECONDARY_TOOL_ACTION))});
        // Terrain mode follows the selected type: the mod keeps soil/stone and snow/ice in separate index ranges.
        private const int IceStart=42;
        internal override ToolbarWheel.Group[] Materials()
        {
            if(!Terrain || !(Mod is object mod)) return new ToolbarWheel.Group[0];
            var all=(string[])names.GetValue(mod);
            ActionChoice Choice(int index) => new ActionChoice(VoxelTools.Words(all[index]),()=>Select(index),icon:VoxelHandTool.Icon(all[index]),enabled:()=>Active);
            return new[] {
                new ToolbarWheel.Group("Soil and stone",Enumerable.Range(0,Math.Min(IceStart,all.Length)).Select(Choice).ToArray(),true),
                new ToolbarWheel.Group("Snow and ice",Enumerable.Range(IceStart,Math.Max(0,all.Length-IceStart)).Select(Choice).ToArray(),true)};
        }
        private void Select(int index)
        {
            if(!Active || !(Mod is object mod)) return;
            var all=(string[])names.GetValue(mod);
            if(index<0 || index>=all.Length) return;
            bool ice=index>=IceStart;
            mode.SetValue(mod,ice ? "Ice Mode":"Soil Mode");
            minTerrain.SetValue(mod,ice ? IceStart:0); maxTerrain.SetValue(mod,ice ? all.Length-1:IceStart-1);
            terrain.SetValue(mod,index);
        }
        internal override bool Selected(out string name,out string icon)
        {
            name=icon=null;
            if(!Terrain || !(Mod is object mod)) return false;
            var all=(string[])names.GetValue(mod);
            int index=(int)terrain.GetValue(mod);
            if(index<0 || index>=all.Length) return false;
            name=VoxelTools.Words(all[index]); icon=VoxelHandTool.Icon(all[index]);
            return true;
        }
    }
}
