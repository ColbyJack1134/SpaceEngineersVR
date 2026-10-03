using System;
using System.IO;
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
        internal const int Limit=262144,MaximumControls=64;
        internal sealed class Record
        {
            internal long Revision;
            internal string Toolbar="";
            internal bool[] Covers=new bool[0];
        }
        private static VRage.Game.ModAPI.IMyUtilities Utilities => MyAPIUtilities.Static;
        internal static string Encode(Record record)
        {
            using(var stream=new MemoryStream())
            using(var writer=new BinaryWriter(stream))
            {
                writer.Write(1); writer.Write(record.Revision); writer.Write(record.Toolbar ?? "");
                writer.Write(record.Covers.Length); foreach(bool value in record.Covers) writer.Write(value);
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
                if(stream.Length>Limit || reader.ReadInt32()!=1) throw new InvalidDataException("Unsupported cockpit storage.");
                var result=new Record {Revision=reader.ReadInt64(),Toolbar=reader.ReadString()};
                int count=reader.ReadInt32();
                if(count<0 || count>MaximumControls || result.Revision<0) throw new InvalidDataException("Invalid cockpit storage.");
                result.Covers=new bool[count]; for(int i=0;i<count;i++) result.Covers[i]=reader.ReadBoolean();
                if(stream.Position!=stream.Length) throw new InvalidDataException("Invalid cockpit storage length.");
                return result;
            }
        }
        internal static Record Read(MyCockpit seat)
        {
            return seat.Storage!=null && seat.Storage.TryGetValue(Key,out string value) ? Decode(value):new Record();
        }
        internal static void Write(MyCockpit seat,Record value)
        {
            string encoded=Encode(value);
            if(seat.Storage==null) seat.Storage=new MyModStorageComponent();
            seat.Storage[Key]=encoded;
        }
        internal static MyObjectBuilder_Toolbar Toolbar(string xml)
            => string.IsNullOrEmpty(xml) ? null:Utilities.SerializeFromXML<MyObjectBuilder_Toolbar>(xml);
        internal static string Toolbar(MyObjectBuilder_Toolbar value) => Utilities.SerializeToXML(value);
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
                if(slot.Data!=null && !(slot.Data is MyObjectBuilder_ToolbarItemTerminalBlock) && !(slot.Data is MyObjectBuilder_ToolbarItemTerminalGroup)) return false;
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
