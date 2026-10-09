using System;
using System.IO;
using System.Linq;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SpaceEngineersVR.Player;
using VRage.Game;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ModAPI;

namespace SpaceEngineersVR.Multiplayer
{
    internal static class CockpitMemory
    {
        internal static readonly Guid Key=new Guid("b6d86dc9-ff9b-421b-8942-88e3d464d809");
        internal const int Limit=262144,MaximumControls=160,CurrentLayout=13;
        internal sealed class Record
        {
            internal long Revision;
            internal string Toolbar="";
            internal FlightTuning Flight;
            internal bool[] Covers=new bool[0];
            internal int LayoutVersion=CurrentLayout;
        }
        private static VRage.Game.ModAPI.IMyUtilities Utilities => MyAPIUtilities.Static;
        internal static string Encode(Record record)
        {
            using(var stream=new MemoryStream())
            using(var writer=new BinaryWriter(stream))
            {
                writer.Write(6); writer.Write(record.Revision); writer.Write(record.Toolbar ?? "");
                writer.Write(record.Covers.Length); foreach(bool value in record.Covers) writer.Write(value);
                writer.Write(record.Flight!=null); record.Flight?.Write(writer);
                writer.Write(record.LayoutVersion);
                if(stream.Length>Limit) throw new InvalidDataException("Cockpit assignments exceed the storage limit.");
                return Convert.ToBase64String(stream.ToArray());
            }
        }
        internal static Record Decode(string value)
        {
            if(string.IsNullOrEmpty(value)) return new Record();
            if(value.Length>Limit*2) throw new InvalidDataException("Cockpit storage is too large.");
            using(var stream=new MemoryStream(Convert.FromBase64String(value),false))
            using(var reader=new BinaryReader(stream))
            {
                int version=reader.ReadInt32();
                if(stream.Length>Limit || version<1 || version>6) throw new InvalidDataException("Unsupported cockpit storage.");
                var result=new Record {Revision=reader.ReadInt64(),Toolbar=reader.ReadString()};
                int count=reader.ReadInt32();
                if(count<0 || count>MaximumControls || result.Revision<0) throw new InvalidDataException("Invalid cockpit storage.");
                result.Covers=new bool[count]; for(int i=0;i<count;i++) result.Covers[i]=reader.ReadBoolean();
                if(version>=2 && reader.ReadBoolean()) result.Flight=FlightTuning.Read(reader,version);
                result.LayoutVersion=version>=3 ? reader.ReadInt32():0;
                if(result.LayoutVersion<0 || result.LayoutVersion>CurrentLayout) throw new InvalidDataException("Unsupported cockpit layout.");
                if(stream.Position!=stream.Length) throw new InvalidDataException("Invalid cockpit storage length.");
                return result;
            }
        }
        internal static Record Read(MyCockpit seat)
        {
            var record=seat.Storage!=null && seat.Storage.TryGetValue(Key,out string value) ? Decode(value):new Record();
            if(Upgrade(record,seat.BlockDefinition.Id.SubtypeName)) Write(seat,record);
            return record;
        }
        internal static void UpgradeToolbar(MyObjectBuilder_Toolbar toolbar,string subtype,int version)
        {
            if(toolbar?.Slots==null) return;
            if(version==0 && subtype=="OpenCockpitLarge")
                toolbar.Slots=toolbar.Slots.Where(s=>s.Index>=4 && s.Index<61).Select(s=>
                { s.Index+=10; return s; }).ToList();
            if(version<2 && subtype=="LargeBlockCockpitSeat")
                toolbar.Slots=toolbar.Slots.Where(s=>s.Index<62 || s.Index>=68).Select(s=>
                { if(s.Index>=68) s.Index-=6; return s; }).ToList();
            if(version<3 && subtype=="LargeBlockCockpitSeat")
                toolbar.Slots=toolbar.Slots.Select(s=> { if(s.Index>=62) s.Index+=70; return s; }).ToList();
            if(version<4 && subtype=="LargeBlockCockpitSeat")
                toolbar.Slots=toolbar.Slots.Where(s=>TrimmedEnclosedSlot(s.Index)>=0).Select(s=>
                { s.Index=TrimmedEnclosedSlot(s.Index); return s; }).ToList();
            if(version<5 && subtype=="SmallBlockCapCockpit")
                toolbar.Slots=toolbar.Slots.Where(s=>s.Index>=0 && s.Index<15).Select(s=>
                { s.Index++; return s; }).ToList();
            if(version<6)
            {
                int inserted=subtype=="SmallBlockFlushCockpit" ? 7 :
                    subtype=="SmallBlockSuspendedControlSeat" || subtype=="LargeBlockSuspendedControlSeat" ||
                    subtype=="SmallBlockSuspendedControlSeatB" || subtype=="LargeBlockSuspendedControlSeatB" ? 9:0;
                if(inserted>0) toolbar.Slots=toolbar.Slots.Where(s=>s.Index==0).Select(s=>
                { s.Index=inserted; return s; }).ToList();
            }
            if(version<7 && subtype=="SmallBlockFlushCockpit")
                toolbar.Slots=toolbar.Slots.Where(s=>s.Index>=0 && s.Index<8).Select(s=>
                { s.Index=s.Index==7 ? 18:s.Index+8; return s; }).ToList();
            if(version<8 && (subtype=="SmallBlockSuspendedControlSeat" || subtype=="LargeBlockSuspendedControlSeat" ||
                subtype=="SmallBlockSuspendedControlSeatB" || subtype=="LargeBlockSuspendedControlSeatB"))
                toolbar.Slots=toolbar.Slots.Where(s=>s.Index>=0 && s.Index<10).Select(s=>
                { if(s.Index==9) s.Index=11; return s; }).ToList();
            if(version<9 && (subtype=="SmallBlockOpenSlopedCockpit" || subtype=="SmallBlockClosedSlopedCockpit"))
                toolbar.Slots=toolbar.Slots.Where(s=>s.Index>=0 && s.Index<3).Select(s=>
                { s.Index+=8; return s; }).ToList();
            if(version<10 && (subtype=="LargeBlockOpenSlopedCockpit" || subtype=="LargeBlockClosedSlopedCockpit"))
                toolbar.Slots=toolbar.Slots.Where(s=>s.Index>=0 && s.Index<17).Select(s=>
                { s.Index+=s.Index<14 ? 3:9; return s; }).ToList();
            if(version<11 && (subtype=="SmallBlockStandingCockpit" || subtype=="LargeBlockStandingCockpit"))
                toolbar.Slots=toolbar.Slots.Where(s=>s.Index>=0 && s.Index<7).Select(s=>
                { s.Index++; return s; }).ToList();
            if(version<12 && (subtype=="SmallBlockStandingCockpit" || subtype=="LargeBlockStandingCockpit"))
                toolbar.Slots=toolbar.Slots.Where(s=>s.Index>=0 && s.Index<12).Select(s=>
                { if(s.Index>0) s.Index+=6; return s; }).ToList();
            if(version<13 && (subtype=="SpeederCockpit" || subtype=="SpeederCockpitCompact"))
                toolbar.Slots=toolbar.Slots.Where(s=>s.Index==0).Select(s=> { s.Index=1; return s; }).ToList();
        }
        private static int TrimmedEnclosedSlot(int slot)
        {
            if(slot<0 || slot>=157) return -1;
            if(slot<9) return slot;
            if(slot<15) return -1;
            if(slot<32) return slot-6;
            if(slot<56) return -1;
            if(slot<132) return slot-30;
            if(slot<142) return -1;
            return slot-40;
        }
        internal static bool Upgrade(Record record,string subtype)
        {
            if(record.LayoutVersion>=CurrentLayout) return false;
            var toolbar=Toolbar(record.Toolbar);
            UpgradeToolbar(toolbar,subtype,record.LayoutVersion);
            if(toolbar!=null) record.Toolbar=Toolbar(toolbar);
            if(record.LayoutVersion==0 && subtype=="OpenCockpitLarge")
            {
                var covers=new bool[MaximumControls];
                for(int i=4;i<Math.Min(61,record.Covers.Length);i++) covers[i+10]=record.Covers[i];
                record.Covers=covers;
            }
            if(record.LayoutVersion<2 && subtype=="LargeBlockCockpitSeat")
            {
                var covers=new bool[MaximumControls];
                for(int i=0;i<Math.Min(62,record.Covers.Length);i++) covers[i]=record.Covers[i];
                for(int i=68;i<Math.Min(93,record.Covers.Length);i++) covers[i-6]=record.Covers[i];
                record.Covers=covers;
            }
            if(record.LayoutVersion<3 && subtype=="LargeBlockCockpitSeat")
            {
                var covers=new bool[MaximumControls];
                for(int i=0;i<Math.Min(87,record.Covers.Length);i++) covers[i<62 ? i:i+70]=record.Covers[i];
                record.Covers=covers;
            }
            if(record.LayoutVersion<4 && subtype=="LargeBlockCockpitSeat")
            {
                var covers=new bool[MaximumControls];
                for(int i=0;i<Math.Min(157,record.Covers.Length);i++)
                {
                    int slot=TrimmedEnclosedSlot(i);
                    if(slot>=0) covers[slot]=record.Covers[i];
                }
                record.Covers=covers;
            }
            if(record.LayoutVersion<5 && subtype=="SmallBlockCapCockpit")
            {
                var covers=new bool[MaximumControls];
                for(int i=0;i<Math.Min(14,record.Covers.Length);i++) covers[i+1]=record.Covers[i];
                record.Covers=covers;
            }
            if(record.LayoutVersion<10 && (subtype=="LargeBlockOpenSlopedCockpit" || subtype=="LargeBlockClosedSlopedCockpit"))
            {
                var covers=new bool[MaximumControls];
                for(int i=0;i<Math.Min(14,record.Covers.Length);i++) covers[i+3]=record.Covers[i];
                record.Covers=covers;
            }
            if(record.LayoutVersion<11 && (subtype=="SmallBlockStandingCockpit" || subtype=="LargeBlockStandingCockpit"))
            {
                var covers=new bool[MaximumControls];
                for(int i=0;i<Math.Min(6,record.Covers.Length);i++) covers[i+1]=record.Covers[i];
                record.Covers=covers;
            }
            if(record.LayoutVersion<12 && (subtype=="SmallBlockStandingCockpit" || subtype=="LargeBlockStandingCockpit"))
            {
                var covers=new bool[MaximumControls];
                for(int i=1;i<Math.Min(7,record.Covers.Length);i++) covers[i+6]=record.Covers[i];
                record.Covers=covers;
            }
            record.LayoutVersion=CurrentLayout;
            return true;
        }
        internal static void Write(MyCockpit seat,Record value)
        {
            string encoded=Encode(value);
            if(seat.Storage==null) seat.Storage=new MyModStorageComponent();
            seat.Storage[Key]=encoded;
        }
        internal static MyObjectBuilder_Toolbar Toolbar(string xml)
            => string.IsNullOrEmpty(xml) ? null:Utilities.SerializeFromXML<MyObjectBuilder_Toolbar>(xml);
        internal static string Toolbar(MyObjectBuilder_Toolbar value)
        {
            value.ToolbarType=MyToolbarType.ButtonPanel;
            return Utilities.SerializeToXML(value);
        }
        internal static bool ValidToolbar(string xml,int count)
        {
            if(xml==null || xml.Length>Limit/2) return false;
            var toolbar=Toolbar(xml);
            if(toolbar==null) return true;
            if(toolbar.ToolbarType!=MyToolbarType.ButtonPanel || toolbar.Slots==null || toolbar.Slots.Count>count) return false;
            var used=new bool[count];
            foreach(var slot in toolbar.Slots)
            {
                if(slot.Index<0 || slot.Index>=count || used[slot.Index]) return false;
                used[slot.Index]=true;
                if(slot.Data!=null && !(slot.Data is MyObjectBuilder_ToolbarItemTerminalBlock) && !(slot.Data is MyObjectBuilder_ToolbarItemTerminalGroup) &&
                    !(slot.Data is MyObjectBuilder_ToolbarItemWeapon)) return false;
            }
            return true;
        }
        internal static void Remap(MyObjectBuilder_CubeBlock block,IMyRemapHelper helper)
        {
            if(!(block is MyObjectBuilder_Cockpit) || block.ComponentContainer?.Components==null) return;
            foreach(var component in block.ComponentContainer.Components)
            {
                var storage=component.Component as MyObjectBuilder_ModStorageComponent;
                if(storage?.Storage?.Dictionary==null || !storage.Storage.Dictionary.TryGetValue(Key,out string value)) continue;
                var record=Decode(value);
                var toolbar=Toolbar(record.Toolbar);
                if(toolbar!=null) { toolbar.Remap(helper); record.Toolbar=Toolbar(toolbar); }
                record.Revision=0;
                storage.Storage.Dictionary[Key]=Encode(record);
            }
        }
    }
}
