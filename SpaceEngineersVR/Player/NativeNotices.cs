using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Sandbox.Game.Gui;

namespace SpaceEngineersVR.Player
{
    internal enum NoticeStyle { Normal,Warning,Title }
    internal readonly struct Notice : IEquatable<Notice>
    {
        public readonly string Text;
        public readonly NoticeStyle Style;
        public readonly float Alpha;
        public Notice(string text,NoticeStyle style,float alpha=1) { Text=text; Style=style; Alpha=alpha; }
        public bool Equals(Notice other) => Text==other.Text && Style==other.Style && Alpha==other.Alpha;
        public override bool Equals(object other) => other is Notice notice && Equals(notice);
        public override int GetHashCode() => Text?.GetHashCode() ?? 0;
    }
    // Only unseen text takes over the wrist. New text extends a takeover up to a limit, then a short break keeps a stream of notices from hiding the HUD.
    internal sealed class NoticeTakeover
    {
        internal const double Seconds=3,Limit=6,Break=2;
        private DateTime start,until,allowed;
        private bool pending;
        public bool Update(DateTime now,bool fresh,bool allowedNow)
        {
            pending=allowedNow && (pending || fresh);
            if(pending && now>=allowed)
            {
                if(now>=until) start=now;
                var limit=start.AddSeconds(Limit);
                until=now.AddSeconds(Seconds)<limit ? now.AddSeconds(Seconds):limit;
                if(until==limit) allowed=limit.AddSeconds(Break);
                pending=false;
            }
            return allowedNow && now<until;
        }
        public void Reset() { start=until=allowed=DateTime.MinValue; pending=false; }
    }
    // Native center notifications and survival buff popups, which the headset otherwise never shows.
    internal static class NativeNotices
    {
        internal const int MaxLines=4;
        private static readonly System.Reflection.FieldInfo groups=AccessTools.Field(typeof(MyHudNotifications),"m_notificationsByPriority"),
            sync=AccessTools.Field(typeof(MyHudNotifications),"m_lockObject"),drawData=AccessTools.Field(AccessTools.Inner(typeof(MyHudNotifications),"NotificationDrawData"),"Notification");
        private static string buffKey;
        private static DateTime buffShown;

        internal static bool Available => groups!=null && sync!=null && drawData!=null;

        public static List<Notice> Read()
        {
            var notices=new List<Notice>();
            ReadBuff(notices);
            var notifications=MyHud.Notifications;
            if(notifications!=null && Available && groups.GetValue(notifications) is IDictionary byPriority)
                lock(sync.GetValue(notifications))
                    for(int priority=MyHudNotifications.MAX_PRIORITY;priority>=0 && notices.Count<MaxLines;priority--)
                    {
                        if(!(byPriority[priority] is IList group)) continue;
                        foreach(var entry in group)
                        {
                            // Control hints name desktop keys and buttons that do not apply in VR.
                            if(!(drawData.GetValue(entry) is MyHudNotificationBase notice) || !notice.Alive || notice.IsControlsHint) continue;
                            string message=Clean(notice.GetText());
                            if(string.IsNullOrEmpty(message) || notices.Any(n=>n.Text==message)) continue;
                            notices.Add(new Notice(message,notice.Font=="Red" ? NoticeStyle.Warning:NoticeStyle.Normal));
                            if(notices.Count==MaxLines) break;
                        }
                    }
            return notices;
        }
        private static void ReadBuff(List<Notice> notices)
        {
            var info=MyHud.BuffInfo;
            var details=info.Visible ? info.GetQuestGetails().Where(d=>!d.IsObjective).Select(d=>Clean(d.Data)).Where(d=>!string.IsNullOrEmpty(d)).ToArray() : new string[0];
            string key=info.Visible ? info.QuestTitle+"\n"+string.Join("\n",details) : null;
            if(key!=buffKey) { buffKey=key; buffShown=DateTime.UtcNow; }
            float limit=info.FramesVisibleLimit<float.MaxValue ? info.FramesVisibleLimit/60 : 7;
            if(key==null || (DateTime.UtcNow-buffShown).TotalSeconds>limit) return;
            notices.Add(new Notice(Clean(info.QuestTitle),NoticeStyle.Title));
            foreach(string detail in details.Take(1)) notices.Add(new Notice(detail,NoticeStyle.Normal));
        }
        // Native notifications mark highlighted spans with square brackets.
        private static string Clean(string value) => value?.Replace("[","").Replace("]","").Replace("\r","").Replace("\n"," ").Trim();
        public static void Reset() => buffKey=null;
    }
}
