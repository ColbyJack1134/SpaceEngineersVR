using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using VRage.Utils;

namespace SpaceEngineersVR.Plugin
{
    // Feature groups in apply order; a group lists its requirements first. Patch classes join a group with
    // [HarmonyPatchCategory(category)]; uncategorized classes are Core, which fails as a whole like before.
    internal static class PatchGroups
    {
        internal static readonly PatchGroup Core=new PatchGroup("Core",null,Patches.FrameInjections.Install);
        internal static readonly PatchGroup SharedExposure=new PatchGroup("Shared exposure","SharedExposure");
        internal static readonly PatchGroup LensMask=new PatchGroup("Lens mask","LensMask");
        internal static readonly PatchGroup StereoFlares=new PatchGroup("Stereo flares","StereoFlares");
        internal static readonly PatchGroup StereoFoliage=new PatchGroup("Stereo foliage","StereoFoliage",h=>Player.StereoFoliage.Warm());
        internal static readonly PatchGroup StereoParticles=new PatchGroup("Stereo particles","StereoParticles");
        internal static readonly PatchGroup StereoScene=new PatchGroup("Stereo scene","StereoScene");
        internal static readonly PatchGroup SharedShadows=new PatchGroup("Shared shadows","SharedShadows");
        internal static readonly PatchGroup Dlss=new PatchGroup("DLSS","Dlss",when:()=>Player.Dlss.NvidiaAdapter);
        internal static readonly PatchGroup StereoShields=new PatchGroup("Stereo shields","StereoShields");
        internal static readonly PatchGroup HandOcclusion=new PatchGroup("Hand occlusion","HandOcclusion");
        internal static readonly PatchGroup BodyFade=new PatchGroup("Body fade","BodyFade");
        internal static readonly PatchGroup CharacterBody=new PatchGroup("Character body","CharacterBody");
        internal static readonly PatchGroup SeatFit=new PatchGroup("Seat fit","SeatFit");
        internal static readonly PatchGroup ScreenEffects=new PatchGroup("Screen effects","ScreenEffects");
        internal static readonly PatchGroup Tracers=new PatchGroup("Tracers","Tracers");
        internal static readonly PatchGroup HudRouting=new PatchGroup("HUD routing","HudRouting",Player.RemoteHud.Install);
        internal static readonly PatchGroup HudMarkers=new PatchGroup("HUD markers","HudMarkers",requires:new[] {HudRouting});
        internal static readonly PatchGroup CameraFeeds=new PatchGroup("Camera feeds","CameraFeeds",requires:new[] {HudRouting});
        internal static readonly PatchGroup CameraDisplays=new PatchGroup("Camera displays","CameraDisplays",requires:new[] {CameraFeeds});
        internal static readonly PatchGroup BlockInspection=new PatchGroup("Block inspection","BlockInspection",Player.NativeBlockInfo.Install,requires:new[] {HudRouting});
        internal static readonly PatchGroup GpuTiming=new PatchGroup("GPU timing","GpuTiming",Patches.PassTimingPatch.Install);
        internal static readonly PatchGroup HandPlacement=new PatchGroup("Hand placement","HandPlacement");
        internal static readonly PatchGroup CreativePlacement=new PatchGroup("Creative placement","CreativePlacement",requires:new[] {HandPlacement});
        internal static readonly PatchGroup BuildHints=new PatchGroup("Build hints","BuildHints");
        internal static readonly PatchGroup GridSelection=new PatchGroup("Grid selection","GridSelection");
        internal static readonly PatchGroup HandInteraction=new PatchGroup("Hand interaction","HandInteraction");
        internal static readonly PatchGroup Spectator=new PatchGroup("Spectator","Spectator");
        internal static readonly PatchGroup ThirdPerson=new PatchGroup("Third person","ThirdPerson");
        internal static readonly PatchGroup Targeting=new PatchGroup("Targeting","Targeting");
        internal static readonly PatchGroup CockpitControls=new PatchGroup("Cockpit controls","CockpitControls");
        internal static readonly PatchGroup CockpitBuilding=new PatchGroup("Cockpit building","CockpitBuilding");
        internal static readonly PatchGroup CockpitMemory=new PatchGroup("Cockpit memory","CockpitMemory");
        internal static readonly PatchGroup HeldTools=new PatchGroup("Held tools","HeldTools",off:()=>Multiplayer.HeldItemPose.Enabled=false);
        internal static readonly PatchGroup WeaponModels=new PatchGroup("Weapon models","WeaponModels");
        internal static readonly PatchGroup VoxelTools=new PatchGroup("Voxel tools","VoxelTools");
        internal static readonly PatchGroup MultiplayerArms=new PatchGroup("Multiplayer arms","MultiplayerArms");
        internal static readonly PatchGroup MenuExtras=new PatchGroup("Menu extras","MenuExtras");
        internal static readonly PatchGroup CursorClip=new PatchGroup("Cursor clip","CursorClip");
        internal static readonly PatchGroup ModHud=new PatchGroup("Mod HUD","ModHud");
        internal static readonly PatchGroup BuildInfo=new PatchGroup("Build Info","BuildInfo",requires:new[] {ModHud,HandPlacement});
        internal static readonly PatchGroup BuildVision=new PatchGroup("Build Vision","BuildVision",requires:new[] {ModHud});
        internal static readonly PatchGroup TouchScreens=new PatchGroup("Touch screens","TouchScreens");
        internal static readonly PatchGroup ArthurLcd=new PatchGroup("Arthur's LCD","ArthurLcd");
        internal static readonly PatchGroup WeaponCore=new PatchGroup("WeaponCore","WeaponCore");
        internal static readonly PatchGroup PaintGun=new PatchGroup("Paint Gun","PaintGun",requires:new[] {VoxelTools,HandPlacement});
        internal static readonly PatchGroup DigiTools=new PatchGroup("Digi tools","DigiTools",requires:new[] {VoxelTools});
        // Its targets' class constructors need the renderer, so it applies from the first Update.
        internal static readonly PatchGroup DoubleClick=new PatchGroup("Double click","DoubleClick",Patches.DoubleClickTolerancePatch.Install);

