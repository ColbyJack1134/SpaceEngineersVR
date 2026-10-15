#if SEVR_DIAGNOSTICS
using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using HarmonyLib;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.SessionComponents;
using SpaceEngineersVR.Patches;
using SpaceEngineersVR.Player;
using VRage.Game.Utils;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class VoxelToolTests
    {
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }

#pragma warning disable CS0169,CS0414,CS0649
        // Field layout shared by Digi's Concrete Tool and the Terrain Tool fork.
        private sealed class DigiFixture
        {
            public static DigiFixture Instance;
            internal object holdingTool;
            private float placeDistance=4f;
            private float placeScale=1f;
            private MatrixD placeMatrix=MatrixD.Identity;
            private const float SCALE_STEP=.25f,MIN_SCALE=.5f,MAX_SCALE=3f,MIN_DISTANCE=2f,MAX_DISTANCE=6f;
            private readonly string[] TERRAIN_NAMES=Enumerable.Range(0,52).Select(i=>"T"+i).ToArray();
            private int selectedTerrainIndex;
            private int minTerrainIndex;
            private int maxTerrainIndex=41;
            public string tool_mode="Soil Mode";
        }
        private sealed class MissingFixture { public static MissingFixture Instance; internal object holdingTool; private float placeDistance=4f; }
