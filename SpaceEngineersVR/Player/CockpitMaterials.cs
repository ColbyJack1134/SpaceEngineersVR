using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using VRage.FileSystem;
using VRage.Utils;
using VRageRender.Import;
using VRageRender.Messages;

namespace SpaceEngineersVR.Player
{
    internal static class CockpitMaterials
    {
        private sealed class Source
        {
            internal readonly string Subtype;
            internal readonly CockpitGeometry Geometry;
            internal Source(string subtype,CockpitGeometry geometry) { Subtype=subtype; Geometry=geometry; }
        }
        private static readonly ConcurrentDictionary<string,Source> models=new ConcurrentDictionary<string,Source>();
        private static readonly System.Type materialType=AccessTools.TypeByName("VRageRender.MyMeshMaterials1");
        private static readonly MethodInfo convert=AccessTools.Method(materialType,"ConvertImportDescToMeshMaterialInfo");
        private static readonly MethodInfo register=materialType.GetMethods(BindingFlags.Static|BindingFlags.NonPublic)
            .Single(m=>m.Name=="GetMaterialId" && m.GetParameters()[0].ParameterType==convert.ReturnType.MakeByRefType());
        private static readonly FieldInfo name=AccessTools.Field(convert.ReturnType,"Name"),index=AccessTools.Field(materialType,"MaterialNameIndex");
        internal static string Name(string subtype,string material) => "SEVR_CockpitMaterial_"+subtype+"_"+material;
        internal static void Prepare(string model,string subtype,CockpitGeometry geometry) => models[model]=new Source(subtype,geometry);
        internal static void Register(MyRenderMessageBase message)
        {
            if(!(message is MyRenderMessageAddRuntimeModel runtime) || !models.TryGetValue(runtime.Name,out var source)) return;
            foreach(var section in runtime.ModelData.Sections)
            {
                var material=source.Geometry.Materials.Single(p=>Name(source.Subtype,p.Key)==section.MaterialName);
                Register(section.MaterialName,material.Value);
            }
        }
        internal static void Register(string alias,MyMaterialDescriptor source)
        {
            var material=convert.Invoke(null,new object[] {source,MyFileSystem.ContentPath,null});
            var key=MyStringId.GetOrCompute(alias); name.SetValue(material,key);
            var id=register.Invoke(null,new[] {material,null});
            // Cached skin variants can replace a name lookup without changing existing mesh bindings.
            ((IDictionary)index.GetValue(null))[key]=id;
        }
        internal static Dictionary<MyStringId,MyTextureChange> Skin(string subtype,IEnumerable<string> materials,Dictionary<MyStringId,MyTextureChange> changes)
        {
            var result=new Dictionary<MyStringId,MyTextureChange>();
            if(changes!=null) foreach(string material in materials.Distinct())
                if(changes.TryGetValue(MyStringId.GetOrCompute(material),out var change)) result.Add(MyStringId.GetOrCompute(Name(subtype,material)),change);
            return result;
        }
    }
}
