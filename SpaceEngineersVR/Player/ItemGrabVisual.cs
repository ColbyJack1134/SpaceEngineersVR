using System;
using System.Collections.Generic;
using System.Linq;
using VRage.FileSystem;
using VRage.Game.Entity;
using VRage.Import;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;

namespace SpaceEngineersVR.Player
{
    internal static class ItemGrabVisual
    {
        private sealed class Part { internal uint Actor,Parent=uint.MaxValue; internal string[] Materials; internal int State=-1; internal bool Visible; }
        private static Part support,magazine;
        private static readonly Dictionary<string,MyModelData> models=new Dictionary<string,MyModelData>();
        private static MyEntity owner;
        private static bool failed;
        internal static void Reset()
        {
            foreach(var part in new[] {support,magazine}) if(part!=null)
            {
                MyRenderProxy.UpdateModelHighlight(part.Actor,null,null,null);
                MyRenderProxy.RemoveRenderObject(part.Actor,MyRenderProxy.ObjectType.Entity);
            }
            support=magazine=null; owner=null; failed=false;
        }
        internal static MyModelData Geometry(string model,Vector3? center)
        {
            var tags=GloveGeometry.GeometryTags(MyFileSystem.ContentPath,model);
            var vertices=((HalfVector4[])tags["Vertices"]).Select(v=>new Vector3(v.ToVector4())).ToArray();
            var normals=(Byte4[])tags["Normals"]; var tangents=(Byte4[])tags["Tangents"]; var uv=(HalfVector2[])tags["TexCoords0"];
            var result=new MyModelData(); result.Clear();
            foreach(var part in (List<MyMeshPartInfo>)tags["MeshParts"])
            {
                int start=result.Indices.Count;
                for(int i=0;i<part.m_indices.Count;i+=3)
                {
                    var indices=part.m_indices;
                    var middle=(vertices[indices[i]]+vertices[indices[i+1]]+vertices[indices[i+2]])/3;
                    if(center.HasValue && Vector3.DistanceSquared(middle,center.Value)>.078f*.078f) continue;
                    for(int j=0;j<3;j++)
                    {
                        int v=indices[i+j]; var normal=VF_Packer.UnpackNormal(normals[v].PackedValue); var point=vertices[v]+normal*.0012f;
                        result.Indices.Add(result.Positions.Count); result.Positions.Add(point); result.AABB.Include(point);
                        result.Normals.Add(normal); result.Tangents.Add(VF_Packer.UnpackNormal(tangents[v].PackedValue)); result.TexCoords.Add(uv[v].ToVector2());
                    }
                }
                if(result.Indices.Count>start) result.Sections.Add(new MyRuntimeSectionInfo {IndexStart=start,TriCount=(result.Indices.Count-start)/3,MaterialName=part.m_MaterialDesc.MaterialName});
            }
            return result;
        }
        private static Part Create(string key,string model,Vector3? center,MatrixD world)
        {
            string name="SEVR_Grab_"+key;
            if(!models.TryGetValue(key,out var source))
            {
                source=Geometry(model,center); models.Add(key,source);
                var message=MyRenderProxy.PrepareAddRuntimeModel(); var data=message.ModelData;
                data.Positions.AddRange(source.Positions); data.Normals.AddRange(source.Normals); data.Tangents.AddRange(source.Tangents);
                data.TexCoords.AddRange(source.TexCoords); data.Indices.AddRange(source.Indices); data.Sections.AddRange(source.Sections); data.AABB=source.AABB;
                message.ReplacedModel=null; MyRenderProxy.AddRuntimeModel(name,message);
            }
            if(source.Indices.Count==0) return null;
            uint actor=MyRenderProxy.CreateRenderEntity(name,name,world,MyMeshDrawTechnique.MESH,RenderFlags.Visible|RenderFlags.ForceOldPipeline,(CullingOptions)0,Color.White,Vector3.Zero);
            return new Part {Actor=actor,Visible=true,Materials=source.Sections.Select(s=>s.MaterialName).Distinct().ToArray()};
        }
        private static void Update(Part part,uint parent,bool visible,int state)
        {
            if(part==null) return;
            visible &= parent!=uint.MaxValue;
            if(part.Visible!=visible) { MyRenderProxy.UpdateRenderObjectVisibility(part.Actor,visible,false); part.Visible=visible; }
            if(!visible)
            {
                if(part.State!=-1) MyRenderProxy.UpdateModelHighlight(part.Actor,null,null,null);
                part.State=-1; return;
            }
            if(part.Parent!=parent)
            {
                // Share the native actor transform, including later weapon updates and render interpolation.
                MyRenderProxy.SetParentCullObject(part.Actor,parent,Matrix.Identity); part.Parent=parent;
            }
            if(state==part.State) return;
            foreach(var material in part.Materials) CockpitRender.ApplyFeedback(part.Actor,material,state);
            MyRenderProxy.UpdateModelHighlight(part.Actor,null,null,state==0 ? (Color?)null:new Color(160,220,255),state==0 ? -1:2);
            part.State=state;
        }
        internal static void Preview(string model,Vector3? contact,MatrixD world,uint parent,int state)
        {
            if(support==null) support=Create("fixture_"+model,model,contact,world);
            Update(support,parent,state!=2,state);
        }
        internal static void Update(MyEntity weapon,WeaponProfile profile,Vector3 supportPoint,int hover,bool held)
        {
            if(owner!=weapon) { Reset(); owner=weapon; }
            if(failed) return;
            try
            {
                weapon.Subparts.TryGetValue("magazine",out var mag);
                if(support==null && hover==1) support=Create(profile.Item+"_support",profile.Model,supportPoint,weapon.WorldMatrix);
                if(magazine==null && hover==2 && mag!=null) magazine=Create(profile.Item+"_magazine",mag.Model.AssetName,null,mag.WorldMatrix);
                Update(support,weapon.Render.GetRenderObjectID(),hover==1 && !held,1);
                Update(magazine,mag?.Render.GetRenderObjectID() ?? uint.MaxValue,hover==2 && !held,1);
            }
            catch(Exception error) { Reset(); owner=weapon; failed=true; Plugin.Logger.Warning(error,"Item grab highlights unavailable"); }
        }
    }
}
