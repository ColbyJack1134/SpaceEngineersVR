using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using SpaceEngineersVR.Diagnostics;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 1 || !Directory.Exists(args[0]))
        {
            Console.Error.WriteLine("Usage: SEVR.Diagnostics.exe <SpaceEngineers/Bin64> [--vr]");
            return 1;
        }
        string game = Path.GetFullPath(args[0]);
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
        {
            string path = Path.Combine(game, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try
        {
            if(args.Length==3 && args[1]=="--stereo-smoke-test") { StereoSmokeTests.Export(game,args[2],Console.WriteLine); return 0; }
            if(args.Length==3 && args[1]=="--weapon-test") { WeaponTests.Export(game,args[2],Console.WriteLine); return 0; }
            if(args.Length==3 && args[1]=="--ammo-test") { WeaponTests.Ammo(game,args[2],Console.WriteLine); return 0; }
            if(args.Length==2 && args[1]=="--lcd-input-test") { LcdInputTests.Run(Console.WriteLine); return 0; }
            if(args.Length==2 && args[1]=="--keyboard-test") { KeyboardInputTests.Run(Console.WriteLine); return 0; }
            if(args.Length==4 && args[1]=="--stick-preview") { StickPreview.Export(game,args[2],args[3]); return 0; }
            if(args.Length==4 && args[1]=="--export-model") { ModelInspection.Export(game,args[2],args[3]); return 0; }
            if(args.Length==3 && args[1]=="--companion-test") { MultiplayerTests.Companion(args[2],Console.WriteLine); return 0; }
            if(args.Length==3 && args[1]=="--multiplayer-test") { MultiplayerTests.Export(game,args[2],Console.WriteLine); return 0; }
            if(args.Length==3 && args[1]=="--navigation-preview") { UiTests.Navigation(game,args[2],Console.WriteLine); return 0; }
            if(args.Length==3 && args[1]=="--wrist-menus") { UiTests.WristMenus(game,args[2],Console.WriteLine); return 0; }
            if(args.Length==3 && args[1]=="--planner-preview") { UiTests.BuildPlanner(game,args[2],Console.WriteLine); return 0; }
            if(args.Length==3 && args[1]=="--signals-test") { UiTests.Signals(game,args[2],Console.WriteLine); return 0; }
            if(args.Length==3 && args[1]=="--tablet-test") { UiTests.Tablet(game,args[2],Console.WriteLine); return 0; }
            if(args.Length==3 && args[1]=="--control-seat-preview") { ControlSeatPreview.Export(game,args[2],Console.WriteLine); return 0; }
            if(args.Length==3 && args[1]=="--compare-physical-rigs") { PhysicalImageComparison.VerifyRigs(args[2],Console.WriteLine); return 0; }
            if((args.Length==2 || args.Length==3) && args[1]=="--benchmark-geometry") { GeometryBenchmark.Run(game,Console.WriteLine,args.Length==3 ? args[2] : null); return 0; }
            return Run(game, Array.IndexOf(args, "--vr") >= 0, Array.IndexOf(args,"--self-test") >= 0, Array.IndexOf(args,"--ui-test") >= 0);
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string game, bool vr, bool selfTest, bool uiTest)
    {
        Console.WriteLine("SEVR preflight " + DateTime.UtcNow.ToString("O"));
        Console.WriteLine("Game: " + game);
        Assembly.LoadFrom(Path.Combine(game, "VRage.Render11.dll"));
        Assembly.LoadFrom(Path.Combine(game, "Sandbox.Game.dll"));
        if (selfTest && !uiTest)
        {
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SEVRPrototype","Reports","regression-data");
            UiTests.Initialize(game,data);
        }
        if (uiTest) UiTests.Run(game,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SEVRPrototype","Reports","ui-preview"),Console.WriteLine);
        if (selfTest) RegressionTests.Run(Console.WriteLine);
        bool compatible = CompatibilityProbe.Run(Console.WriteLine);
        bool vrReady = !vr || VrProbe.Run(Console.WriteLine);
        Console.WriteLine("Metadata checks do not validate rendering or in-game interaction.");
        return !compatible ? 2 : (vrReady ? 0 : 3);
    }
}