        internal static readonly PatchGroup[] All={Core,SharedExposure,LensMask,StereoFlares,StereoFoliage,StereoParticles,StereoScene,SharedShadows,StereoShields,
            HandOcclusion,BodyFade,CharacterBody,SeatFit,ScreenEffects,Tracers,HudRouting,HudMarkers,CameraFeeds,CameraDisplays,BlockInspection,GpuTiming,
            HandPlacement,CreativePlacement,BuildHints,GridSelection,HandInteraction,Spectator,ThirdPerson,Targeting,CockpitControls,CockpitBuilding,CockpitMemory,
            HeldTools,WeaponModels,VoxelTools,MultiplayerArms,MenuExtras,CursorClip,ModHud,BuildInfo,BuildVision,TouchScreens,ArthurLcd,WeaponCore,PaintGun,DigiTools};
        private static readonly PatchGroup[] later={DoubleClick,Dlss};
#if SEVR_DIAGNOSTICS
        internal static readonly HashSet<string> Broken=new HashSet<string>();
        internal static Action<string> Sink;
#endif
        private static bool notified;

        internal static int PatchedMethods => All.Concat(later).SelectMany(g=>g.Harmony.GetPatchedMethods()).Distinct().Count();
        internal static IEnumerable<PatchGroup> Off => All.Concat(later).Where(g=>g.Attempted && !g.Enabled && !g.Skipped);

        // Core applies first and throws on failure after taking everything back out; the other groups fail on their own.
        internal static void Apply(Assembly assembly)
        {
            var classes=Classes(assembly);
            foreach(var group in All)
            {
                group.Apply(classes.TryGetValue(group.Category ?? "",out var list) ? list:null);
                if(group==Core && !Core.Enabled) { Remove(); throw new InvalidOperationException("Core patches failed: "+Core.Failure,Core.Error); }
            }
            Log(Summary(),null);
        }

        // Called every update; a group applies once, whether it succeeded or not.
        private static bool laterDone;
        internal static void ApplyLater(Assembly assembly)
        {
            if(laterDone) return;
            laterDone=later.All(g=>g.Attempted);
            if(laterDone) return;
            var classes=Classes(assembly);
            foreach(var group in later)
                if(!group.Attempted && group.Apply(classes.TryGetValue(group.Category,out var list) ? list:null))
                    Log("SEVR patch group on: "+group.Name+"; "+group.Harmony.GetPatchedMethods().Count()+" patched methods",null);
        }

        internal static void Remove()
        {
            laterDone=false;
            foreach(var group in Enumerable.Reverse(All.Concat(later))) group.Remove();
        }

        internal static string Summary()
        {
            var off=Off.Select(g=>g.Name).ToArray();
            return "SEVR patch groups: "+All.Count(g=>g.Enabled)+" on, "+off.Length+" off"+(off.Length>0 ? ": "+string.Join(", ",off):"")+"; "+PatchedMethods+" patched methods";
        }

        // Classes by category; an unknown category is a build error, not a silently skipped patch.
        internal static Dictionary<string,List<Type>> Classes(Assembly assembly)
        {
            var known=new HashSet<string>(All.Concat(later).Select(g=>g.Category ?? ""));
#if SEVR_DIAGNOSTICS
            known.Add(Diagnostics.PatchGroupTests.Category);
#endif
            var result=new Dictionary<string,List<Type>>();
            foreach(var type in AccessTools.GetTypesFromAssembly(assembly))
            {
                var attributes=HarmonyMethodExtensions.GetFromType(type);
                if(attributes.Count==0) continue;
                string category=HarmonyMethod.Merge(attributes).category ?? "";
                if(!known.Contains(category)) throw new InvalidOperationException(type.FullName+" uses the unknown patch category "+category);
                if(!result.TryGetValue(category,out var list)) result[category]=list=new List<Type>();
                list.Add(type);
            }
            return result;
        }

        // Once per game session, when a HUD exists to show it.
        internal static void NotifyPlayer()
        {
            if(notified) return;
            notified=true;
            var off=Off.Select(g=>g.Name).ToArray();
            if(off.Length>0) Sandbox.ModAPI.MyAPIGateway.Utilities?.ShowNotification("VR: a game hook failed, off: "+string.Join(", ",off)+" (see the log)",10000,"Red");
        }

        internal static void Log(string line,Exception error)
        {
            if(error!=null) Logger.Warning(error,line); else Logger.Info(line);
            var game=MyLog.Default;
            if(game!=null)
            {
                game.WriteLine(line);
                if(error!=null) game.WriteLine(error.ToString());
            }
#if SEVR_DIAGNOSTICS
            Sink?.Invoke(line);
#endif
        }
    }
}
