using System.Collections.Generic;

namespace SpaceEngineersVR.Player
{
    internal sealed partial class CockpitAssignmentLayout
    {
        private static void AddRemaining(Dictionary<string,CockpitAssignmentLayout> result)
        {
            Add(result,"LargeBlockSuspendedControlSeat",
                new Group("Gauge buttons",9,10),
                new Group("Right button column",6,7,8),
                new Group("Lower button row",0,1,2,3,4,5),
                new Group("Right analog handle",11));
            Add(result,"LargeBlockSuspendedControlSeatB",
                new Group("Gauge buttons",9,10),
                new Group("Right button column",6,7,8),
                new Group("Lower button row",0,1,2,3,4,5),
                new Group("Right analog handle",11));
            Add(result,"SmallBlockSuspendedControlSeat",
                new Group("Gauge buttons",9,10),
                new Group("Right button column",6,7,8),
                new Group("Lower button row",0,1,2,3,4,5),
                new Group("Right analog handle",11));
            Add(result,"SmallBlockSuspendedControlSeatB",
                new Group("Gauge buttons",9,10),
                new Group("Right button column",6,7,8),
                new Group("Lower button row",0,1,2,3,4,5),
                new Group("Right analog handle",11));
            Add(result,"SmallBlockCapCockpit",
                new Group("Overhead button",0),
                new Group("Left switch row",4,3,2,1),
                new Group("Overhead upper switches",13,7,9,11,12),
                new Group("Overhead lower switches",5,6,8,10,14),
                new Group("Center analog handle",15));
            Add(result,"SmallBlockFlushCockpit",
                new Group("Left dash button",6),
                new Group("Right dash button",7),
                new Group("Right button row",0,1,2,3,4,5),
                new Group("Center switch row",8,9,10,11,12,13,14),
                new Group("Right switch row",15,16,17),
                new Group("Center analog handle",18));
            Add(result,"SpeederCockpit",
                new Group("Right button",0),
                new Group("Right analog slider",1));
            Add(result,"SpeederCockpitCompact",
                new Group("Right button",0),
                new Group("Right analog slider",1));
            Add(result,"LargeBlockOpenSlopedCockpit",
                new Group("Dash button column",0,1),
                new Group("Right button",2),
                new Group("Left switch row",6,5,4,3),
                new Group("Dash switch row",17,18,19,20,21,22),
                new Group("Right bank left switches",7,12,13,8,9),
                new Group("Right bank right switches",14,10,15,16,11),
                new Group("Left analog handle",23),
                new Group("Right analog handles",25,24));
            Add(result,"LargeBlockClosedSlopedCockpit",
                new Group("Dash button column",0,1),
                new Group("Right button",2),
                new Group("Left switch row",6,5,4,3),
                new Group("Dash switch row",17,18,19,20,21,22),
                new Group("Right bank left switches",7,12,13,8,9),
                new Group("Right bank right switches",14,10,15,16,11),
                new Group("Left analog handle",23),
                new Group("Right analog handles",25,24));
            Add(result,"SmallBlockOpenSlopedCockpit",
                new Group("Dash button column",0,1),
                new Group("Right button",2),
                new Group("Dash switch row",3,4,5,6,7),
                new Group("Left analog handle",8),
                new Group("Right analog handles",10,9));
            Add(result,"SmallBlockClosedSlopedCockpit",
                new Group("Dash button column",0,1),
                new Group("Right button",2),
                new Group("Dash switch row",3,4,5,6,7),
                new Group("Left analog handle",8),
                new Group("Right analog handles",10,9));
            Add(result,"BuggyCockpit");
            Add(result,"SmallBlockStandingCockpit",
                new Group("Left upper buttons",1,2),
                new Group("Left lower buttons",3,4,5,6),
                new Group("Right red button",0),
                new Group("Right switches",7,9,11,8,10,12),
                new Group("Pinch sliders",14,15,16,17),
                new Group("Right analog handle",13));
            Add(result,"LargeBlockStandingCockpit",
                new Group("Left upper buttons",1,2),
                new Group("Left lower buttons",3,4,5,6),
                new Group("Right red button",0),
                new Group("Right switches",7,9,11,8,10,12),
                new Group("Pinch sliders",14,15,16,17),
                new Group("Right analog handle",13));
            Add(result,"DBSmallBlockFighterCockpit",
                new Group("Left outer switches",12,1,11,10,0,9),
                new Group("Left inner switches",7,3,6,2,5,4),
                new Group("Left striped switch",8),
                new Group("Left rear switch row",16,15,14,13),
                new Group("Dash left switch row",21,22,23,24,25,26),
                new Group("Dash right switch row",27,28,29,30,31,32),
                new Group("Right front switch row",33,34,35,36),
                new Group("Right rearward switches",37,38,39,40),
                new Group("Right rear switch row",17,18,19,20),
                new Group("Striped pull bar",41));
        }
    }
}
