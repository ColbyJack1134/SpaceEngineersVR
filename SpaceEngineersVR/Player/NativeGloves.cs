using System;
using System.Threading;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRage.FileSystem;
using VRage.Render.Scene;
using VRageMath;
using VRageRender;
using VRageRender.Import;

namespace SpaceEngineersVR.Player
{
    internal static class NativeGloves
    {
        private sealed class State
        {
            public string Model;
            public uint[] Actors;
            public Vector3 Color;
            public BoundingBox Bounds;
        }
        private static State current;
        private static int sequence;
        private static bool failed;
        private static readonly bool[] shown=new bool[2],distant=new bool[2];
        internal static bool Visible => shown[0] || shown[1];
        internal static bool Shown(bool left) => shown[left ? 0:1];
        internal static bool IsHand(uint id,out bool left)
        {
            var actors=Volatile.Read(ref current)?.Actors;
            left=actors?.Length>0 && actors[0]==id;
            return left || actors?.Length>1 && actors[1]==id || actors?.Length>2 && actors[2]==id;
        }
        internal static void Reset()
        {
            var previous=Interlocked.Exchange(ref current,null);
            if(previous!=null) foreach(uint id in previous.Actors) MyRenderProxy.RemoveRenderObject(id,MyRenderProxy.ObjectType.Entity);
            shown[0]=shown[1]=distant[0]=distant[1]=false;
        }
        internal static void Update()
        {
            if(!Main.WorldAvailable) { Reset(); failed=false; return; }
            if(!ThirdPersonView.Active) return;
            if(failed) return;
            try
            {
                var character=MySession.Static.LocalCharacter;
                string model=character.Definition.Model;
                if(current?.Model!=model) Create(model,character.ColorMask);
                if(current.Color!=character.ColorMask)
                {
                    foreach(uint id in current.Actors) MyRenderProxy.UpdateRenderEntity(id,null,character.ColorMask);
                    current.Color=character.ColorMask;
                }
            }
            catch(Exception ex) { Reset(); failed=true; Logger.Warning(ex,"Native observer gloves unavailable"); }
        }
        internal static void Create(string model,Vector3 color)
        {
            Reset(); MyRenderProxy.PreloadModel(model,forceOldPipeline:true);
            var state=new State { Model=model,Color=color,Actors=new uint[0],Bounds=BoundingBox.CreateInvalid() };
            Volatile.Write(ref current,state);
            for(int h=0;h<3;h++)
            {
                var geometry=GloveGeometry.Load(MyFileSystem.ContentPath,model,h==0);
                if(h<2) MenuHands.PublishGeometry(model,h==0,geometry);
                string name="SEVR_Glove_"+(++sequence);
                var message=MyRenderProxy.PrepareAddRuntimeModel(); var source=geometry.NativeModel; var data=message.ModelData;
                if(h==2) foreach(var vertex in geometry.Vertices) data.Positions.Add(vertex.Closed);
                else data.Positions.AddRange(source.Positions);
                data.Indices.AddRange(source.Indices);
                if(h==2) foreach(var vertex in geometry.Vertices) data.Normals.Add(vertex.ClosedNormal);
                else data.Normals.AddRange(source.Normals);
                data.Tangents.AddRange(source.Tangents); data.TexCoords.AddRange(source.TexCoords);
                data.Sections.AddRange(source.Sections); data.AABB=source.AABB; state.Bounds=source.AABB;
                message.ReplacedModel=null; MyRenderProxy.AddRuntimeModel(name,message);
                uint actor=MyRenderProxy.CreateRenderEntity(name,name,MatrixD.Identity,MyMeshDrawTechnique.MESH,
                    RenderFlags.Visible|RenderFlags.ForceOldPipeline,(CullingOptions)0,Color.White,color);
                var ids=new uint[h+1]; Array.Copy(state.Actors,ids,h); ids[h]=actor; Volatile.Write(ref state.Actors,ids);
            }
        }
        // Called on the render thread before culling either eye; both share one predicted pose.
        // Past the native far plane, MenuHands draws that hand in tracking space instead.
        internal static void Prepare(CameraRig.Frame frame,double far=double.MaxValue)
        {
            var state=Volatile.Read(ref current); shown[0]=shown[1]=false;
            if(state?.Actors.Length!=3) return;
            var hands=new[] { Player.HandL,Player.HandR,Player.HandR };
            var head=Player.Headset.renderPose.deviceToAbsolute.matrix.Translation;
            for(int h=0;h<2;h++)
            {
                double distance=frame==null ? 0 : (hands[h].RenderGripTracking.Translation-head).Length()*frame.UnitsPerMeter;
                distant[h]=distance>far*(distant[h] ? .8:.9);
            }
            for(int h=0;h<3;h++)
            {
                var actor=MyIDTracker<MyActor>.FindByID(state.Actors[h]);
                if(actor==null) continue;
                int side=h==0 ? 0:1;
                bool visible=frame?.ThirdPerson==true && hands[h].renderPose.isTracked && !distant[side] && (h==0 || (h==2)==SpatialUi.PinchingKnob);
                actor.SetVisibility(visible);
                if(!visible) { actor.UpdateBeforeDraw(); continue; }
                MatrixD pose=Alignment.Apply(Alignment.HandKey(hands[h]),CockpitHandPose.GripWrist(hands[h].RenderGripTracking));
                pose=MenuHands.AttachWrist(pose,hands[h]);
                pose*=frame.TrackingToWorld;
                actor.SetMatrix(ref pose);
                // The engine's pixel-size cull reuses the extent from the last proxy rebuild; refresh it for the zoom scale.
                actor.SetLocalAabb(state.Bounds);
                actor.UpdateBeforeDraw(); shown[side]=true;
            }
        }
        internal static bool Preview(MatrixD left,MatrixD right,bool pinch=false)
        {
            var state=current; if(state?.Actors.Length!=3) return false;
            MyIDTracker<MyActor>.FindByID(state.Actors[pinch ? 1:2])?.SetVisibility(false);
            for(int h=0;h<2;h++)
            {
                var actor=MyIDTracker<MyActor>.FindByID(state.Actors[h==1 && pinch ? 2:h]); if(actor==null) return false;
                MatrixD pose=h==0 ? left:right; actor.SetMatrix(ref pose); actor.SetVisibility(true); actor.UpdateBeforeDraw();
            }
            return true;
        }
    }
}
