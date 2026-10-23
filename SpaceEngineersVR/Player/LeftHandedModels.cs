using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.Entities.Character;
using SpaceEngineersVR.Plugin;
using VRage.FileSystem;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.Models;
using VRage.Import;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender.Import;

namespace SpaceEngineersVR.Player
{
    // Copies of side-handled tools and side-sighted launchers with those parts reflected to the other side, so a left-handed hold
    // can mirror left/right and keep the tool upright. Only items held by a mirrored character use them.
    internal static class LeftHandedModels
    {
        internal static string Folder => Path.Combine(MyFileSystem.UserDataPath,"SEVR","LeftHanded");
        private static readonly Dictionary<string,string> copies=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ready=new HashSet<string>();
        private static readonly Dictionary<MyEntity,string> originals=new Dictionary<MyEntity,string>();
        private static bool failed;

        internal static bool Ready(WeaponProfile profile) => ready.Contains(profile.Item);

        internal static void Prepare()
        {
            copies.Clear(); ready.Clear(); originals.Clear(); failed=false;
            WeaponProfile.SideSwapped=Ready;
            if(MyDefinitionManager.Static==null) return;
            foreach(var profile in WeaponProfile.All.Where(p=>p.SideAxis.HasValue))
            {
                try
                {
                    var id=new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject),profile.Item);
                    if(!MyDefinitionManager.Static.TryGetPhysicalItemDefinition(id,out var definition) || definition.Model==null) continue;
                    // Mods that load the model from another path keep their own mesh and the upside-down hold.
                    if(!definition.Model.Replace('\\','/').EndsWith(profile.Model,StringComparison.OrdinalIgnoreCase)) continue;
                    string source=Path.IsPathRooted(definition.Model) ? definition.Model:Path.Combine(MyFileSystem.ContentPath,definition.Model);
                    string copy=Path.Combine(Folder,profile.Model.Replace('/',Path.DirectorySeparatorChar));
                    Build(source,copy,profile.SideAxis.Value,profile.SideCut);
                    copies[definition.Model]=copy;
                    HoloSight.Mirror(definition.Model,copy,profile.SideAxis.Value);
                    ready.Add(profile.Item);
                }
                catch(Exception ex) { Logger.Warning(ex,"Left-handed model unavailable for {0}",profile.Item); }
            }
            if(ready.Count>0) Logger.Info("Left-handed models: {0} items with moved side parts",ready.Count);
        }

        internal static void Build(string source,string copy,float axis,float cut)
        {
            HoloSight.Write(copy,Patch(File.ReadAllBytes(source),source,axis,cut));
            var tags=HoloSight.Tags(source,"Dummies","LODs");
            // Subparts load from the parent model's folder.
            foreach(var dummy in ((Dictionary<string,MyModelDummy>)tags["Dummies"]).Where(d=>d.Key.Contains("subpart_")))
            {
                if(!(dummy.Value.CustomData?.TryGetValue("file",out var file)==true && file is string name)) continue;
                string from=Path.Combine(Path.GetDirectoryName(source),name+".mwm"),to=Path.Combine(Path.GetDirectoryName(copy),name+".mwm");
                if(File.Exists(from)) HoloSight.Write(to,File.ReadAllBytes(from));
            }
            // LODs resolve against the folder above Models first, so patched copies there replace the installed ones.
            if(tags.TryGetValue("LODs",out var value) && value is MyLODDescriptor[] lods)
                foreach(var lod in lods.Where(l=>l.Model!=null))
                {
                    string from=lod.GetModelAbsoluteFilePath(source);
                    string relative=lod.Model.EndsWith(".mwm",StringComparison.OrdinalIgnoreCase) ? lod.Model:lod.Model+".mwm";
                    string root=copy.Substring(0,copy.ToLowerInvariant().IndexOf("models",StringComparison.Ordinal));
                    if(from==null || !File.Exists(from)) continue;
                    // A LOD that cannot be patched keeps resolving to the installed one.
                    try { HoloSight.Write(Path.Combine(root,relative),Patch(File.ReadAllBytes(from),from,axis,cut)); }
                    catch(Exception ex) { Logger.Warning(ex,"Left-handed LOD unavailable: {0}",lod.Model); }
                }
        }

        // Reflects the connected parts whose bounds centre lies at item X below cut across the plane X=axis. Every array keeps its size,
        // so offsets in the file stay valid; each patched array must occur exactly once.
        internal static byte[] Patch(byte[] data,string file,float axis,float cut)
        {
            var tags=HoloSight.Tags(file,"Vertices","Normals","Tangents","Binormals","MeshParts","BoundingBox","BoundingSphere");
            var positions=(HalfVector4[])tags["Vertices"];
            var parts=(List<MyMeshPartInfo>)tags["MeshParts"];
            var selected=Selected(positions.Select(v=>VF_Packer.UnpackPosition(v)).ToArray(),parts,cut);
            if(selected.Count==0) throw new InvalidDataException("No side parts found");
            var result=(byte[])data.Clone();
            var moved=positions.ToArray(); var box=BoundingBox.CreateInvalid();
            foreach(int v in selected)
            {
                var p=VF_Packer.UnpackPosition(positions[v]); p.X=2*axis-p.X;
                moved[v]=VF_Packer.PackPosition(p); box.Include(p);
            }
            Replace(result,Array(positions.Select(v=>v.PackedValue)),Array(moved.Select(v=>v.PackedValue)));
            foreach(string name in new[] {"Normals","Binormals","Tangents"})
            {
                if(!(tags.TryGetValue(name,out var value) && value is Byte4[] packed) || packed.Length!=positions.Length) continue;
                var changed=packed.ToArray();
                foreach(int v in selected)
                {
                    if(name=="Tangents")
                    {
                        // Reflection flips the bitangent handedness.
                        var t=VF_Packer.UnpackTangentSign(ref changed[v]); t.X=-t.X; t.W=-t.W;
                        changed[v]=VF_Packer.PackTangentSignB4(ref t);
                    }
                    else { var n=VF_Packer.UnpackNormal(changed[v]); n.X=-n.X; changed[v]=VF_Packer.PackNormalB4(ref n); }
                }
                Replace(result,Array(packed.Select(b=>b.PackedValue)),Array(changed.Select(b=>b.PackedValue)));
            }
            foreach(var part in parts)
            {
                var indices=part.m_indices.ToArray(); var swapped=indices.ToArray(); bool any=false;
                for(int i=0;i<indices.Length;i+=3)
                    if(selected.Contains(indices[i])) { swapped[i+1]=indices[i+2]; swapped[i+2]=indices[i+1]; any=true; }
                if(any) Replace(result,Array(indices),Array(swapped));
            }
            var bounds=(BoundingBox)tags["BoundingBox"]; var sphere=(BoundingSphere)tags["BoundingSphere"];
            var grown=bounds; grown.Include(box);
            Replace(result,Floats(bounds.Min.X,bounds.Min.Y,bounds.Min.Z,bounds.Max.X,bounds.Max.Y,bounds.Max.Z),Floats(grown.Min.X,grown.Min.Y,grown.Min.Z,grown.Max.X,grown.Max.Y,grown.Max.Z));
            var reach=BoundingSphere.CreateMerged(sphere,BoundingSphere.CreateFromBoundingBox(grown));
            Replace(result,Floats(sphere.Center.X,sphere.Center.Y,sphere.Center.Z,sphere.Radius),Floats(reach.Center.X,reach.Center.Y,reach.Center.Z,reach.Radius));
            return result;
        }

        // Vertices of connected parts (welded at 0.1 mm) whose bounds centre lies below cut on X.
        internal static HashSet<int> Selected(Vector3[] positions,List<MyMeshPartInfo> parts,float cut)
        {
            var welded=new Dictionary<Vector3I,int>(); var weld=new int[positions.Length];
            for(int v=0;v<positions.Length;v++)
            {
                var key=Vector3I.Round(positions[v]*1e4f);
                if(!welded.TryGetValue(key,out weld[v])) welded[key]=weld[v]=v;
            }
            var root=Enumerable.Range(0,positions.Length).ToArray();
            int Find(int v) { while(root[v]!=v) v=root[v]=root[root[v]]; return v; }
            foreach(var part in parts)
                for(int i=0;i+2<part.m_indices.Count;i+=3)
                {
                    int a=Find(weld[part.m_indices[i]]);
                    for(int k=1;k<3;k++) { int b=Find(weld[part.m_indices[i+k]]); if(a!=b) root[b]=a; }
                }
            var bounds=new Dictionary<int,BoundingBox>();
            var used=new HashSet<int>(parts.SelectMany(p=>p.m_indices));
            foreach(int v in used)
            {
                int r=Find(weld[v]);
                var b=bounds.TryGetValue(r,out var found) ? found:BoundingBox.CreateInvalid();
                b.Include(positions[v]); bounds[r]=b;
            }
            return new HashSet<int>(used.Where(v=>bounds[Find(weld[v])].Center.X<cut));
        }

        private static byte[] Array<T>(IEnumerable<T> values) where T:struct
        {
            var list=values.ToArray();
            int size=System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));
            var bytes=new byte[4+list.Length*size];
            System.Buffer.BlockCopy(BitConverter.GetBytes(list.Length),0,bytes,0,4);
            System.Buffer.BlockCopy(list,0,bytes,4,list.Length*size);
            return bytes;
        }
        private static byte[] Floats(params float[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();
        private static void Replace(byte[] data,byte[] from,byte[] to)
        {
            int at=HoloSight.Find(data,from,0);
            if(at<0 || HoloSight.Find(data,from,at+1)>=0) throw new InvalidDataException("Model array not found once in the file");
            System.Buffer.BlockCopy(to,0,data,at,to.Length);
        }

        // Swaps the held item's render model, keeping the entity and its subpart entities, which tools cache.
        internal static void Sync(MyCharacter character)
        {
            if(failed || copies.Count==0) return;
            try
            {
                if(originals.Count>0) foreach(var closed in originals.Keys.Where(e=>e.Closed).ToArray()) originals.Remove(closed);
                if(!(character?.CurrentWeapon is MyEntity item) || item.Render==null || item.Model==null) return;
                bool mirrored=Multiplayer.HeldItemPose.Mirrored(character);
                if(mirrored && !originals.ContainsKey(item) && copies.TryGetValue(item.Model.AssetName,out var copy))
                { originals[item]=item.Model.AssetName; Swap(item,copy); }
                else if(!mirrored && originals.TryGetValue(item,out var original))
                { originals.Remove(item); Swap(item,original); }
            }
            catch(Exception ex) { failed=true; Logger.Warning(ex,"Left-handed models disabled"); }
        }
        private static void Swap(MyEntity item,string model)
        {
            item.Render.RemoveRenderObjects();
            item.Render.ModelStorage=MyModels.GetModelOnlyData(model);
            item.Render.AddRenderObjects();
            if(item.Subparts!=null) foreach(var subpart in item.Subparts.Values) { subpart.Render.RemoveRenderObjects(); subpart.Render.AddRenderObjects(); }
        }
    }
}
