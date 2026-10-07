using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using HarmonyLib;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.EntityComponents;
using SpaceEngineersVR.Multiplayer;
using SpaceEngineersVR.Player;
using VRage.Game;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ModAPI;
using VRage.Serialization;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    public static class MultiplayerTests
    {
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
        private static PlayerPose Pose(uint sequence=1)
            => new PlayerPose {Character=100,Sequence=sequence,Tracked=3,Left=Matrix.CreateTranslation(-.3f,1.3f,-.4f),
                Right=Matrix.CreateTranslation(.3f,1.2f,-.5f),LeftFingers=ArmSkeleton.Fingers.Point,RightFingers=ArmSkeleton.Fingers.Stick,RightTrigger=.8f};
        internal static void Run(Action<string> log)
        {
            var original=Pose(); var bytes=original.Encode(); var decoded=PlayerPose.Decode(bytes);
            Require(bytes.Length==PlayerPose.HeadSize && decoded!=null && decoded.Character==100 && decoded.Tracked==3,"Pose packet round trip failed");
            Require(decoded.Left==original.Left && decoded.Right==original.Right && Math.Abs(decoded.RightTrigger-.8f)<.005f,"Pose transforms or trigger lost");
            for(int n=0;n<bytes.Length;n++) if(n!=PlayerPose.LegacySize) Require(PlayerPose.Decode(bytes.Take(n).ToArray())==null,"Truncated pose accepted");
            var bad=(byte[])bytes.Clone(); bad[24]=32; Require(PlayerPose.Decode(bad)==null,"Unknown tracking bits accepted");
            var looking=Pose(); looking.Tracked|=PlayerPose.HeadTracked; looking.Head=Matrix.CreateFromYawPitchRoll(.6f,-.3f,.1f); looking.Head.Translation=new Vector3(0,1.6f,0);
            var lookingBytes=looking.Encode(); var lookingDecoded=PlayerPose.Decode(lookingBytes);
            Require(lookingDecoded!=null && lookingDecoded.Tracked==7 && Vector3.Distance(lookingDecoded.Head.Forward,looking.Head.Forward)<1e-4f,"Head pose lost in transit");
            var aiming=Pose(); aiming.Tracked|=PlayerPose.ToolRayTracked;
            aiming.ToolRay=Matrix.CreateFromYawPitchRoll(.8f,-.4f,.1f); aiming.ToolRay.Translation=new Vector3(.1f,1.1f,-.6f);
            var aimedBytes=aiming.Encode(); var aimed=PlayerPose.Decode(aimedBytes);
            Require(aimedBytes.Length==PlayerPose.Size && aimed!=null && (aimed.Tracked&PlayerPose.ToolRayTracked)!=0 &&
                Vector3.Distance(aimed.ToolRay.Forward,aiming.ToolRay.Forward)<1e-5f && Vector3.Distance(aimed.ToolRay.Translation,aiming.ToolRay.Translation)<1e-5f,"Independent tool ray lost in transit");
            for(int n=PlayerPose.HeadSize+1;n<PlayerPose.Size;n++) Require(PlayerPose.Decode(aimedBytes.Take(n).ToArray())==null,"Partial tool ray accepted");
            var headOnly=PlayerPose.Decode(aimedBytes.Take(PlayerPose.HeadSize).ToArray());
            Require(headOnly!=null && (headOnly.Tracked&PlayerPose.ToolRayTracked)==0,"Legacy pose invented a tool ray");
            foreach(bool tool in new[] {false,true})
            {
                var supported=tool ? aiming:Pose(); supported.Tracked|=PlayerPose.ItemSupported;
                var restored=PlayerPose.Decode(supported.Encode());
                Require(restored!=null && (restored.Tracked&PlayerPose.ItemSupported)!=0,"Support ownership lost in transit");
                supported.Tracked&=unchecked((byte)~PlayerPose.ItemSupported);
                Require((PlayerPose.Decode(supported.Encode()).Tracked&PlayerPose.ItemSupported)==0,"Support survived release in transit");
            }
            bad=(byte[])aimedBytes.Clone(); Array.Copy(BitConverter.GetBytes(float.NaN),0,bad,PlayerPose.HeadSize,4); Require(PlayerPose.Decode(bad)==null,"Nonfinite tool ray accepted");
            var toolStream=new PoseStream(); toolStream.Push(aiming,1);
            var movedRay=PlayerPose.Decode(aimedBytes); movedRay.Sequence++;
            movedRay.ToolRay.Translation+=Vector3.Right; toolStream.Push(movedRay,1.05);
            Require(Vector3.Distance(toolStream.ToolRay(1.075).Translation,Vector3.Lerp(aiming.ToolRay.Translation,movedRay.ToolRay.Translation,.5f))<.0001f,"Tool ray interpolation misses midpoint");
            var reacquired=new PoseStream(); reacquired.Push(Pose(),1); reacquired.Push(movedRay,1.05);
            Require(reacquired.ToolRay(1.05)==movedRay.ToolRay,"New tool ray interpolates from an untracked frame");
            var legacy=PlayerPose.Decode(lookingBytes.Take(PlayerPose.LegacySize).ToArray());
            Require(legacy!=null && legacy.Tracked==3 && legacy.Head==Matrix.Identity,"Version 1 hand-only packet rejected or given a head");
            bad=(byte[])lookingBytes.Clone(); Array.Copy(BitConverter.GetBytes(float.NaN),0,bad,PlayerPose.LegacySize+12,4); Require(PlayerPose.Decode(bad)==null,"Nonfinite head accepted");
            bad=(byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(float.NaN),0,bad,25,4); Require(PlayerPose.Decode(bad)==null,"Nonfinite hand accepted");
            bad=(byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(10f),0,bad,25,4); Require(PlayerPose.Decode(bad)==null,"Unbounded hand accepted");
            bad=(byte[])bytes.Clone(); bad[81]=255; Require(PlayerPose.Decode(bad)==null,"Unknown hand animation accepted");
            var stream=new PoseStream(); Require(stream.Push(decoded,1),"First pose rejected");
            var next=Pose(2); next.Left.Translation+=Vector3.Right;
            Require(stream.Push(next,1.05) && !stream.Push(decoded,1.06) && !stream.Push(next,1.07),"Duplicate/reordered pose accepted");
            Require(Vector3.Distance(stream.Hand(true,1.075).Translation,Vector3.Lerp(decoded.Left.Translation,next.Left.Translation,.5f))<.0001f,"Interpolation misses midpoint");
            Require(stream.Sample(1.55,out _) && !stream.Sample(1.56,out _),"Stale pose timeout failed");
            var seated=Pose(3); seated.Seat=50; stream.Push(seated,1.1);
            Require(stream.Hand(true,1.1)==seated.Left,"Seat change interpolates across reference frames");
            var wrapped=new PoseStream(); wrapped.Push(Pose(uint.MaxValue),2); Require(wrapped.Push(Pose(0),2.05),"Sequence wrap rejected");
            var record=new CockpitMemory.Record {Revision=3,Toolbar="<toolbar>parameter &amp; text</toolbar>",Covers=new[] {true,false,true}};
            var saved=CockpitMemory.Decode(CockpitMemory.Encode(record));
            Require(saved.Revision==3 && saved.Toolbar==record.Toolbar && saved.Covers.SequenceEqual(record.Covers),"Cockpit record loses assignments or covers");
            var oldToolbar=new MyObjectBuilder_Toolbar {ToolbarType=MyToolbarType.ButtonPanel,Slots=new List<MyObjectBuilder_Toolbar.Slot> {
                new MyObjectBuilder_Toolbar.Slot {Index=0},new MyObjectBuilder_Toolbar.Slot {Index=4},new MyObjectBuilder_Toolbar.Slot {Index=60} }};
            var old=new CockpitMemory.Record {LayoutVersion=0,Toolbar=CockpitMemory.Toolbar(oldToolbar),Covers=new bool[64]};
            old.Covers[14]=true;
            Require(CockpitMemory.Upgrade(old,"OpenCockpitLarge") && !CockpitMemory.Upgrade(old,"OpenCockpitLarge"),"Control Seat layout migration repeats");
            Require(CockpitMemory.Toolbar(old.Toolbar).Slots.Select(s=>s.Index).SequenceEqual(new[] {14,70}) && old.Covers[24] && !old.Covers[14],
                "Control Seat migration loses assignments or reuses removed flat buttons");
            var enclosedToolbar=new MyObjectBuilder_Toolbar {ToolbarType=MyToolbarType.ButtonPanel,Slots=new List<MyObjectBuilder_Toolbar.Slot> {
                new MyObjectBuilder_Toolbar.Slot {Index=61},new MyObjectBuilder_Toolbar.Slot {Index=62},
                new MyObjectBuilder_Toolbar.Slot {Index=67},new MyObjectBuilder_Toolbar.Slot {Index=68},
                new MyObjectBuilder_Toolbar.Slot {Index=91},new MyObjectBuilder_Toolbar.Slot {Index=92} }};
            var enclosed=new CockpitMemory.Record {LayoutVersion=1,Toolbar=CockpitMemory.Toolbar(enclosedToolbar),Covers=new bool[96]};
            enclosed.Covers[62]=enclosed.Covers[68]=enclosed.Covers[92]=true;
            Require(CockpitMemory.Upgrade(enclosed,"LargeBlockCockpitSeat") && !CockpitMemory.Upgrade(enclosed,"LargeBlockCockpitSeat"),
                "Enclosed cockpit layout migration repeats");
            Require(CockpitMemory.Toolbar(enclosed.Toolbar).Slots.Select(s=>s.Index).SequenceEqual(new[] {31,115,116}) &&
                enclosed.Covers[116] && !enclosed.Covers[68],"Removed controls shift surviving assignments incorrectly");
            var trimToolbar=new MyObjectBuilder_Toolbar {ToolbarType=MyToolbarType.ButtonPanel,Slots=new List<MyObjectBuilder_Toolbar.Slot>()};
            foreach(int index in new[] {8,9,14,15,31,32,55,56,131,132,141,142,154,155,156})
                trimToolbar.Slots.Add(new MyObjectBuilder_Toolbar.Slot {Index=index});
            var trim=new CockpitMemory.Record {LayoutVersion=3,Toolbar=CockpitMemory.Toolbar(trimToolbar),Covers=new bool[160]};
            trim.Covers[9]=trim.Covers[132]=trim.Covers[142]=trim.Covers[155]=true;
            Require(CockpitMemory.Upgrade(trim,"LargeBlockCockpitSeat") && !CockpitMemory.Upgrade(trim,"LargeBlockCockpitSeat") &&
                CockpitMemory.Toolbar(trim.Toolbar).Slots.Select(s=>s.Index).SequenceEqual(new[] {8,9,25,26,101,102,114,115,116}) &&
                trim.Covers[102] && trim.Covers[115] && !trim.Covers[9],"Enclosed trim loses retained controls or reuses removed assignments");
            for(int version=0;version<5;version++)
            {
                var cabToolbar=new MyObjectBuilder_Toolbar {ToolbarType=MyToolbarType.ButtonPanel,Slots=new List<MyObjectBuilder_Toolbar.Slot>()};
                for(int i=0;i<15;i++) cabToolbar.Slots.Add(new MyObjectBuilder_Toolbar.Slot {Index=i,
                    Data=new MyObjectBuilder_ToolbarItemTerminalBlock {BlockEntityId=100+i,_Action=i==14 ? "IncreaseVelocity":"OnOff"}});
                var cab=new CockpitMemory.Record {LayoutVersion=version,Toolbar=CockpitMemory.Toolbar(cabToolbar),Covers=new bool[15]};
                for(int i=0;i<14;i++) cab.Covers[i]=i%3==0;
                var priorCovers=(bool[])cab.Covers.Clone();
                Require(CockpitMemory.Upgrade(cab,"SmallBlockCapCockpit") && !CockpitMemory.Upgrade(cab,"SmallBlockCapCockpit"),
                    "Cab button migration repeats");
                var migrated=CockpitMemory.Toolbar(cab.Toolbar).Slots;
                Require(migrated.Select(s=>s.Index).SequenceEqual(Enumerable.Range(1,15)) && !cab.Covers[0] &&
                    Enumerable.Range(0,14).All(i=>cab.Covers[i+1]==priorCovers[i]) && migrated.All(s=>
                    s.Data is MyObjectBuilder_ToolbarItemTerminalBlock item && item.BlockEntityId==99+s.Index &&
                    item._Action==(s.Index==15 ? "IncreaseVelocity":"OnOff")),"Cab button migration loses switch, cover or analog assignments");
                var unchanged=new CockpitMemory.Record {LayoutVersion=version,Toolbar=CockpitMemory.Toolbar(cabToolbar),Covers=priorCovers};
                string priorToolbar=unchanged.Toolbar;
                CockpitMemory.Upgrade(unchanged,"CockpitOpen");
                Require(unchanged.Toolbar==priorToolbar && unchanged.Covers.SequenceEqual(priorCovers),"Cab migration changes another cockpit");
            }
            foreach(string subtype in new[] {"SmallBlockFlushCockpit","SmallBlockSuspendedControlSeat","LargeBlockSuspendedControlSeat",
                "SmallBlockSuspendedControlSeatB","LargeBlockSuspendedControlSeatB"})
            for(int version=0;version<6;version++)
            {
                int handle=subtype=="SmallBlockFlushCockpit" ? 18:11;
                var toolbar=new MyObjectBuilder_Toolbar {ToolbarType=MyToolbarType.ButtonPanel,Slots=new List<MyObjectBuilder_Toolbar.Slot> {
                    new MyObjectBuilder_Toolbar.Slot {Index=0,Data=new MyObjectBuilder_ToolbarItemTerminalBlock {BlockEntityId=456,_Action="IncreaseVelocity"}} }};
                var batchRecord=new CockpitMemory.Record {LayoutVersion=version,Toolbar=CockpitMemory.Toolbar(toolbar)};
                Require(CockpitMemory.Upgrade(batchRecord,subtype) && !CockpitMemory.Upgrade(batchRecord,subtype),"Batch cockpit migration repeats");
                var slots=CockpitMemory.Toolbar(batchRecord.Toolbar).Slots;
                Require(slots.Count==1 && slots[0].Index==handle && slots[0].Data is MyObjectBuilder_ToolbarItemTerminalBlock item &&
                    item.BlockEntityId==456 && item._Action=="IncreaseVelocity" && AnalogControl.IsHandle(subtype,handle) &&
                    Enumerable.Range(0,handle).All(i=>!AnalogControl.IsHandle(subtype,i)),"Batch controls lose or reuse the saved analog assignment");
            }
            foreach(string subtype in new[] {"SmallBlockSuspendedControlSeat","LargeBlockSuspendedControlSeat",
                "SmallBlockSuspendedControlSeatB","LargeBlockSuspendedControlSeatB"})
            for(int version=6;version<8;version++)
            {
                var toolbar=new MyObjectBuilder_Toolbar {ToolbarType=MyToolbarType.ButtonPanel,Slots=Enumerable.Range(0,10).Select(i=>
                    new MyObjectBuilder_Toolbar.Slot {Index=i,Data=new MyObjectBuilder_ToolbarItemTerminalBlock {BlockEntityId=800+i,
                        _Action=i==9 ? "IncreaseVelocity":"OnOff"}}).ToList()};
                var suspendedRecord=new CockpitMemory.Record {LayoutVersion=version,Toolbar=CockpitMemory.Toolbar(toolbar)};
                Require(CockpitMemory.Upgrade(suspendedRecord,subtype) && !CockpitMemory.Upgrade(suspendedRecord,subtype),"Suspended round button migration repeats");
                var slots=CockpitMemory.Toolbar(suspendedRecord.Toolbar).Slots;
                Require(slots.Select(s=>s.Index).SequenceEqual(new[] {0,1,2,3,4,5,6,7,8,11}) && slots.All(s=>
                    s.Data is MyObjectBuilder_ToolbarItemTerminalBlock item && item.BlockEntityId==800+(s.Index==11 ? 9:s.Index) &&
                    item._Action==(s.Index==11 ? "IncreaseVelocity":"OnOff")) && AnalogControl.IsHandle(subtype,11) &&
                    !AnalogControl.IsHandle(subtype,9) && !AnalogControl.IsHandle(subtype,10),
                    "Suspended round buttons lose existing assignments or analog classification");
            }
            foreach(string subtype in new[] {"SmallBlockOpenSlopedCockpit","SmallBlockClosedSlopedCockpit"})
            for(int version=0;version<9;version++)
            {
                var toolbar=new MyObjectBuilder_Toolbar {ToolbarType=MyToolbarType.ButtonPanel,Slots=Enumerable.Range(0,3).Select(i=>
                    new MyObjectBuilder_Toolbar.Slot {Index=i,Data=new MyObjectBuilder_ToolbarItemTerminalBlock {
                        BlockEntityId=900+i,_Action="IncreaseVelocity"}}).ToList()};
                var slopedRecord=new CockpitMemory.Record {LayoutVersion=version,Toolbar=CockpitMemory.Toolbar(toolbar)};
                Require(CockpitMemory.Upgrade(slopedRecord,subtype) && !CockpitMemory.Upgrade(slopedRecord,subtype),
                    "Small Sloped controls migration repeats");
                var slots=CockpitMemory.Toolbar(slopedRecord.Toolbar).Slots;
                Require(slots.Select(s=>s.Index).SequenceEqual(new[] {8,9,10}) && slots.All(s=>
                    s.Data is MyObjectBuilder_ToolbarItemTerminalBlock item && item.BlockEntityId==892+s.Index &&
                    item._Action=="IncreaseVelocity" && AnalogControl.IsHandle(subtype,s.Index)) &&
                    Enumerable.Range(0,8).All(i=>!AnalogControl.IsHandle(subtype,i)),
                    "Small Sloped controls lose analog assignments or reuse button slots");
            }
            foreach(string subtype in new[] {"LargeBlockOpenSlopedCockpit","LargeBlockClosedSlopedCockpit"})
            for(int version=0;version<10;version++)
            {
                var toolbar=new MyObjectBuilder_Toolbar {ToolbarType=MyToolbarType.ButtonPanel,Slots=Enumerable.Range(0,17).Select(i=>
                    new MyObjectBuilder_Toolbar.Slot {Index=i,Data=new MyObjectBuilder_ToolbarItemTerminalBlock {
                        BlockEntityId=1000+i,_Action=i<14 ? "OnOff":"IncreaseVelocity"}}).ToList()};
                var largeSlopedRecord=new CockpitMemory.Record {LayoutVersion=version,Toolbar=CockpitMemory.Toolbar(toolbar),
                    Covers=Enumerable.Range(0,17).Select(i=>i%3==0).ToArray()};
                Require(CockpitMemory.Upgrade(largeSlopedRecord,subtype) && !CockpitMemory.Upgrade(largeSlopedRecord,subtype),
                    "Large Sloped migration repeats");
                var slots=CockpitMemory.Toolbar(largeSlopedRecord.Toolbar).Slots;
                Require(slots.Select(s=>s.Index).SequenceEqual(Enumerable.Range(3,14).Concat(new[] {23,24,25})) && slots.All(s=>
                    s.Data is MyObjectBuilder_ToolbarItemTerminalBlock item && item.BlockEntityId==1000+s.Index-(s.Index<17 ? 3:9) &&
                    item._Action==(s.Index<17 ? "OnOff":"IncreaseVelocity")) &&
                    Enumerable.Range(0,14).All(i=>largeSlopedRecord.Covers[i+3]==(i%3==0)) &&
                    Enumerable.Range(0,26).All(i=>AnalogControl.IsHandle(subtype,i)==(i>=23)),
                    "Large Sloped migration loses assignments, cover states or analog classification");
            }
            var flushToolbar=new MyObjectBuilder_Toolbar {ToolbarType=MyToolbarType.ButtonPanel,Slots=Enumerable.Range(0,8).Select(i=>
                new MyObjectBuilder_Toolbar.Slot {Index=i,Data=new MyObjectBuilder_ToolbarItemTerminalBlock {BlockEntityId=700+i,
                    _Action=i==7 ? "IncreaseVelocity":"OnOff"}}).ToList()};
            var flushRecord=new CockpitMemory.Record {LayoutVersion=6,Toolbar=CockpitMemory.Toolbar(flushToolbar)};
            Require(CockpitMemory.Upgrade(flushRecord,"SmallBlockFlushCockpit") && !CockpitMemory.Upgrade(flushRecord,"SmallBlockFlushCockpit"),
                "Flush dial migration repeats");
            var flushSlots=CockpitMemory.Toolbar(flushRecord.Toolbar).Slots;
            Require(flushSlots.Select(s=>s.Index).SequenceEqual(new[] {8,9,10,11,12,13,14,18}) && flushSlots.All(s=>
                s.Data is MyObjectBuilder_ToolbarItemTerminalBlock item && item.BlockEntityId==700+(s.Index==18 ? 7:s.Index-8) &&
                item._Action==(s.Index==18 ? "IncreaseVelocity":"OnOff")),"Flush dial migration loses existing switch or analog assignments");
            foreach(var invalid in new[] {"invalid base64",Convert.ToBase64String(new byte[12]),new string('A',CockpitMemory.Limit*2+1)})
            {
                bool rejected=false; try { CockpitMemory.Decode(invalid); } catch { rejected=true; }
                Require(rejected,"Invalid cockpit storage accepted");
            }
            log("PASS multiplayer payloads: pose round trip, truncated/nonfinite/out-of-reach rejection, finger flags, ordering/wrap, interpolation, seat changes, stale timeout and cockpit record persistence.");
        }
        private sealed class Remap : IMyRemapHelper
        {
            private readonly Dictionary<long,long> ids=new Dictionary<long,long>();
            public long RemapEntityId(long id) { if(!ids.TryGetValue(id,out long mapped)) ids[id]=mapped=id+1000; return mapped; }
            public string RemapEntityName(long id) => "copy-"+id;
            public int RemapGroupId(string group,int id) => id+1000;
            public void Clear() => ids.Clear();
            public Dictionary<long,long> GetRemapInfo() => ids;
        }
        internal static void RunNative(Action<string> log)
        {
            var live=new Sandbox.Game.Screens.Helpers.MyToolbar(MyToolbarType.ButtonPanel,9,5);
            Require(CockpitMemory.ValidToolbar(CockpitMemory.Toolbar(live.GetObjectBuilder()),42),
                "Native cockpit toolbar export was rejected by host validation");
            var legacy=new MyObjectBuilder_Toolbar {ToolbarType=MyToolbarType.Character,Slots=new List<MyObjectBuilder_Toolbar.Slot> {
                new MyObjectBuilder_Toolbar.Slot {Index=8,Data=new MyObjectBuilder_ToolbarItemTerminalBlock {BlockEntityId=23,_Action="OnOff"}} }};
            Require(!CockpitMemory.ValidToolbar(((VRage.Game.ModAPI.IMyUtilities)Sandbox.ModAPI.MyAPIUtilities.Static).SerializeToXML(legacy),42) &&
                CockpitMemory.ValidToolbar(CockpitMemory.Toolbar(legacy),42) && legacy.Slots[0].Index==8,
                "Legacy local cockpit assignment did not normalize for host persistence");
            var toolbar=new MyObjectBuilder_Toolbar {ToolbarType=MyToolbarType.ButtonPanel,Slots=new List<MyObjectBuilder_Toolbar.Slot> {
                new MyObjectBuilder_Toolbar.Slot {Index=0,Data=new MyObjectBuilder_ToolbarItemTerminalBlock {BlockEntityId=21,_Action="OnOff"}},
                new MyObjectBuilder_Toolbar.Slot {Index=40,Data=new MyObjectBuilder_ToolbarItemTerminalGroup {BlockEntityId=22,GroupName="Ship lights",_Action="OnOff"}} }};
            var record=new CockpitMemory.Record {Revision=9,Toolbar=CockpitMemory.Toolbar(toolbar),Covers=new[] {true,false,true},Flight=new FlightTuning {Rotation=1.3f,Translation=.6f,TwistDeadzone=.12f}};
            Require(CockpitMemory.ValidToolbar(record.Toolbar,42) && !CockpitMemory.ValidToolbar(record.Toolbar,9),"Toolbar bounds validation failed");
            var component=new MyModStorageComponent(); component.SetValue(CockpitMemory.Key,CockpitMemory.Encode(record));
            var serialized=(MyObjectBuilder_ModStorageComponent)component.Serialize(true);
            Require(serialized?.Storage.Dictionary.ContainsKey(CockpitMemory.Key)==true,"Plugin storage did not survive native serialization");
            var block=new MyObjectBuilder_Cockpit {EntityId=20,ComponentContainer=new MyObjectBuilder_ComponentContainer {
                Components=new List<MyObjectBuilder_ComponentContainer.ComponentData> {
                    new MyObjectBuilder_ComponentContainer.ComponentData {TypeId=typeof(MyModStorageComponent).Name,Component=serialized}}}};
            var original=(MyObjectBuilder_Cockpit)block.Clone();
            block.Remap(new Remap());
            var copied=CockpitMemory.Decode(((MyObjectBuilder_ModStorageComponent)block.ComponentContainer.Components[0].Component).Storage.Dictionary[CockpitMemory.Key]);
            var copiedToolbar=CockpitMemory.Toolbar(copied.Toolbar);
            Require(block.EntityId==1020 && ((MyObjectBuilder_ToolbarItemTerminalBlock)copiedToolbar.Slots[0].Data).BlockEntityId==1021 &&
                ((MyObjectBuilder_ToolbarItemTerminalGroup)copiedToolbar.Slots[1].Data).BlockEntityId==1022,"Native copy remapping lost switch target references");
            Require(copied.Covers.SequenceEqual(record.Covers) && copied.Flight.Encode()==record.Flight.Encode(),"Native copy lost cover or flight settings");
            var untouched=CockpitMemory.Decode(((MyObjectBuilder_ModStorageComponent)original.ComponentContainer.Components[0].Component).Storage.Dictionary[CockpitMemory.Key]);
            Require(untouched.Toolbar==record.Toolbar && untouched.Revision==9,"Copy mutated original cockpit assignments");
            block.SetupForProjector(); var built=(MyObjectBuilder_Cockpit)block.Clone();
            var projected=CockpitMemory.Decode(((MyObjectBuilder_ModStorageComponent)built.ComponentContainer.Components[0].Component).Storage.Dictionary[CockpitMemory.Key]);
            Require(projected.Toolbar==copied.Toolbar && projected.Covers.SequenceEqual(record.Covers) && projected.Flight.Encode()==record.Flight.Encode(),"Projector setup/build clone discarded cockpit state");
            var restored=new MyModStorageComponent(); restored.Deserialize(serialized);
            Require(restored.GetValue(CockpitMemory.Key)==CockpitMemory.Encode(copied),"Storage deserialization changed data");
            log("PASS native cockpit persistence: registered-key serialization, typed toolbar bounds, block/group target remapping, original isolation, projector setup/build clone, cover states and deserialization. No world was loaded.");
        }
        public static void Companion(string assemblyPath,Action<string> log)
        {
            var assembly=System.Reflection.Assembly.LoadFrom(assemblyPath);
            Require(!assembly.GetReferencedAssemblies().Any(a=>a.Name=="OVRSharp" || a.Name=="SpaceEngineersVR"),"Companion requires the VR plugin/runtime");
            var support=assembly.GetType("SpaceEngineersVR.Multiplayer.MultiplayerSupport",true);
            try
            {
                support.GetMethod("Start").Invoke(null,null);
                int count=Harmony.GetAllPatchedMethods().Count(m=>Harmony.GetPatchInfo(m).Owners.Contains("SpaceEngineersVR.Multiplayer"));
                Require(count==23,"Companion did not attach all 23 native storage/actuator/arm/held-item patches");
                support.GetMethod("Update").Invoke(null,null);
            }
            finally { support.GetMethod("Stop").Invoke(null,null); }
            Require(!Harmony.HasAnyPatches("SpaceEngineersVR.Multiplayer"),"Companion left patches after shutdown");
            log("PASS flatscreen companion: independent assembly, 23 native patch attachments, no-world update and clean shutdown without OpenVR initialization.");
        }
        public static void Export(string game,string output,Action<string> log)
        {
            UiTests.Initialize(game,Path.Combine(output,"data")); Directory.CreateDirectory(output); Run(log);
            foreach(string scenario in new[] {"standing","wrist-local","wrist-remote","seated","look-left","look-down"})
            {
                var bones=ArmTests.InstalledBones();
                var packet=Pose();
                packet.Left=Matrix.CreateRotationY(.3f)*Matrix.CreateRotationX(-.2f); packet.Left.Translation=new Vector3(-.35f,1.20f,-.4f);
                packet.Right=Matrix.CreateRotationY(-.3f)*Matrix.CreateRotationX(-.1f); packet.Right.Translation=new Vector3(.35f,1.20f,-.4f);
                packet.RightFingers=ArmSkeleton.Fingers.Point;
                if(scenario.StartsWith("wrist"))
                {
                    packet.Left=Matrix.CreateFromYawPitchRoll(-.7f,-.4f,.7f); packet.Left.Translation=new Vector3(-.15f,1.35f,-.4f);
                    packet.Right.Translation=new Vector3(.1f,1.3f,-.55f); packet.RightFingers=ArmSkeleton.Fingers.Pinch;
                }
                if(scenario=="seated")
                {
                    foreach(string side in new[] {"L","R"})
                    {
                        var thigh=bones.Single(b=>b.Name=="SE_Rig"+side+"Thigh");
                        var calf=bones.Single(b=>b.Name=="SE_Rig"+side+"Calf");
                        var foot=bones.Single(b=>b.Name=="SE_Rig"+side+"Foot");
                        var upper=ArmMath.AimBone(thigh.AbsoluteTransform,calf.AbsoluteTransform.Translation-thigh.AbsoluteTransform.Translation,Vector3.Forward);
                        thigh.SetCompleteTransformFromAbsoluteMatrix(ref upper,false); thigh.ComputeAbsoluteTransform(true,true);
                        var lower=ArmMath.AimBone(calf.AbsoluteTransform,foot.AbsoluteTransform.Translation-calf.AbsoluteTransform.Translation,Vector3.Down);
                        calf.SetCompleteTransformFromAbsoluteMatrix(ref lower,false); calf.ComputeAbsoluteTransform(true,true);
                    }
                    packet.Left.Translation=new Vector3(-.35f,1.0f,-.35f); packet.Right.Translation=new Vector3(.35f,1.0f,-.35f);
                    packet.LeftFingers=packet.RightFingers=ArmSkeleton.Fingers.Stick;
                }
                var stream=new PoseStream(); stream.Push(PlayerPose.Decode(packet.Encode()),0); stream.Sample(0,out var received);
                foreach(bool left in new[] {true,false})
                {
                    string side=left ? "L":"R";
                    var arm=ArmSkeleton.Find(name=>bones.FirstOrDefault(b=>b.Name==name),"SE_Rig"+side+"Upperarm","SE_Rig"+side+"Forearm1","SE_Rig"+side+"Palm",left ? -1:1);
                    var target=stream.Hand(left,0);
                    Require(ArmSkeleton.Apply(arm,target,true,left && scenario=="wrist-local" ? 1:0,1,left ? received.LeftFingers:received.RightFingers,left ? received.LeftTrigger:received.RightTrigger),"Received arm pose failed");
                    Require(Vector3.Distance(arm.Palm.Bone.AbsoluteTransform.Translation,target.Translation)<.001f,"Received palm missed transmitted target");
                }
                if(scenario.StartsWith("look"))
                {
                    var head=new ArmSkeleton.SavedBone {Bone=ArmSkeleton.Head(name=>bones.FirstOrDefault(b=>b.Name==name),"HeadDummy")};
                    var look=scenario=="look-left" ? Matrix.CreateRotationY(.8f):Matrix.CreateRotationX(-.6f);
                    Require(ArmSkeleton.Look(head,look,1.4f),"Received head pose failed");
                    var turned=Matrix.Invert(head.Bone.GetAbsoluteRigTransform().GetOrientation())*head.Bone.AbsoluteTransform.GetOrientation();
                    Require(Vector3.Distance(turned.Forward,look.Forward)<.01f,"Remote head missed the transmitted direction");
                    Require(!ArmSkeleton.Look(head,new Matrix(),1.4f),"Invalid head pose accepted");
                }
                var export=new CockpitHandTests.PoseExport();
                foreach(var bone in bones)
                {
                    var m=bone.AbsoluteTransform;
                    export.absolute[bone.Name]=new[] {m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};
                }
                using(var file=File.Create(Path.Combine(output,scenario+".json")))
                    new DataContractJsonSerializer(typeof(CockpitHandTests.PoseExport),new DataContractJsonSerializerSettings {UseSimpleDictionaryFormat=true}).WriteObject(file,export);
            }
            log("PASS installed skeleton received-pose exports: standing, seated, local/remote wrist comparison and head turns; palms and head follow transmitted targets.");
        }
    }
}
