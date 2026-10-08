using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SpaceEngineersVR.Player;
using VRage.FileSystem;
using VRage.Render.Scene;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class CockpitVisibilityTests
    {
        internal static void RunNative(Action<string> log)
        {
            var source=(CockpitRender.Verification)AccessTools.Field(typeof(CockpitRender),"verification").GetValue(null);
            var ids=source.Actors.Where(id=>id!=uint.MaxValue).ToArray();
            if(ids.Length<2) throw new InvalidOperationException("Cockpit visibility fixture needs native replacement actors");
            var exterior=MyIDTracker<MyActor>.FindByID(ids[0]);
            bool savedExterior=exterior.IsVisible;
            var check=new CockpitRender.Verification(source.Interior,CockpitRig.Find(CockpitLayout.Fighter).Geometry(MyFileSystem.ContentPath),Array.Empty<string>()) {
                Exterior=ids[0],Actors=ids.Skip(1).ToArray() };
            var actors=new[] {check.Interior}.Concat(check.Actors).Select(id=>MyIDTracker<MyActor>.FindByID(id)).ToArray();
            var saved=actors.Select(actor=>actor.IsVisible).ToArray();
            var restore=new List<Action>();
            try
            {
                exterior.SetVisibility(false); exterior.UpdateBeforeDraw();
                try
                {
                    CockpitRender.RemoteVisibility(check,restore);
                    if(!exterior.IsVisible || actors.Any(actor=>actor.IsVisible)) throw new InvalidOperationException("Camera feed left native interior or replacement cockpit visible");
                    var nested=new List<Action>();
                    CockpitRender.RemoteVisibility(check,nested);
                    if(nested.Count!=0) throw new InvalidOperationException("Nested camera visibility changed an already isolated scene");
                }
                finally
                {
                    for(int i=restore.Count-1;i>=0;i--) restore[i]();
                }
                if(exterior.IsVisible || actors.Where((actor,i)=>actor.IsVisible!=saved[i]).Any()) throw new InvalidOperationException("Camera feed failed to restore physical cockpit visibility");
            }
            finally { exterior.SetVisibility(savedExterior); exterior.UpdateBeforeDraw(); }
            log("PASS native cockpit camera visibility: exterior-only feed, hidden interior/replacements, nested isolation and finally restoration of physical cockpit actors.");
        }
    }
}
