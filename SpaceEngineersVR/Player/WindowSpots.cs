using System;
using System.Linq;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Saved window spots: one per screen key (all native menus share "Menu"), in the play space on foot or relative to the seat per cockpit model.
    internal static class WindowSpots
    {
        internal const float KeepAngle=70,KeepDistance=3;

        internal static bool Find(string screen,out Matrix local,out MenuWindowSetting saved)
        {
            local=Matrix.Identity;
            saved=Common.Config.MenuWindows?.FirstOrDefault(s=>s.Screen==screen && s.World==null && s.Cockpit==0);
            if(saved==null) return false;
            var p=new Vector3(saved.X,saved.Y,saved.Z); var q=new Quaternion(saved.QX,saved.QY,saved.QZ,saved.QW);
            if(!p.IsValid() || p.Length()>10 || !q.LengthSquared().IsValid() || q.LengthSquared()<.9f || q.LengthSquared()>1.1f) return false;
            q.Normalize(); local=Matrix.CreateFromQuaternion(q); local.Translation=p;
            return true;
        }

        internal static void Store(string screen,Matrix local,float width,float height)
        {
            var p=local.Translation; var q=Quaternion.CreateFromRotationMatrix(local);
            var entry=new MenuWindowSetting {Screen=screen,Width=width,Height=height,X=p.X,Y=p.Y,Z=p.Z,QX=q.X,QY=q.Y,QZ=q.Z,QW=q.W};
            Common.Config.MenuWindows=(Common.Config.MenuWindows ?? new MenuWindowSetting[0]).Where(s=>s.Screen!=screen && Current(s)).Concat(new[] {entry}).ToArray();
        }

        // Older head-relative menu spots and per-entity spots no longer apply and are dropped on the next save.
        internal static bool Current(MenuWindowSetting s) => s.Screen=="CameraSignals" ||
            s.Screen!=null && s.World==null && s.Cockpit==0 && (s.Screen=="Menu" || s.Screen=="Mirror" || s.Screen.StartsWith("Menu/") || s.Screen.StartsWith("Mirror/") || s.Screen.StartsWith("Remote/"));

        // Roomscale: a window left behind the player or several meters away reopens in front instead.
        internal static bool InView(Matrix pose,Matrix head)
        {
            var offset=pose.Translation-head.Translation; offset.Y=0;
            if(offset.Length()>KeepDistance) return false;
            var forward=head.Forward; forward.Y=0;
            if(offset.LengthSquared()<1e-4f || forward.LengthSquared()<1e-4f) return true;
            return Vector3.Dot(Vector3.Normalize(offset),Vector3.Normalize(forward))>=Math.Cos(MathHelper.ToRadians(KeepAngle));
        }

        internal static bool Keep(Matrix pose,Matrix head) => Common.Config.KeepScreenPlacement || InView(pose,head);

        // Open windows are posed in raw tracking space; in a seat, a recenter moves the cockpit there, so they move with it.
        internal static Matrix Follow(Matrix pose,Matrix oldOrigin,Matrix newOrigin) => pose*Matrix.Invert(oldOrigin)*newOrigin;
        internal static void FollowRecenter(Matrix oldOrigin,Matrix newOrigin)
        {
            FloatingMenu.Follow(oldOrigin,newOrigin); RemoteView.Follow(oldOrigin,newOrigin); DesktopWindow.Follow(oldOrigin,newOrigin);
        }

        // Every saved spot returns to its default and open windows move now; camera signal modes are kept.
        internal static void ResetAll()
        {
            Common.Config.MenuWindows=(Common.Config.MenuWindows ?? new MenuWindowSetting[0]).Where(s=>s.Screen=="CameraSignals").ToArray();
            FloatingMenu.Recenter(); RemoteView.Recenter(); DesktopWindow.ResetPlacement();
        }
    }
}