#pragma warning restore CS0169,CS0414,CS0649

        internal static void Run(Action<string> log)
        {
            NativeStatics();
            // Discovery runs every second in game; it must skip, not throw on, every assembly loaded here.
            foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
                Require(DigiTools.ToolTypes(assembly).Count==0,"Voxel tool discovery bound a non-Digi assembly: "+assembly.GetName().Name);
            NativeRotation();
            Digi();
            InstalledMods(log);
            var instructions=PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(MySessionComponentVoxelHand),nameof(MySessionComponentVoxelHand.UpdateBeforeSimulation)));
            var forward=AccessTools.PropertyGetter(typeof(MyCamera),nameof(MyCamera.ForwardVector));
            Require(instructions.Count(i=>i.Calls(forward))==2 && instructions.Count(i=>Equals(i.operand,AccessTools.Field(typeof(MyCamera),nameof(MyCamera.ViewMatrix))))==1,
                "Native voxel hand aim reads changed");
            Require(instructions.Any(i=>i.Calls(AccessTools.Method(typeof(Sandbox.Game.Entities.Character.MyCharacter),nameof(Sandbox.Game.Entities.Character.MyCharacter.GetHeadMatrix)))),
                "Native voxel hand no longer aims from the head matrix");
            log("PASS voxel tools: voxel hand statics only read in a world, brush rotation matches native cube builder axes, 60 deg/s at any frame rate, Digi distance/scale clamps, remove mode, wheel order and native aim reads");
        }

        private static void NativeRotation()
        {
            var hints=new MyBlockBuilderRotationHints();
            var setters=new[] {"RotationRightAxis","RotationRightDirection","RotationUpAxis","RotationUpDirection","RotationForwardAxis","RotationForwardDirection"}
                .Select(name=>AccessTools.PropertySetter(typeof(MyBlockBuilderRotationHints),name)).ToArray();
            var previous=MyCubeBuilder.Static;
            var builder=(MyCubeBuilder)FormatterServices.GetUninitializedObject(typeof(MyCubeBuilder));
            AccessTools.PropertySetter(typeof(MyCubeBuilder),nameof(MyCubeBuilder.DynamicMode)).Invoke(builder,new object[] {true});
            MyCubeBuilder.Static=builder;
            try
            {
                var start=MatrixD.CreateFromYawPitchRoll(.3,-.7,1.1);
                var directions=new[] {-1,1,1,-1,1,-1};
                foreach(var axes in new[] {new[] {0,1,2},new[] {1,2,0},new[] {2,0,1}})
                foreach(int flip in new[] {1,-1})
                {
                    for(int i=0;i<3;i++)
                    {
                        setters[i*2].Invoke(hints,new object[] {axes[i]});
                        setters[i*2+1].Invoke(hints,new object[] {i==1 ? -flip:flip});
                    }
                    for(int control=0;control<6;control++)
                    {
                        VoxelTools.HintAxis(hints,control,out int axis,out int sign);
                        int hintAxis=control<2 ? hints.RotationUpAxis : control<4 ? hints.RotationRightAxis : hints.RotationForwardAxis;
                        int hintDirection=control<2 ? hints.RotationUpDirection : control<4 ? hints.RotationRightDirection : hints.RotationForwardDirection;
                        var native=start;
                        Require(MyCubeBuilder.CalculateBlockRotation(hintAxis,directions[control]*hintDirection,ref native,out var expected,.2),"Native rotation rejected the fixture");
                        var actual=VoxelTools.Rotate(start,axis,sign,.2);
                        Require(Close(expected,actual),"Voxel brush rotation differs from the cube builder for control "+control);
                    }
                }
            }
            finally { MyCubeBuilder.Static=previous; }
            var moved=MatrixD.CreateWorld(new Vector3D(1e7,-3e6,5e5));
            var turned=VoxelTools.Rotate(moved,1,1,.5);
            Require(turned.Translation==moved.Translation && Math.Abs(turned.Up.Length()-1)<1e-9,"Brush rotation moved or scaled the brush");
            foreach(int hz in new[] {36,72,90,144})
            {
                var matrix=MatrixD.Identity;
                for(int i=0;i<hz;i++) matrix=VoxelTools.Rotate(matrix,1,1,MathHelper.ToRadians(VoxelHandTool.DegreesPerSecond)/hz);
                Require(Math.Abs(Vector3D.Dot(matrix.Forward,MatrixD.CreateFromAxisAngle(Vector3D.Up,MathHelper.ToRadians(60)).Forward)-1)<1e-9,"Brush rotation speed depends on frame rate at "+hz+" Hz");
            }
        }

        // The voxel hand's statics read the large grid size, 0 before a world loads; only the guarded accessor and in-world input may touch them.
        private static void NativeStatics()
        {
            var field=AccessTools.Field(typeof(MySessionComponentVoxelHand),nameof(MySessionComponentVoxelHand.Static));
            var allowed=new[] {AccessTools.Method(typeof(VoxelHandTool),nameof(VoxelHandTool.Loaded)),AccessTools.Method(typeof(KeyboardMouseInput),nameof(KeyboardMouseInput.Add))};
            Require((allowed[0].MethodImplementationFlags & System.Reflection.MethodImplAttributes.NoInlining)!=0,"Guarded voxel hand accessor can be inlined");
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.DeclaredOnly;
            foreach(var type in typeof(Plugin.Main).Assembly.GetTypes())
                foreach(var method in type.GetMethods(flags).Cast<System.Reflection.MethodBase>().Concat(type.GetConstructors(flags)))
                {
                    if(method.GetMethodBody()==null || method.ContainsGenericParameters || type.ContainsGenericParameters || allowed.Contains(method)) continue;
                    if(PatchProcessor.ReadMethodBody(method).Any(i=>Equals(i.Value,field)))
                        throw new Exception("Voxel hand statics read outside the world guard: "+type.FullName+"."+method.Name);
                }
        }
        internal static DigiTool Fixture() => DigiTool.TryCreate(typeof(DigiFixture));
        // Previews read the selected type and remove state through the production path, which needs the mod's static instance.
        internal static void Attach() => DigiFixture.Instance=new DigiFixture();
        internal static void Detach() => DigiFixture.Instance=null;
        private static bool Close(MatrixD a,MatrixD b)
        {
            for(int row=0;row<4;row++) for(int column=0;column<4;column++) if(Math.Abs(a[row,column]-b[row,column])>1e-9) return false;
            return true;
        }
        private static void Digi()
        {
            Require(DigiTool.TryCreate(typeof(MissingFixture))==null,"Digi adapter accepted an incomplete layout");
            var tool=DigiTool.TryCreate(typeof(DigiFixture));
            Require(tool!=null && tool.Terrain,"Digi adapter rejected the shared layout");
            var mod=new DigiFixture(); DigiFixture.Instance=mod;
            try
            {
                var distance=AccessTools.Field(typeof(DigiFixture),"placeDistance");
                var scale=AccessTools.Field(typeof(DigiFixture),"placeScale");
                for(int i=0;i<200;i++) tool.Adjust(1.05f);
                Require((float)distance.GetValue(mod)==6f,"Digi distance exceeded the mod maximum");
                for(int i=0;i<200;i++) tool.Adjust(.95f);
                Require((float)distance.GetValue(mod)==2f,"Digi distance went below the mod minimum");
                var actions=tool.Actions();
                actions.First(a=>a.Label=="Larger box").Run(); actions.First(a=>a.Label=="Larger box").Run();
                Require((float)scale.GetValue(mod)==1.5f,"Digi scale did not use the mod step");
                for(int i=0;i<20;i++) actions.First(a=>a.Label=="Smaller box").Run();
                Require((float)scale.GetValue(mod)==.5f,"Digi scale went below the mod minimum");
                Require(!tool.Active,"Digi fixture active without a held tool");
                distance.SetValue(mod,4f);
                for(int i=0;i<20;i++) tool.Adjust(PlacementControls.AdjustFactor(1,1/90f));
                for(int i=0;i<20;i++) tool.Adjust(PlacementControls.AdjustFactor(-1,1/90f));
                Require(Math.Abs((float)distance.GetValue(mod)-4f)<1e-4,"Pulling the stick back does not bring a voxel tool closer");
                VoxelTools.StickSizes=true;
                try
                {
                    scale.SetValue(mod,1f);
                    for(int i=0;i<20;i++) tool.Adjust(PlacementControls.AdjustFactor(1,1/90f));
                    Require((float)scale.GetValue(mod)>1.05f && (float)distance.GetValue(mod)==4f,"Size mode moved the box instead of resizing it");
                    for(int i=0;i<500;i++) tool.Adjust(PlacementControls.AdjustFactor(1,1/90f));
                    Require((float)scale.GetValue(mod)==3f,"Size mode exceeded the mod's box limit");
                }
                finally { VoxelTools.StickSizes=false; }
                RemoveMode(tool);
                var groups=ToolbarWheel.ToolGroups(tool.Title,tool.Actions(),tool.Materials(),new[] {GameActions.PauseAction});
                var pages=ToolbarWheel.Layout(groups,out var titles,out var variants);
                Require(titles.SequenceEqual(new[] {"Terrain tool"}.Concat(Enumerable.Repeat("Soil and stone",5)).Concat(Enumerable.Repeat("Snow and ice",2)).Concat(new[] {"Quick actions"})),
                    "Terrain wheel is not ordered tool actions, soil, ice, quick actions");
                Require(variants.SequenceEqual(new[] {false}.Concat(Enumerable.Repeat(true,7)).Concat(new[] {false})),"Terrain pages are not icon-only variant pages");
                Require(pages.Skip(1).Take(5).SelectMany(p=>p).Count(c=>c!=null)==42 && pages.Skip(6).Take(2).SelectMany(p=>p).Count(c=>c!=null)==10,"Terrain pages lost a type");
            }
            finally { DigiFixture.Instance=null; }
        }

        // Ctrl removes in the mod and also turns its rotation into single 90 degree presses, so it must not
        // reach the mod while the sticks rotate, and must not drop during a hold, which would place terrain instead.
        private static void RemoveMode(DigiTool tool)
        {
            var frame=new Player.Control.ActionFrame();
            var remove=tool.Actions().First(a=>a.HistoryKey=="Remove terrain");
            try
            {
                Require(remove.Label=="Remove terrain: off","Remove mode starts latched");
                tool.Queue(frame,true,true);
                Require(tool.Removing,"Grip and trigger do not remove");
                VoxelTools.Update(true);
                tool.Queue(frame,false,true);
                Require(!tool.Removing,"A held grip sends the rotation modifier while the sticks rotate");
                tool.Queue(frame,true,true);
                Require(tool.Removing,"A removal hold stops when the sticks move");
                VoxelTools.Update(false);
                remove.Run();
                Require(remove.Label=="Remove terrain: on","Remove mode did not latch");
                tool.Queue(frame,false,false);
                Require(tool.Removing,"Latched remove shows no preview before the trigger");
                tool.Queue(frame,true,false);
                Require(tool.Removing,"Latched remove does not remove on the trigger alone");
                VoxelTools.Update(true);
                tool.Queue(frame,false,false);
                Require(!tool.Removing,"Latched remove sends the rotation modifier while the sticks rotate");
                Require(tool.Selected(out string name,out _) && name=="Remove terrain","Latched remove is not shown in the tool slot");
                VoxelTools.Update(false);
                remove.Run();
                tool.Queue(frame,true,false);
                Require(!tool.Removing,"Unlatching remove left the tool removing");
                Require(tool.Selected(out name,out _) && name=="T0","The tool slot kept the remove label after unlatching");
            }
            finally { if(remove.Label=="Remove terrain: on") remove.Run(); tool.ResetModifiers(); VoxelTools.Update(false); }
        }

        // Reads the installed workshop sources so a mod update that renames what the adapter binds fails here.
        private static void InstalledMods(Action<string> log)
        {
            string workshop=Path.GetFullPath(Path.Combine(VRage.FileSystem.MyFileSystem.ContentPath,"..","..","..","workshop","content","244850"));
            foreach(var mod in new[] {Tuple.Create("396679430","Concrete","ConcreteToolMod"),Tuple.Create("2820001381","Terrain","TerrainToolMod")})
            {
                string folder=Path.Combine(workshop,mod.Item1,"Data","Scripts",mod.Item2);
                if(!Directory.Exists(folder)) { log("SKIP voxel tool source contract: "+mod.Item3+" not installed"); continue; }
                string source=File.ReadAllText(Path.Combine(folder,mod.Item3+".cs"));
                string input=File.ReadAllText(Path.Combine(folder,"InputHandler.cs"));
                foreach(string pattern in new[] {@"namespace Digi\.ConcreteTool",@"public static "+mod.Item3+@" Instance",@"internal IMyAutomaticRifleGun holdingTool;",
                    @"private float placeDistance\b",@"private float placeScale\b",@"private MatrixD placeMatrix\b",@"public void HoldingTool\(IMyCharacter character\)",
                    @"const float MIN_DISTANCE",@"const float MAX_DISTANCE",@"const float MIN_SCALE",@"const float MAX_SCALE",@"const float SCALE_STEP",
                    @"character\.GetHeadMatrix\(",@"IsAnyCtrlKeyPressed\(\)",@"FREE_ROTATION",
                    @"removeMode = ctrl;",@"bool increments = ctrl \|\| shift \|\| alt;",@"angleDeg = 90;"})
                    Require(Regex.IsMatch(source,pattern),mod.Item3+" no longer matches the adapter: "+pattern);
                Require(Regex.IsMatch(input,@"namespace Digi\b") && Regex.IsMatch(input,@"IsControlPressedIgnoreBlock\(MyStringId controlId, bool newPress"),mod.Item3+" input helper changed");
                if(mod.Item2=="Terrain")
                    foreach(string pattern in new[] {@"TERRAIN_NAMES",@"private int selectedTerrainIndex",@"private int minTerrainIndex",@"private int maxTerrainIndex",
                        "public string tool_mode",@"minTerrainIndex = 42;",@"""Ice Mode""",@"""Soil Mode""",@"IsNewKeyPressed\(MyKeys\.Multiply\) && alt"})
                        Require(Regex.IsMatch(source,pattern),mod.Item3+" terrain layout changed: "+pattern);
                log("PASS voxel tool source contract: "+mod.Item3);
            }
        }
    }
}
#endif
