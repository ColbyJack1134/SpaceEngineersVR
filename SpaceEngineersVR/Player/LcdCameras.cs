using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.Game.World;
using VRage.Game.Entity;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Uses the Camera LCD plugin's script id and Custom Data format so screens set up for it keep working.
    internal static class LcdCameras
    {
        internal const string ScriptId="TSS_CameraDisplay";
        internal sealed class View
        {
            public string Texture;
            public BoundingBoxD Bounds;
            public Vector2 SurfaceSize;
            // The grid's render actor gets late matrix updates, so the view is built from it on the render thread.
            public uint GridActor;
            public MatrixD CameraInGrid;
            public float Fov;
            public float Alpha;
            public uint Character=uint.MaxValue;
            public string[] FirstPersonMaterials;
        }
        private static readonly List<CameraDisplay> displays=new List<CameraDisplay>();
        private static readonly System.Reflection.FieldInfo fov=AccessTools.Field(typeof(MyCameraBlock),"m_fov");
        internal static bool Available => fov!=null && fov.FieldType==typeof(float);
        private static bool? otherPlugin;
        internal static void Add(CameraDisplay display) { if(!displays.Contains(display)) displays.Add(display); }
        internal static void Remove(CameraDisplay display) => displays.Remove(display);

        internal static bool OtherPlugin
        {
            get
            {
                if(otherPlugin.HasValue) return otherPlugin.Value;
                otherPlugin=AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetType("avaness.CameraLCD.CameraTSS",false)!=null);
                if(otherPlugin.Value) Plugin.Logger.Info("Camera LCD plugin loaded; SEVR camera displays are off");
                return otherPlugin.Value;
            }
        }

        // Called on the simulation thread for each render packet.
        internal static View[] Capture()
        {
            if(displays.Count==0 || !Plugin.PatchGroups.CameraDisplays.Enabled) return null;
            var main=MySector.MainCamera;
            if(main==null) return null;
            var character=MySession.Static?.LocalCharacter;
            uint id=character?.Render.GetRenderObjectID() ?? uint.MaxValue;
            var materials=RemoteView.FirstPersonMaterials(character?.Definition);
            double range=Plugin.Common.Config?.LcdCameraRange ?? Config.PluginConfig.DefaultLcdCameraRange;
            List<View> views=null;
            foreach(var display in displays)
            {
                var camera=display.Camera;
                if(camera==null || !display.Visible || !display.Terminal.IsWorking || !camera.IsWorking ||
                    Vector3D.DistanceSquared(main.Position,display.Terminal.WorldMatrix.Translation)>range*range) continue;
                string texture;
                // The panel's render component is null while its block is removed or rebuilt.
                try { texture=display.Panel.GetRenderTextureName(); }
                catch(NullReferenceException) { continue; }
                if(views==null) views=new List<View>();
                views.Add(new View {Texture=texture,Bounds=display.Terminal.PositionComp.WorldAABB,SurfaceSize=display.Panel.SurfaceSize,
                    GridActor=camera.CubeGrid.Render.GetRenderObjectID(),
                    CameraInGrid=MatrixD.Invert(camera.GetViewMatrix())*camera.CubeGrid.PositionComp.WorldMatrixInvScaled,Fov=(float)fov.GetValue(camera),Alpha=display.Panel.BackgroundAlpha/255f,Character=id,FirstPersonMaterials=materials});
            }
            return views?.ToArray();
        }

        // Compares without the string copy CustomName.ToString() makes.
        internal static bool Same(System.Text.StringBuilder text,string value)
        {
            if(text.Length!=value.Length) return false;
            for(int i=0;i<value.Length;i++) if(text[i]!=value[i]) return false;
            return true;
        }
        // A "<surface>:<name>" line for this surface wins; otherwise each line is a candidate name, first match first.
        internal static List<string> CameraNames(string customData,int area)
        {
            string prefix=area+":";
            var lines=new List<string>();
            using(var reader=new StringReader(customData))
                for(string line=reader.ReadLine();line!=null;line=reader.ReadLine())
                {
                    if(line.StartsWith(prefix,StringComparison.Ordinal) && line.Length>prefix.Length) return new List<string> {line.Substring(prefix.Length)};
                    if(!string.IsNullOrWhiteSpace(line)) lines.Add(line);
                }
            return lines;
        }
    }

    [MyTextSurfaceScript(LcdCameras.ScriptId,"Camera Display (VR)")]
    public sealed class CameraDisplay : MyTSSCommon
    {
        internal readonly MyTextPanelComponent Panel;
        internal readonly MyTerminalBlock Terminal;
        private readonly MyTextPanel textPanel;
        internal MyCameraBlock Camera { get; private set; }
        private bool disposed;
        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update100;
        // A rotated text panel keeps the surface of its previous rotation alive.
        internal bool Visible => textPanel==null || textPanel.PanelComponent==Panel;
        public CameraDisplay(Sandbox.ModAPI.Ingame.IMyTextSurface surface,VRage.Game.ModAPI.Ingame.IMyCubeBlock block,Vector2 size) : base(surface,block,size)
        {
            Panel=(MyTextPanelComponent)surface; Terminal=(MyTerminalBlock)block; textPanel=block as MyTextPanel;
            Terminal.CustomDataChanged+=Resolve;
            Terminal.OnMarkForClose+=Closing;
            Resolve(Terminal);
        }
        public override void Run()
        {
            base.Run();
            // Renaming the camera raises no event, and Custom Data edits do not always raise one.
            Resolve(Terminal);
        }
        public override void Dispose()
        {
            base.Dispose();
            disposed=true;
            Terminal.CustomDataChanged-=Resolve;
            Terminal.OnMarkForClose-=Closing;
            Unregister();
        }
        private void Closing(MyEntity entity) => Unregister();
        private void Resolve(MyTerminalBlock block)
        {
            var terminal=string.IsNullOrWhiteSpace(Terminal.CustomData) ? null : Terminal.CubeGrid.GridSystems?.TerminalSystem;
            var names=terminal==null ? null : LcdCameras.CameraNames(Terminal.CustomData,Panel.Area);
            if(Camera!=null && !Camera.Closed && names?.Count>0 && Camera.CubeGrid.GridSystems?.TerminalSystem==terminal && LcdCameras.Same(Camera.CustomName,names[0])) return;
            MyCameraBlock camera=null;
            if(names!=null)
                foreach(string name in names)
                    if((camera=terminal.Blocks.OfType<MyCameraBlock>().FirstOrDefault(b=>LcdCameras.Same(b.CustomName,name)))!=null) break;
            if(camera==Camera) return;
            bool shown=Camera!=null;
            Unregister();
            // Reset disposes this script; the panel creates a new one, which clears the last picture.
            if(camera==null) { if(shown && !disposed) Panel.Reset(); return; }
            Camera=camera;
            Camera.OnClose+=CameraClosed;
            LcdCameras.Add(this);
        }
        private void CameraClosed(MyEntity entity)
        {
            Unregister();
            if(!disposed) Panel.Reset();
        }
        private void Unregister()
        {
            if(Camera!=null) Camera.OnClose-=CameraClosed;
            Camera=null;
            LcdCameras.Remove(this);
        }
    }
}
