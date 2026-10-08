using System.Collections.Generic;

namespace SpaceEngineersVR.Player
{
    internal sealed partial class CockpitAssignmentLayout
    {
        private static Dictionary<string,CockpitAssignmentLayout> Create()
        {
            var result=new Dictionary<string,CockpitAssignmentLayout>();
            Add(result,"LargeBlockModularBridgeCockpit",
                new Group("Right handles",0));
            Add(result,"LargeBlockCockpit");
            Add(result,"LargeBlockCockpitSeat",
                new Group("Left outer cap buttons",0),
                new Group("Left outer pair buttons",2,1),
                new Group("Right cap buttons",8,5,4,7,6,3),
                new Group("Left dashboard buttons",19,18,17,16,15,14,13),
                new Group("Right dashboard buttons",22,23,20,21,24,25),
                new Group("Left console outer buttons",12,11,10,9),
                new Group("Left console inner buttons",26,27,28),
                new Group("Right console inner buttons",29,30,31),
                new Group("Left screen outer upper buttons",46,47,48),
                new Group("Left screen outer lower buttons",49,50,51,52),
                new Group("Left screen inner upper buttons",53,54,55),
                new Group("Left screen inner lower buttons",56,57,58,59),
                new Group("Center screen left upper buttons",32,33,34),
                new Group("Center screen left lower buttons",35,36,37,38),
                new Group("Center screen right upper buttons",39,40,41),
                new Group("Center screen right lower buttons",42,43,44,45),
                new Group("Right screen inner upper buttons",60,61,62),
                new Group("Right screen inner lower buttons",63,64,65,66),
                new Group("Right screen outer upper buttons",67,68,69),
                new Group("Right screen outer lower buttons",70,71,72,73),
                new Group("Center lower screen buttons",77,76,75,74,78,79,80,81),
                new Group("Left lower screen inner buttons",92,93,94,95,96),
                new Group("Left lower screen outer buttons",101,100,99,98,97),
                new Group("Right lower screen inner buttons",82,83,84,85,86),
                new Group("Right lower screen outer buttons",91,90,89,88,87),
                new Group("Right dashboard inner sliders",102,103,104,105),
                new Group("Right dashboard outer sliders",106,107),
                new Group("Right console outer sliders",108,109,110,111,112,113,114),
                new Group("Side handles",115,116));
            Add(result,"SmallBlockCockpit",
                new Group("Left outer cap buttons",0),
                new Group("Left outer pair buttons",13,12),
                new Group("Right cap buttons",6,3,2,5,4,1),
                new Group("Right outer pair buttons",14,15),
                new Group("Right lower cap buttons",7),
                new Group("Left console outer buttons",11,10,9,8),
                new Group("Left console inner buttons",16,17,18),
                new Group("Right console inner buttons",19,20,21),
                new Group("Left dashboard sliders",22,23,24),
                new Group("Left outer sliders",25,26),
                new Group("Right outer sliders",27),
                new Group("Right console sliders",28,29,30,31,32,33,34),
                new Group("Side handles",35,36));
            Add(result,"CockpitOpen");
            Add(result,"RoverCockpit",
                new Group("Right handles",0),
                new Group("Lower pull bars",1));
            Add(result,"OpenCockpitSmall",
                new Group("Left front switches",0,2,4,6,8),
                new Group("Left rear switches",1,3,5,7,9),
                new Group("Right front switches",10,13,15,16,18),
                new Group("Right rear switches",11,12,14,17,19));
            Add(result,"OpenCockpitLarge",
                new Group("Left red cap buttons",0),
                new Group("Left inner cap buttons",1),
                new Group("Left white column buttons",2,3),
                new Group("Left gauge buttons",4,5),
                new Group("Right gauge buttons",6,7),
                new Group("Right front pair buttons",13,12),
                new Group("Right rear buttons",9,8,10,11),
                new Group("Left auxiliary switches",16,15,14,19,18,17),
                new Group("Main row 1 left switches",20,21,22,24,26,28,32,34),
                new Group("Main row 1 right switches",43,45),
                new Group("Main row 2 left switches",23,25,27,30,33,37,39,44),
                new Group("Main row 2 right switches",49,51),
                new Group("Main row 3 left switches",29,31,35,38,41),
                new Group("Main row 3 right switches",47,50,52,54,55),
                new Group("Main row 4 switches",36,40,42,46,48,53,56,57,59),
                new Group("Left outer column switches",61,62),
                new Group("Left gauge switches",65,58,64,63),
                new Group("Right gauge switches",66,67,60,68),
                new Group("Left sliders",72,71,73,74),
                new Group("Right sliders",76,75),
                new Group("Side handles",69,70));
            Add(result,"SmallBlockCockpitIndustrial",
                new Group("Left front switches",1,3,0,35),
                new Group("Left outer column switches",37,36,2),
                new Group("Left inner front switches",39,6,9,12,15),
                new Group("Left inner middle switches",4,7,10,13,38),
                new Group("Left inner rear switches",5,8,11,14),
                new Group("Dashboard left switches",16,17,18,19,20,21,22),
                new Group("Dashboard right switches",23,24,25,26,27),
                new Group("Right front switches",28,29,32,30),
                new Group("Right outer column switches",34,33,31));
            Add(result,"LargeBlockCockpitIndustrial",
                new Group("Left front switches",1,5,0,6),
                new Group("Left outer column switches",2,3,4),
                new Group("Left inner front switches",7,10,11,13,15),
                new Group("Left inner rear switches",8,9,12,14,16),
                new Group("Dashboard left switches",17,18,19,20,21,22,23),
                new Group("Dashboard right switches",24,25,26,27,28),
                new Group("Right front switches",29,31,30,32),
                new Group("Right outer column switches",33,34,35));
            AddRemaining(result);
            return result;
        }

        private static void Add(Dictionary<string,CockpitAssignmentLayout> result,string subtype,params Group[] groups)
        {
            result.Add(subtype,new CockpitAssignmentLayout(CockpitRig.Find(subtype).Count,groups));
        }
    }
}
