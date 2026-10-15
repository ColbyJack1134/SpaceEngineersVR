using System;
using SpaceEngineersVR.Plugin;
using VRage.Game.Entity;

namespace SpaceEngineersVR.Player
{
    // WeaponCore warnings that its (hidden in VR) HUD would show: projectiles homing on the ship and drones inbound.
    // Surfaced as a notice and a strong pulse on both controllers when they start, a light pulse every 1.5 s while they
    // last, and a line on the wrist Weapons page. Independent of the visor, since these are warnings.
    internal static class WeaponCoreAlerts
    {
        internal static int Incoming { get; private set; }
        internal static bool Drones { get; private set; }
        private static DateTime next,nextPulse;

        internal static string Text => Incoming>0 && Drones ? "INCOMING  "+Incoming+"  ·  DRONES" :
            Incoming>0 ? "INCOMING  "+Incoming+(Incoming==1 ? " projectile":" projectiles") : Drones ? "DRONES INBOUND" : null;

        internal static void Update(MyEntity grid)
        {
            var now=DateTime.UtcNow;
            if(now<next) return;
            next=now.AddSeconds(.25);
            int incoming=0; bool drones=false;
            try
            {
                if(grid!=null)
                {
                    incoming=WeaponCoreBridge.IncomingProjectiles(grid);
                    drones=WeaponCoreInternals.Available && WeaponCoreInternals.DroneAlert;
                }
            }
            catch(Exception ex) { Logger.Warning(ex,"WeaponCore alert read failed"); next=now.AddSeconds(5); }

            bool started=incoming>Incoming || drones && !Drones;
            Incoming=incoming; Drones=drones;
            if(Incoming==0 && !Drones) return;
            if(started)
            {
                EssentialHud.Notify(Text);
                Pulse(.6f,.12f); nextPulse=now.AddSeconds(1.5);
            }
            else if(now>=nextPulse) { Pulse(.25f,.05f); nextPulse=now.AddSeconds(1.5); }
        }

        private static void Pulse(float amplitude,float duration)
        {
            if(Player.HandL.pose.isTracked) Player.HandL.Vibrate(0,duration,160,amplitude);
            if(Player.HandR.pose.isTracked) Player.HandR.Vibrate(0,duration,160,amplitude);
        }

        internal static void Reset() { Incoming=0; Drones=false; next=nextPulse=DateTime.MinValue; }
    }
}
