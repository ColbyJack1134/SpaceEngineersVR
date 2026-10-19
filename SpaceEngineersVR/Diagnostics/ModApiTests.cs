#if SEVR_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Sandbox.ModAPI;
using SpaceEngineersVR.Plugin;
using VRage;
using VRage.Scripting;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class ModApiTests
    {
        public static void Run(Action<string> log)
        {
            string client=Resource("SevrApi.cs"),guide=Resource("MOD-API.md");
            var example=Regex.Match(guide,"```csharp\\r?\\n(.*?)```",RegexOptions.Singleline);
            if(!example.Success) throw new Exception("Mod API guide has no C# example");
            var type=Compile(client,example.Groups[1].Value).GetType("SpaceEngineersVR.Api.SevrApi",true);
            bool rejected=false;
            try { Compile("public class Probe { public string Read() { return System.IO.File.ReadAllText(\"x\"); } }"); }
            catch(Exception ex) { rejected=ex.Message.Contains("is prohibited"); }
            if(!rejected) throw new Exception("Mod compiler accepted a call outside the whitelist; the client check proves nothing");
            log("PASS mod API client and guide example compile under the game's mod whitelist");

            if(MyAPIGateway.Utilities==null) MyAPIGateway.Utilities=MyAPIUtilities.Static;
            try
            {
                object early=Activator.CreateInstance(type),late=Activator.CreateInstance(type);
                Call(early,"Load");
                if(Get<bool>(early,"Loaded")) throw new Exception("Mod API client loaded before the plugin offered it");
                ModApi.Listen();
                if(!Get<bool>(early,"Loaded") || Get<int>(early,"Version")!=ModApi.Version) throw new Exception("Mod API offer did not reach a mod loaded first");
                Call(late,"Load");
                if(!Get<bool>(late,"Loaded")) throw new Exception("Mod API request from a mod loaded later was not answered");
                if(Get<bool>(late,"IsActive")) throw new Exception("Mod API reported VR active without a world");

                var presses=new List<string>();
                type.GetEvent("Use").AddEventHandler(early,new Action<bool,LineD>((main,ray) => presses.Add("early "+main+" "+ray.To.X)));
                type.GetEvent("Use").AddEventHandler(late,new Action<bool,LineD>((main,ray) => presses.Add("late "+main+" "+ray.To.X)));
                MyAPIGateway.Utilities.SendModMessage(ModApi.Channel,ModApi.Request);
                ModApi.Notify(true,new LineD(Vector3D.Zero,new Vector3D(2,0,0)));
                if(!presses.SequenceEqual(new[] {"early True 2","late True 2"})) throw new Exception("Mod API Use reached: "+string.Join(", ",presses));
                presses.Clear();
                Call(late,"Unload");
                ModApi.Notify(false,new LineD(Vector3D.Zero,Vector3D.One));
                if(!presses.SequenceEqual(new[] {"early False 1"})) throw new Exception("Mod API Use after unload reached: "+string.Join(", ",presses));
                Call(early,"Unload");
            }
            finally { ModApi.Reset(); }
            log("PASS mod API handshake in both load orders, Use delivered once per mod after a resend, unload");
        }

        internal static string Resource(string name)
        {
            using(var stream=Assembly.GetEntryAssembly().GetManifestResourceStream(name))
            {
                if(stream==null) throw new Exception("Missing embedded "+name);
                using(var reader=new StreamReader(stream)) return reader.ReadToEnd();
            }
        }

        private static bool initialized;
        // The same references and whitelists the game uses for Workshop mods and programmable blocks (MySpaceGameDefaultIlCompiler, MySpaceGameDefaultIlChecker).
        internal static IVRageScripting Scripting()
        {
            var scripting=MyVRage.Platform.Scripting;
            if(!initialized)
            {
                string bin=Path.GetDirectoryName(typeof(MyAPIGateway).Assembly.Location);
                var references=new[] {"Sandbox.Game","Sandbox.Common","Sandbox.Graphics","VRage","VRage.Library","VRage.Math","VRage.Game","VRage.Render","VRage.Input",
                    "SpaceEngineers.ObjectBuilders","SpaceEngineers.Game","System.Collections.Immutable","ProtoBuf.Net.Core"}.Select(name => Path.Combine(bin,name+".dll")).ToList();
                references.Add(Assembly.Load("netstandard").Location);
                references.AddRange(AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name=="System.Runtime" || a.GetName().Name=="System.Collections").Select(a => a.Location));
                // The game's implicit programmable block namespaces come from these types.
                var namespaces=new[] {typeof(MyTuple),typeof(Vector2),typeof(VRage.Game.Game),typeof(Sandbox.ModAPI.Interfaces.ITerminalAction),
                    typeof(Sandbox.ModAPI.Ingame.IMyGridTerminalSystem),typeof(Sandbox.Game.EntityComponents.MyModelComponent),typeof(VRage.Game.Components.IMyComponentAggregate),
                    typeof(VRage.Collections.ListReader<>),typeof(VRage.Game.ObjectBuilders.Definitions.MyObjectBuilder_FactionDefinition),typeof(VRage.Game.ModAPI.Ingame.IMyCubeBlock),
                    typeof(VRage.Game.ModAPI.Ingame.Utilities.MyIni),Assembly.LoadFrom(Path.Combine(bin,"System.Collections.Immutable.dll")).GetType("System.Collections.Immutable.ImmutableArray",true),typeof(SpaceEngineers.Game.ModAPI.Ingame.IMyAirVent),
                    typeof(VRage.Game.GUI.TextPanel.MySprite)};
                scripting.Initialize(Thread.CurrentThread,references,namespaces,new[] {"STABLE"},null,false);
                new SpaceEngineers.Game.MySpaceGameDefaultIlChecker().InitIlChecker();
                initialized=true;
            }
            return scripting;
        }
        private static Assembly Compile(params string[] sources)
        {
            var scripts=sources.Select((code,i) => new Script("Source"+i+".cs",code)).ToArray();
            var assembly=Scripting().CompileAsync(MyApiTarget.Mod,"SEVR.ModApiClientTest",scripts,out var messages,"SEVR mod API client").Result;
            var errors=messages.Where(m => m.IsError).ToArray();
            if(errors.Length>0 || assembly==null) throw new Exception("Mod compiler errors: "+string.Join(" | ",errors.Select(m => m.Text)));
            return assembly;
        }

        private static void Call(object target,string method) => target.GetType().GetMethod(method).Invoke(target,null);
        private static T Get<T>(object target,string property) => (T)target.GetType().GetProperty(property).GetValue(target);
    }
}
#endif
