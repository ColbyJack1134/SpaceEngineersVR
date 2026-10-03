using System;
using System.Linq;

namespace SpaceEngineersVR.Config
{
    public sealed class HudProfile
    {
        public bool Vitals { get; set; }=true;
        public int Markers { get; set; }=1;
        public float IconScale { get; set; }=1.5f;
        public float TextScale { get; set; }=1;
        public bool Group { get; set; }=true;
        internal HudProfile Copy() => (HudProfile)MemberwiseClone();
        internal string Name => Markers==2 ? "Details":Markers==1 ? "Markers":Vitals ? "Vitals":"Off";
    }

    public partial class PluginConfig
    {
        private HudProfile[] hudProfiles=new HudProfile[0];
        private int hudProfileIndex;
        private bool hudProfilesReady,applyingHudProfile;
        private float signalIconScale=1.5f,signalTextScale=1,wristSignalTint=.15f;
        public float SignalIconScale { get=>signalIconScale; set=>SetValue(ref signalIconScale,Bound(value,.75f,2.5f,1.5f)); }
        public float SignalTextScale { get=>signalTextScale; set=>SetValue(ref signalTextScale,Bound(value,.75f,1.5f,1)); }
        public float WristSignalTint { get=>wristSignalTint; set=>SetValue(ref wristSignalTint,Bound(value,0,.75f,.15f)); }
        public HudProfile[] HudProfiles
        {
            get=>hudProfiles;
            set { hudProfiles=value?.Length>=4 ? value.Take(5).Select(NormalizeHudProfile).ToArray():new HudProfile[0]; }
        }
        public int HudProfileIndex
        {
            get=>hudProfileIndex;
            set { if(hudProfilesReady) SelectHudProfile(value); else hudProfileIndex=Math.Max(0,Math.Min(4,value)); }
        }
        private static HudProfile NormalizeHudProfile(HudProfile value)
        {
            var p=(value ?? new HudProfile()).Copy();
            p.Markers=Math.Max(0,Math.Min(2,p.Markers));
            p.IconScale=Bound(p.IconScale,.75f,2.5f,1.5f); p.TextScale=Bound(p.TextScale,.75f,1.5f,1);
            return p;
        }
        private HudProfile CaptureHudProfile() => new HudProfile { Vitals=ShowVitals,Markers=WaypointMode,
            IconScale=SignalIconScale,TextScale=SignalTextScale,Group=GroupSignals };
        internal void InitializeHudProfiles()
        {
            if(hudProfilesReady) return;
            if(hudProfiles.Length<4)
            {
                var current=CaptureHudProfile();
                hudProfileIndex=current.Markers>0 ? current.Markers+1:current.Vitals ? 1:0;
                hudProfiles=Enumerable.Range(0,4).Select(i=> {var p=current.Copy(); p.Vitals=i>0; p.Markers=Math.Max(0,i-1); return p;}).ToArray();
                hudProfiles[hudProfileIndex]=current;
            }
            hudProfilesReady=true;
            SelectHudProfile(hudProfileIndex);
        }
        private void CaptureActiveHudProfile(string property)
        {
            if(!hudProfilesReady || applyingHudProfile) return;
            switch(property)
            {
                case nameof(ShowVitals): case nameof(WaypointMode): case nameof(GroupSignals):
                case nameof(SignalIconScale): case nameof(SignalTextScale):
                    hudProfiles[hudProfileIndex]=CaptureHudProfile(); break;
            }
        }
        internal void SelectHudProfile(int index)
        {
            if(!hudProfilesReady) InitializeHudProfiles();
            hudProfileIndex=Math.Max(0,Math.Min(hudProfiles.Length-1,index));
            var p=hudProfiles[hudProfileIndex];
            // Notify persistence only after all fields have switched.
            applyingHudProfile=true;
            showVitals=p.Vitals; waypointMode=p.Markers; groupSignals=p.Group;
            signalIconScale=p.IconScale; signalTextScale=p.TextScale;
            applyingHudProfile=false;
            OnPropertyChanged(nameof(HudProfiles));
        }
        internal void EditHudProfile(int index,Action<HudProfile> edit)
        {
            InitializeHudProfiles();
            if(index<0 || index>=hudProfiles.Length) return;
            var p=hudProfiles[index].Copy(); edit(p); hudProfiles[index]=NormalizeHudProfile(p);
            if(index==hudProfileIndex) SelectHudProfile(index); else OnPropertyChanged(nameof(HudProfiles));
        }
        internal void AddHudProfile()
        {
            InitializeHudProfiles();
            if(hudProfiles.Length==5) return;
            hudProfiles=hudProfiles.Concat(new[] {new HudProfile {Vitals=false}}).ToArray();
            OnPropertyChanged(nameof(HudProfiles));
        }
        internal void RemoveExtraHudProfile()
        {
            InitializeHudProfiles();
            if(hudProfiles.Length!=5) return;
            hudProfiles=hudProfiles.Take(4).ToArray(); SelectHudProfile(Math.Min(hudProfileIndex,3));
        }
        internal void ResetHudProfile(int index)
        {
            EditHudProfile(index,p=> {var d=new HudProfile {Vitals=index>0 && index<4,Markers=index==4 ? 1:Math.Max(0,index-1)};
                p.Vitals=d.Vitals; p.Markers=d.Markers; p.IconScale=d.IconScale; p.TextScale=d.TextScale;
                p.Group=true; });
        }
    }
}
