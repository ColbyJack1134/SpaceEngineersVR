using System;
using Sandbox.Game.Screens.Helpers;
using VRage.Game;

namespace SpaceEngineersVR.Player
{
    // Only the editor uses padded group pages; saved and network slots retain their control IDs.
    internal sealed class CockpitAssignmentToolbar : IDisposable
    {
        internal readonly MyToolbar Toolbar;
        internal readonly CockpitAssignmentLayout Layout;
        private readonly MyToolbar controls;
        internal CockpitAssignmentToolbar(MyToolbar controls,CockpitAssignmentLayout layout)
        {
            this.controls=controls; Layout=layout;
            Toolbar=new MyToolbar(MyToolbarType.ButtonPanel,CockpitAssignmentLayout.SlotsPerPage,layout.Pages.Length);
            Toolbar.CanPlayerActivateItems=false;
            if(controls.Owner!=null)
            {
                Toolbar.Init(null,controls.Owner);
                Multiplayer.MultiplayerRuntime.Detach(Toolbar);
            }
            for(int control=0;control<layout.Pages.Length*CockpitAssignmentLayout.SlotsPerPage;control++)
            {
                int slot=layout.Control(control);
                if(slot>=0) Toolbar.SetItemAtIndex(control,Copy(controls.GetItemAtIndex(slot)));
            }
            Toolbar.ItemChanged+=Changed;
        }
        internal static MyToolbarItem Copy(MyToolbarItem item)
        {
            var data=item?.GetObjectBuilder();
            return data==null ? null:MyToolbarItemFactory.CreateToolbarItem(data);
        }
        private void Changed(MyToolbar source,MyToolbar.IndexArgs args,bool gamepad)
        {
            if(gamepad) return;
            int control=Layout.Control(args.ItemIndex);
            if(control>=0) controls.SetItemAtIndex(control,Copy(source.GetItemAtIndex(args.ItemIndex)));
            else if(source.GetItemAtIndex(args.ItemIndex)!=null) source.SetItemAtIndex(args.ItemIndex,null);
        }
        public void Dispose() { Toolbar.ItemChanged-=Changed; Toolbar.Clear(); }
    }
}
