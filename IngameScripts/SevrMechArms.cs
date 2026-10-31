// Space Engineers VR: mech arms that copy the player's arms.
//
// Setup
// 1. Put [SEVR] in this programmable block's name. The plugin sends it VR data while you control a cockpit or
//    remote control on the same grid (subgrids included).
// 2. Tag each arm rotor or hinge in its name with side and role, for example [SEVR L shoulder], [SEVR L elbow],
//    [SEVR R wrist]. Shoulder joints copy the upper arm, elbow joints the forearm, wrist joints the hand.
//    Joints of one arm must form one chain; any number per role, in any order and mounting (the stator may sit on
//    either link). Parallel hinges between the same two links are driven as one joint.
//    Head joints are tagged [SEVR head] and turn the mech's head with yours; calibrate with the head facing the seat's way.
// 3. Run the argument "calibrate" in any arm pose. The arm is measured from its joints: the upper arm runs from the
//    last shoulder joint to the first elbow joint, the forearm from there to the first wrist joint (without one, toward
//    the middle of the elbow's head grid), and the hand continues the forearm with its back toward the mech's up.
//    Bend the elbow a little first if the forearm is not on the elbow hinge's top side.
// 4. Run "arms" to follow your arms and again to hold them. Tagged joints are switched on when following starts.
//
// Arguments: calibrate, reload, and the switches arms, head (head tracking), lean (head-tracked lean through the legs,
// with Mech Control Script) and headturn (looking aside turns the mech). A switch alone toggles; add "on" or "off" to
// set it, for example "lean on". Switches are saved with the world.
//
// Mapping "scaled" (default) puts the mech hand where your hand is, scaled from your arm length to the mech's, and
// bends the mech elbow the way yours bends. Mapping "joints" copies your joint angles instead, which only matches
// your hand position on a mech with human proportions. Origin "chest" measures your hands from your chest instead of
// each shoulder, so the mech's hands meet when yours do however wide its shoulders are; KeepOut then holds them
// that many metres in front of the cockpit.
// Weapons on an arm fire while that hand's trigger is pulled (setting Fire).
// Wheels on this block's grid steer with stick yaw and head turn.
// With Mech Control Script, keep the arm joints free of MCS tags or run MCS with arms off.
// With several VR pilots on one construct, the arms follow the first until that pilot stops sending.
// Settings are in Custom Data under [SEVR Arms] and apply when the script starts or on "reload".

const string Section = "SEVR Arms";
// The plugin resends unchanged data once a second.
const double Stale = 1.5, ScanInterval = 10;
// Radians per message.
const double Jump = .35;
const float FireTrigger = .6f;
// Each turn change runs Mech Control Script, so the turn input moves in steps.
const double TurnStep = .05;
const string StorageHeader = "SEVRARMS4", OldStorageHeader = "SEVRARMS3";

SevrData vr = new SevrData(), incoming = new SevrData();
readonly List<IMyMechanicalConnectionBlock> mechanical = new List<IMyMechanicalConnectionBlock>();
readonly List<IMyMotorStator> stators = new List<IMyMotorStator>();
readonly Dictionary<long, string> calibration = new Dictionary<long, string>();
readonly MyIni ini = new MyIni();
MechArm[] arms = new MechArm[0];
IMyTextSurface screen;
IMyShipController pilot;
double clock, received = -1, interval = .05, nextScan;
int messages;
bool following;
string problem = "", notice = "";

double gain = 8, feedForward = 1, maxRpm = 60, engageSeconds = 1, maxAccel = 20, scale, forearm, keepOut, shoulderGain, deadband, reachDown = 1;
bool scaled = true, fire = true, chest;
readonly MechLean lean = new MechLean();
readonly MechSteer steer = new MechSteer();
double leanPerMetre = 60, maxLean = 12, lastLegInput;
bool armsOn, leanOn, headTurn, headTrack;
double headTurnStart = 20, headTurnFull = 60, sentTurn, sentSteer;

readonly List<IMyUserControllableGun> guns = new List<IMyUserControllableGun>();

public Program()
{
    screen = Me.GetSurface(0);
    screen.ContentType = ContentType.TEXT_AND_IMAGE;
    screen.Font = "Monospace";
    screen.FontSize = .55f;
    LoadSettings();
    LoadStorage();
    Scan();
    // Joint velocity is saved with the world; a script stopped mid-follow would leave the arms turning.
    foreach (var arm in arms) arm.Hold();
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
}

public void Save()
{
    var text = new StringBuilder(StorageHeader).Append(' ').Append(Switches());
    foreach (var pair in calibration) text.Append('\n').Append(pair.Key).Append(' ').Append(pair.Value);
    Storage = text.ToString();
}

public void Main(string argument, UpdateType updateSource)
{
    clock += Runtime.TimeSinceLastRun.TotalSeconds;
    // Another programmable block may relay a SEVR1 message.
    if ((updateSource & UpdateType.Mod) != 0 || ((updateSource & UpdateType.Script) != 0 && argument != null && argument.StartsWith("SEVR1 ")))
    {
        // The first live pilot keeps the arms; another pilot's data is used once that one stops or goes quiet.
        if (incoming.TryParse(argument) && (!Live() || incoming.Controller == vr.Controller))
        {
            var swap = vr; vr = incoming; incoming = swap;
            if (received >= 0) interval = MathHelper.Clamp(clock - received, 1 / 60.0, .1);
            received = clock; messages++;
            Receive();
        }
        return;
    }
    if ((updateSource & (UpdateType.Terminal | UpdateType.Trigger | UpdateType.Script)) != 0) Command(argument.Trim().ToLowerInvariant());
    if ((updateSource & UpdateType.Update1) != 0) { Drive(); lean.Drive(); }
    if ((updateSource & UpdateType.Update10) != 0)
    {
        if ((!following && !lean.Active && clock >= nextScan) || Broken()) Scan();
        ReleaseUnwanted();
        Settle();
        UpdateLean();
        UpdateTurn();
        UpdateSteer();
        Show();
    }
    Runtime.UpdateFrequency = following || lean.Active ? UpdateFrequency.Update1 | UpdateFrequency.Update10 : UpdateFrequency.Update10;
}

void Command(string command)
{
    string[] words = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    string name = words.Length > 0 ? words[0] : "", value = words.Length > 1 ? words[1] : "toggle";
    if (words.Length > 2 || words.Length == 2 && value != "on" && value != "off" && value != "toggle") name = command;
    if (name == "calibrate" && words.Length == 1) Calibrate();
    else if (name == "reload" && words.Length == 1) { LoadSettings(); Scan(); }
    else if (name == "arms") { armsOn = Switch(armsOn, value, "VR arms"); ReleaseUnwanted(); }
    else if (name == "head") { headTrack = Switch(headTrack, value, "Head tracking"); ReleaseUnwanted(); }
    else if (name == "lean") leanOn = Switch(leanOn, value, "VR lean");
    else if (name == "headturn") headTurn = Switch(headTurn, value, "Head turn");
    else if (name != "") notice = "Unknown argument: " + command + ". Use calibrate, reload, arms, head, lean or headturn, optionally with on or off.";
}

bool Switch(bool current, string value, string label)
{
    bool on = value == "toggle" ? !current : value == "on";
    notice = label + (on ? " on" : " off");
    return on;
}

string Switches()
{
    return ((armsOn ? "arms " : "") + (headTrack ? "head " : "") + (leanOn ? "lean " : "") + (headTurn ? "headturn" : "")).TrimEnd();
}

// Lean holds the legs while the switch is on and the stick is idle; any walk, jump or turn input hands them back to MCS.
void UpdateLean()
{
    bool on = leanOn && Live() && vr.Has(SevrData.Head) && pilot != null;
    bool input = on && (pilot.MoveIndicator.LengthSquared() > .01 || Math.Abs(pilot.RotationIndicator.Y) > .1 || Math.Abs(pilot.RollIndicator) > .1 || sentTurn != 0);
    if (input || !on) lastLegInput = clock;
    // After walking, MCS needs a moment to bring the legs back to standing before they are held.
    lean.Update(on && clock - lastLegInput > 1.5, pilot, vr.HeadPose.Translation, leanPerMetre, maxLean);
    if (lean.Problem != "") notice = lean.Problem;
}

// MCS turns on foot only from roll, which a VR stick does not send. Stick yaw and, with head turn on, looking aside go to
// MCS as "turn", which it also uses for VTOL turning in flight. Looking aside turns the seat until it faces your head.
void UpdateTurn()
{
    var mcs = lean.Mcs;
    if (mcs == null) return;
    double turn = TurnInput();
    if (turn == sentTurn) return;
    // MCS parses the number in the game's culture.
    if (mcs.TryRun(MechLean.TurnCommand + turn.ToString("0.00"))) sentTurn = turn;
}

void UpdateSteer()
{
    double turn = TurnInput();
    if (turn == sentSteer) return;
    steer.Steer(turn);
    sentSteer = turn;
}

double TurnInput()
{
    double turn = 0;
    if (pilot != null && Live())
    {
        // Full stick yaw is 10 in the plugin's seat input.
        turn = pilot.RotationIndicator.Y / 10;
        if (headTurn && vr.Has(SevrData.Head))
        {
            Vector3D look = vr.HeadPose.Forward;
            double yaw = MathHelper.ToDegrees(Math.Atan2(look.X, -look.Z));
            turn += Math.Sign(yaw) * MathHelper.Clamp((Math.Abs(yaw) - headTurnStart) / Math.Max(1, headTurnFull - headTurnStart), 0, 1);
        }
        turn = Math.Round(MathHelper.Clamp(turn, -1, 1) / TurnStep) * TurnStep;
    }
    return turn;
}

bool Live() { return received >= 0 && clock - received < Stale && !vr.Has(SevrData.Stopped); }
bool Fresh() { return Live() && armsOn; }

// The arms follow with the "arms" switch, the head with the "head" switch.
bool Wanted(MechArm arm) { return arm.IsHead ? headTrack && Live() && vr.Has(SevrData.Head) : Fresh(); }

void ReleaseUnwanted()
{
    following = false;
    foreach (var arm in arms)
    {
        if (arm.Following && !Wanted(arm)) { arm.Following = false; arm.Hold(); }
        following |= arm.Following;
    }
}

void Receive()
{
    if (pilot == null || pilot.EntityId != vr.Controller) pilot = GridTerminalSystem.GetBlockWithId(vr.Controller) as IMyShipController;
    UpdateLean();
    UpdateTurn();
    ReleaseUnwanted();
    if (pilot == null) return;
    MechArm left = Arm(-1), right = Arm(1);
    bool fromChest = chest && scaled && left != null && right != null && left.Calibrated && right.Calibrated && left.Base == right.Base &&
        vr.Has(SevrData.LeftJoints) && vr.Has(SevrData.RightJoints);
    foreach (var arm in arms)
    {
        if (!arm.Calibrated || !Wanted(arm)) continue;
        if (arm.IsHead) arm.SolveHead(pilot, vr.HeadPose, arm.Following ? interval : 0);
        else
        {
            if (!vr.Has(arm.Side < 0 ? SevrData.LeftJoints : SevrData.RightJoints)) continue;
            Vector3D? hand = null;
            if (fromChest) hand = ChestTarget(arm, arm == left ? right : left);
            arm.Solve(pilot, arm.Side < 0 ? vr.LeftArm : vr.RightArm, arm.Side < 0 ? vr.LeftHand : vr.RightHand, arm.Following ? interval : 0, scaled, scale, forearm, hand);
            if (fire) arm.Fire((arm.Side < 0 ? vr.LeftHand : vr.RightHand).Trigger > FireTrigger);
        }
        if (!arm.Following)
        {
            arm.Following = true;
            arm.Engaged = clock;
            arm.Engage();
            following = true;
            Runtime.UpdateFrequency = UpdateFrequency.Update1 | UpdateFrequency.Update10;
        }
    }
}

MechArm Arm(int side) { foreach (var arm in arms) if (arm.Side == side) return arm; return null; }

// The mech hand from its shoulder, in the base grid frame: the pilot's hand measured from the chest, scaled by the arm
// length ratio, except that the span from the chest to the pilot's shoulder maps onto the mech's chest-to-shoulder span.
Vector3D ChestTarget(MechArm arm, MechArm other)
{
    MatrixD live = arm.Seat(pilot);
    SevrJoints mine = arm.Side < 0 ? vr.LeftArm : vr.RightArm, theirs = arm.Side < 0 ? vr.RightArm : vr.LeftArm;
    Vector3D pilotChest = (mine.Shoulder + theirs.Shoulder) / 2;
    Vector3D pilotSide = Vector3D.TransformNormal(mine.Shoulder - pilotChest, live), hand = Vector3D.TransformNormal(mine.Wrist - pilotChest, live);
    Vector3D shoulder = arm.Shoulder(), mechChest = (shoulder + other.Shoulder()) / 2, mechSide = shoulder - mechChest;
    // Depth below the chest stretches by ReachDown, so a mech with long arms reaches the ground without a straight arm.
    Vector3D up = live.Up;
    double below = -Vector3D.Dot(hand, up);
    if (below > 0) hand -= up * below * (reachDown - 1);
    double pilotHalf = pilotSide.Length(), mechHalf = mechSide.Length();
    double reach = (mine.Elbow - mine.Shoulder).Length() + (mine.Wrist - mine.Elbow).Length();
    if (pilotHalf < .01 || mechHalf < .01 || reach < .01) return hand * arm.Reach / Math.Max(reach, .01);
    pilotSide /= pilotHalf; mechSide /= mechHalf;
    double k = arm.Reach / reach, across = Vector3D.Dot(hand, pilotSide);
    double mapped = across < 0 ? across * k : across < pilotHalf ? across * mechHalf / pilotHalf : mechHalf + (across - pilotHalf) * k;
    Vector3D target = mechChest + mechSide * mapped + (hand - pilotSide * across) * k;
    if (keepOut > 0)
    {
        // Between the shoulders, push the hand ahead of the cockpit, easing out over half a metre beyond keepOut sideways.
        Vector3D forward = live.Forward, offset = target - mechChest;
        double ahead = Vector3D.Dot(offset, forward), side = Math.Abs(Vector3D.Dot(offset, mechSide));
        double need = keepOut * MathHelper.Clamp((keepOut + .5 - side) / .5, 0, 1);
        if (ahead < need) target += forward * (need - ahead);
    }
    return target - shoulder;
}

void Drive()
{
    if (!following) return;
    double age = Math.Min(clock - received, interval);
    foreach (var arm in arms)
    {
        if (!arm.Following || !arm.Calibrated) continue;
        double engage = engageSeconds > 0 ? Math.Min(1, (clock - arm.Engaged) / engageSeconds) : 1;
        arm.Drive(age, gain, shoulderGain > 0 ? shoulderGain : gain, MathHelper.ToRadians(deadband), feedForward, maxRpm * Math.PI / 30 * engage, maxAccel);
    }
}

void Hold()
{
    following = false;
    foreach (var arm in arms) { arm.Following = false; arm.Hold(); }
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
}

// While not following, tagged joints hold still even if another block or script gave them a speed; the screen names the first one.
void Settle()
{
    foreach (var arm in arms)
    {
        if (arm.Following) continue;
        string moved = arm.Settle();
        if (moved != null) notice = "Stopped " + moved + ": something else set its speed.";
    }
}

bool Broken()
{
    foreach (var arm in arms)
        foreach (var joint in arm.Joints)
        {
            if (joint.Block.Closed || !joint.Block.IsAttached) return true;
            foreach (var twin in joint.Twins) if (twin.Block.Closed || !twin.Block.IsAttached) return true;
        }
    return false;
}

void Scan()
{
    nextScan = clock + ScanInterval;
    if (following) Hold();
    problem = "";
    GridTerminalSystem.GetBlocksOfType(mechanical, b => b.IsSameConstructAs(Me) && b.IsAttached);
    GridTerminalSystem.GetBlocksOfType(stators, b => b.IsSameConstructAs(Me) && b.IsAttached);
    MapGrids();
    var found = new List<MechArm>();
    foreach (int side in new[] { -1, 1, 0 })
    {
        var joints = new List<ArmJoint>();
        foreach (var stator in stators)
        {
            int role;
            if (Tag(stator.CustomName, side, out role)) joints.Add(new ArmJoint { Block = stator, Role = role });
        }
        if (joints.Count == 0) continue;
        for (int i = joints.Count - 1; i > 0; i--)
            for (int k = 0; k < i; k++)
                if (joints[k].Role == joints[i].Role && SameLink(joints[k].Block, joints[i].Block))
                { joints[k].Twins.Add(new Twin { Block = joints[i].Block }); joints.RemoveAt(i); break; }
        string error = Order(joints);
        if (error != null) { problem += (side < 0 ? "Left" : side > 0 ? "Right" : "Head") + ": " + error + "\n"; continue; }
        var arm = new MechArm(side, joints);
        arm.Load(calibration);
        found.Add(arm);
    }
    arms = found.ToArray();
    // Turrets aim themselves; their Shoot would fire them wherever they point.
    GridTerminalSystem.GetBlocksOfType(guns, b => b.IsSameConstructAs(Me) && !(b is IMyLargeTurretBase));
    foreach (var arm in arms) if (!arm.IsHead) arm.FindGuns(guns, mechanical);
    lean.Scan(GridTerminalSystem, Me, stators);
    steer.Scan(GridTerminalSystem, Me);
}

// Name tag "[SEVR L shoulder]": side L or R, role shoulder, elbow or wrist, any case. "[SEVR head]" is side 0, matched like a wrist.
static bool Tag(string name, int side, out int role)
{
    role = -1;
    int start = name.IndexOf("[SEVR ", StringComparison.OrdinalIgnoreCase);
    if (start < 0) return false;
    int end = name.IndexOf(']', start);
    if (end < 0) return false;
    string[] words = name.Substring(start + 6, end - start - 6).Trim().ToLowerInvariant().Split(' ');
    if (side == 0)
    {
        if (words.Length != 1 || words[0] != "head") return false;
        role = 2;
        return true;
    }
    if (words.Length != 2 || words[0] != (side < 0 ? "l" : "r")) return false;
    role = Array.IndexOf(MechArm.Roles, words[1]);
    return role >= 0;
}

static bool SameLink(IMyMotorStator a, IMyMotorStator b)
{
    return (a.CubeGrid == b.CubeGrid && a.TopGrid == b.TopGrid) || (a.CubeGrid == b.TopGrid && a.TopGrid == b.CubeGrid);
}

// The construct as a tree from this block's grid: each grid's parent and the joint between them. A stator may sit on the
// body side or the limb side of its joint, so the tree is walked over both ends of every mechanical block.
readonly Dictionary<IMyCubeGrid, IMyCubeGrid> parentGrid = new Dictionary<IMyCubeGrid, IMyCubeGrid>();
readonly Dictionary<IMyCubeGrid, IMyMechanicalConnectionBlock> parentJoint = new Dictionary<IMyCubeGrid, IMyMechanicalConnectionBlock>();

void MapGrids()
{
    parentGrid.Clear(); parentJoint.Clear();
    var queue = new List<IMyCubeGrid> { Me.CubeGrid };
    parentGrid[Me.CubeGrid] = null;
    for (int i = 0; i < queue.Count; i++)
    {
        var grid = queue[i];
        foreach (var block in mechanical)
        {
            IMyCubeGrid other = block.CubeGrid == grid ? block.TopGrid : block.TopGrid == grid ? block.CubeGrid : null;
            if (other == null || parentGrid.ContainsKey(other)) continue;
            parentGrid[other] = grid; parentJoint[other] = block;
            queue.Add(other);
        }
    }
}

// The grid on the limb side of a stator: the one farther from this block's grid.
IMyCubeGrid Distal(IMyMotorStator stator)
{
    IMyCubeGrid grid = stator.TopGrid;
    for (int guard = 0; guard < 256 && grid != null; guard++)
    {
        IMyMechanicalConnectionBlock via;
        if (!parentJoint.TryGetValue(grid, out via)) break;
        if (via == stator) return stator.TopGrid;
        grid = parentGrid[grid];
    }
    return stator.CubeGrid;
}

// Sorts the joints from the body outward: a joint's depth counts the chain's joints between this block's grid and its limb side.
string Order(List<ArmJoint> joints)
{
    foreach (var joint in joints)
    {
        IMyCubeGrid distal = Distal(joint.Block);
        joint.Flipped = distal == joint.Block.CubeGrid;
        foreach (var twin in joint.Twins) twin.Flipped = Distal(twin.Block) == twin.Block.CubeGrid;
        if (!parentGrid.ContainsKey(distal)) return joint.Block.CustomName + " is not connected to this block's grid";
        joint.Depth = 0;
        for (var grid = distal; grid != null; grid = parentGrid[grid])
        {
            IMyMechanicalConnectionBlock via;
            if (!parentJoint.TryGetValue(grid, out via)) break;
            foreach (var other in joints)
                if (other.Block == via || other.Twins.Exists(t => t.Block == via)) joint.Depth++;
        }
    }
    joints.Sort((a, b) => a.Depth.CompareTo(b.Depth));
    for (int i = 1; i < joints.Count; i++)
    {
        if (joints[i].Depth == joints[i - 1].Depth) return joints[i].Block.CustomName + " and " + joints[i - 1].Block.CustomName + " are not in one chain";
        if (joints[i].Role < joints[i - 1].Role) return joints[i].Block.CustomName + " is closer to the body than a joint of a later role";
    }
    return null;
}

// The seat the head is calibrated against: the pilot's, else the main cockpit, else any controller on the construct.
IMyShipController Seat()
{
    if (pilot != null) return pilot;
    var seats = new List<IMyShipController>();
    GridTerminalSystem.GetBlocksOfType(seats, b => b.IsSameConstructAs(Me));
    foreach (var seat in seats) if (seat.IsMainCockpit) return seat;
    return seats.Count > 0 ? seats[0] : null;
}

void Calibrate()
{
    Scan();
    if (arms.Length == 0) { notice = "Calibrate: no tagged arm joints found."; return; }
    IMyShipController seat = Seat();
    foreach (var arm in arms) arm.Calibrate(calibration, seat);
    Save();
    notice = "Calibrated " + arms.Length + " chain(s).";
}

void LoadSettings()
{
    MyIniParseResult result;
    if (!ini.TryParse(Me.CustomData, out result)) { notice = "Custom Data: " + result; return; }
    if (!ini.ContainsSection(Section))
    {
        ini.Set(Section, "Gain", gain);
        ini.SetComment(Section, "Gain", "Joint speed per radian of error, 1/s. Higher is snappier and less stable.");
        ini.Set(Section, "ShoulderGain", shoulderGain);
        ini.SetComment(Section, "ShoulderGain", "Gain for the shoulder joints, which move the whole arm. 0 uses Gain.");
        ini.Set(Section, "Deadband", deadband);
        ini.SetComment(Section, "Deadband", "Degrees of error ignored while your arm is still, so the joints do not chase small wobbles.");
        ini.Set(Section, "Lean", leanPerMetre);
        ini.SetComment(Section, "Lean", "With VR lean on, degrees the mech leans per metre your head moves from where it was when lean took over.");
        ini.Set(Section, "MaxLean", maxLean);
        ini.SetComment(Section, "MaxLean", "Largest lean in degrees.");
        ini.Set(Section, "HeadTurn", headTurnStart + " " + headTurnFull);
        ini.SetComment(Section, "HeadTurn", "With head turn on, degrees you look aside before the mech turns, and where it turns at full speed.");
        ini.Set(Section, "FeedForward", feedForward);
        ini.SetComment(Section, "FeedForward", "Share of the arm's own speed passed straight to the joints, 0 to 1.");
        ini.Set(Section, "MaxRPM", maxRpm);
        ini.SetComment(Section, "MaxRPM", "Joint speed cap. The game caps large grid joints at 30 and small grid at 60.");
        ini.Set(Section, "EngageSeconds", engageSeconds);
        ini.SetComment(Section, "EngageSeconds", "Speed ramp after the arms turn on, so they do not jump to your pose.");
        ini.Set(Section, "MaxAccel", maxAccel);
        ini.SetComment(Section, "MaxAccel", "Joint speed change limit, rad/s^2. Lower values kick the mech body less.");
        ini.Set(Section, "Mapping", "scaled");
        ini.SetComment(Section, "Mapping", "scaled: the mech hand goes where your hand is, at the mech's arm length. joints: copy your joint angles.");
        ini.Set(Section, "Scale", scale);
        ini.SetComment(Section, "Scale", "Mech arm length per metre of your arm. 0 measures the mech arm from its joints.");
        ini.Set(Section, "Forearm", forearm);
        ini.SetComment(Section, "Forearm", "Forearm length in metres for arms without a wrist joint. 0 uses the upper arm length.");
        ini.Set(Section, "Origin", "shoulder");
        ini.SetComment(Section, "Origin", "shoulder: your hand is measured from your shoulder. chest: from your chest, so the mech's hands meet when yours do.");
        ini.Set(Section, "KeepOut", keepOut);
        ini.SetComment(Section, "KeepOut", "With Origin chest, metres the hands stay in front of the cockpit while between the shoulders. 0 is off.");
        ini.Set(Section, "ReachDown", reachDown);
        ini.SetComment(Section, "ReachDown", "With Origin chest, how much your hand's depth below your chest is stretched. Above 1 the mech reaches lower than your arm.");
        ini.Set(Section, "Fire", fire);
        ini.SetComment(Section, "Fire", "Weapons on an arm shoot while that hand's trigger is pulled.");
        Me.CustomData = ini.ToString();
        return;
    }
    fire = ini.Get(Section, "Fire").ToBoolean(fire);
    gain = Math.Max(0, ini.Get(Section, "Gain").ToDouble(gain));
    shoulderGain = Math.Max(0, ini.Get(Section, "ShoulderGain").ToDouble(shoulderGain));
    deadband = Math.Max(0, ini.Get(Section, "Deadband").ToDouble(deadband));
    feedForward = MathHelper.Clamp(ini.Get(Section, "FeedForward").ToDouble(feedForward), 0, 1);
    maxRpm = Math.Max(0, ini.Get(Section, "MaxRPM").ToDouble(maxRpm));
    engageSeconds = Math.Max(0, ini.Get(Section, "EngageSeconds").ToDouble(engageSeconds));
    maxAccel = Math.Max(0, ini.Get(Section, "MaxAccel").ToDouble(maxAccel));
    scaled = ini.Get(Section, "Mapping").ToString("scaled").Trim().ToLowerInvariant() != "joints";
    scale = Math.Max(0, ini.Get(Section, "Scale").ToDouble(scale));
    forearm = Math.Max(0, ini.Get(Section, "Forearm").ToDouble(forearm));
    chest = ini.Get(Section, "Origin").ToString("shoulder").Trim().ToLowerInvariant() == "chest";
    keepOut = Math.Max(0, ini.Get(Section, "KeepOut").ToDouble(keepOut));
    reachDown = MathHelper.Clamp(ini.Get(Section, "ReachDown").ToDouble(reachDown), .5, 4);
    leanPerMetre = Math.Max(0, ini.Get(Section, "Lean").ToDouble(leanPerMetre));
    maxLean = MathHelper.Clamp(ini.Get(Section, "MaxLean").ToDouble(maxLean), 0, 30);
    string[] turnRange = ini.Get(Section, "HeadTurn").ToString("").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    double turnValue;
    if (turnRange.Length == 2 && double.TryParse(turnRange[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out turnValue))
    {
        headTurnStart = MathHelper.Clamp(turnValue, 0, 90);
        if (double.TryParse(turnRange[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out turnValue))
            headTurnFull = MathHelper.Clamp(turnValue, headTurnStart + 1, 120);
    }
}

// First line: header, then the switches that are on. Then one calibration line per joint id.
void LoadStorage()
{
    calibration.Clear();
    string[] lines = Storage.Split('\n');
    string[] header = lines[0].Split(' ');
    if (header[0] != StorageHeader && header[0] != OldStorageHeader) return;
    armsOn = Array.IndexOf(header, "arms") > 0;
    headTrack = Array.IndexOf(header, "head") > 0;
    leanOn = Array.IndexOf(header, "lean") > 0;
    headTurn = Array.IndexOf(header, "headturn") > 0;
    for (int i = 1; i < lines.Length; i++)
    {
        int space = lines[i].IndexOf(' ');
        long id;
        if (space > 0 && long.TryParse(lines[i].Substring(0, space), out id)) calibration[id] = lines[i].Substring(space + 1);
    }
}

void Show()
{
    var text = new StringBuilder();
    string state = received < 0 ? "waiting for VR data" : !Live() ? "no recent VR data" : !armsOn ? "arms off, holding" : following ? "following" : "arms on";
    text.AppendLine("SEVR arms: " + state);
    if (received >= 0) text.AppendLine(messages + " messages, last " + (clock - received).ToString("0.0") + " s ago, joints " + (vr.Has(SevrData.LeftJoints) ? "L" : "-") + (vr.Has(SevrData.RightJoints) ? "R" : "-"));
    if (lean.Joints > 0) text.AppendLine("Lean: " + (lean.Active ? "holding legs" : leanOn ? "on" : "off") + ", " + lean.Joints + " hip joints");
    if (lean.Mcs != null) text.AppendLine("Head turn: " + (headTurn ? "on" : "off") + ", turn " + sentTurn.ToString("0.00"));
    if (Arm(0) != null) text.AppendLine("Head tracking: " + (headTrack ? "on" : "off"));
    if (steer.Wheels > 0) text.AppendLine("Wheels: " + steer.Wheels + ", steering " + sentSteer.ToString("0.00"));
    if (notice != "") text.AppendLine(notice);
    if (problem != "") text.Append(problem);
    if (arms.Length == 0 && problem == "") text.AppendLine("No [SEVR L shoulder] style joints found.");
    foreach (var arm in arms)
    {
        text.AppendLine((arm.IsHead ? "Head" : arm.Side < 0 ? "Left" : "Right") + (arm.Calibrated ? (arm.IsHead ? "" : scaled ? "  scale " + arm.Scale.ToString("0.0") : "  joints") : ": run calibrate"));
        foreach (var joint in arm.Joints)
            text.AppendLine("  " + (arm.IsHead ? "head" : MechArm.Roles[joint.Role]).PadRight(9) + MathHelper.ToDegrees(joint.Block.Angle).ToString("0").PadLeft(5) +
                (arm.Calibrated && arm.Following ? " -> " + MathHelper.ToDegrees(joint.Rest + joint.Solved).ToString("0").PadLeft(5) : "") +
                (joint.Twins.Count > 0 ? "  x" + (joint.Twins.Count + 1) : "") + (double.IsNaN(joint.Sent) ? "" : "  v " + joint.Sent.ToString("0.00")) +
                (joint.Stalled ? "  stall" : "") + (joint.Block.IsWorking ? "" : "  OFF"));
    }
    screen.WriteText(text);
    Echo(text.ToString());
}

// A stator driven with its chain joint: rest angle at calibration and the sign of its axis against the joint's.
// Flipped: the stator sits on the limb side of its joint, so its angle turns the limb the other way about its axis.
public class Twin
{
    public IMyMotorStator Block;
    public bool Flipped;
    public double Rest, Sign = 1, Sent = double.NaN;
    // Right-handed axis the limb turns about per stator angle, in world: the stator's Down, or its Up when flipped.
    public Vector3D AxisWorld { get { return Flipped ? Block.WorldMatrix.Up : Block.WorldMatrix.Down; } }
    public IMyCubeGrid Proximal { get { return Flipped ? Block.TopGrid : Block.CubeGrid; } }
    public IMyCubeGrid DistalGrid { get { return Flipped ? Block.CubeGrid : Block.TopGrid; } }
}

public class ArmJoint : Twin
{
    public int Role, Depth;
    // Rest axis in the arm's base grid frame (right-handed: the stator's Down), and the solved angle from rest.
    public Vector3D Axis;
    public double Solved, Rate;
    // Last speed command after the acceleration limit, the angle it was measured against, and time spent pushing without moving.
    public double Command, LastAngle = double.NaN, StallTime;
    public bool Stalled;
    public readonly List<Twin> Twins = new List<Twin>();
}

public class MechArm
{
    public static readonly string[] Roles = { "shoulder", "elbow", "wrist" };
    // -1 left arm, 1 right arm, 0 head (wrist-role joints matching the head's forward and up).
    public readonly int Side;
    public readonly ArmJoint[] Joints;
    public bool Calibrated, Following;
    public double Engaged;
    public bool IsHead { get { return Side == 0; } }
    // Mech metres per metre of the player's arm in scaled mapping, for the screen.
    public double Scale = 1;
    // Base grid frame directions of the arm as calibrated: upper arm, elbow hinge, forearm, elbow hinge, hand forward, hand up.
    readonly Vector3D[] reference = new Vector3D[6];
    // Segment lengths in metres from the joint positions at calibration; the forearm is 0 without a wrist joint.
    double upperLength, foreLength;
    // Elbow bend direction kept from the last message for a straight player arm.
    Vector3D lastBend = Vector3D.Down;
    readonly ArmSolver solver;
    readonly List<IMyUserControllableGun> guns = new List<IMyUserControllableGun>();
    bool firing;

    public MechArm(int side, List<ArmJoint> joints)
    {
        Side = side;
        Joints = joints.ToArray();
        solver = new ArmSolver(Joints.Length);
    }

    public IMyCubeGrid Base { get { return Joints[0].Proximal; } }
    // Mech arm length from the shoulder to the hand as measured at calibration.
    public double Reach { get { return upperLength + (foreLength > .01 ? foreLength : upperLength); } }

    // Where the upper arm turns, in the base grid frame: the point on the last shoulder axis nearest the one before it,
    // so a twist joint along the upper arm does not shorten the measured arm. Parallel axes use the last joint's position.
    public Vector3D Shoulder()
    {
        MatrixD inverse = MatrixD.Transpose(Base.WorldMatrix.GetOrientation());
        return Vector3D.TransformNormal(ShoulderWorld() - Base.GetPosition(), inverse);
    }
    Vector3D ShoulderWorld()
    {
        ArmJoint last = Last(0), before = null;
        foreach (var joint in Joints) if (joint.Role == 0 && joint != last) before = joint;
        if (last == null) return Joints[0].Block.GetPosition();
        Vector3D p = last.Block.GetPosition();
        if (before == null) return p;
        Vector3D a = last.AxisWorld, b = before.AxisWorld, n = Vector3D.Cross(a, b);
        double nn = n.LengthSquared();
        if (nn < 1e-6) return p;
        return p + a * (Vector3D.Dot(Vector3D.Cross(before.Block.GetPosition() - p, b), n) / nn);
    }

    public void Calibrate(Dictionary<long, string> store, IMyShipController seat)
    {
        MatrixD inverse = MatrixD.Transpose(Base.WorldMatrix.GetOrientation());
        foreach (var joint in Joints)
        {
            joint.Axis = Vector3D.Normalize(Vector3D.TransformNormal(joint.AxisWorld, inverse));
            joint.Rest = joint.Block.Angle;
            joint.Solved = joint.Rate = 0;
            foreach (var twin in joint.Twins)
            {
                twin.Rest = twin.Block.Angle;
                twin.Sign = Vector3D.Dot(twin.AxisWorld, joint.AxisWorld) < 0 ? -1 : 1;
            }
        }
        if (IsHead) MeasureHead(seat, inverse); else Measure(inverse);
        foreach (var joint in Joints)
        {
            store[joint.Block.EntityId] = Format(joint);
            foreach (var twin in joint.Twins) store[twin.Block.EntityId] = Format(twin);
        }
        Calibrated = true;
        Prepare();
    }

    // The arm as it stands: links run between the joints of successive roles; a straight elbow bends about its hinge.
    void Measure(MatrixD inverse)
    {
        ArmJoint shoulder = Last(0), elbow = First(1), wrist = First(2);
        Vector3D upper = Vector3D.Forward, fore, bend, up = Vector3D.Up;
        upperLength = foreLength = 0;
        Vector3D pivot = ShoulderWorld();
        if (shoulder != null && elbow != null) { upper = Direction(pivot, elbow, inverse, upper); upperLength = Vector3D.Distance(pivot, elbow.Block.GetPosition()); }
        else if (shoulder != null && wrist != null) { upper = Direction(pivot, wrist, inverse, upper); upperLength = Vector3D.Distance(pivot, wrist.Block.GetPosition()); }
        if (elbow != null && wrist != null) foreLength = Length(elbow, wrist);
        fore = elbow == null ? upper : wrist != null ? Direction(elbow, wrist, inverse, upper) : Forearm(elbow, inverse, upper);
        bend = Vector3D.Cross(upper, fore);
        if (bend.Length() < .17) bend = elbow != null ? elbow.Axis : Vector3D.Cross(upper, up);
        if (bend.LengthSquared() < 1e-6) bend = Vector3D.Cross(upper, Vector3D.Backward);
        up -= fore * Vector3D.Dot(up, fore);
        if (up.LengthSquared() < 1e-6) up = Vector3D.Cross(fore, Vector3D.Right);
        reference[0] = upper; reference[1] = reference[3] = Vector3D.Normalize(bend);
        reference[2] = fore; reference[4] = fore; reference[5] = Vector3D.Normalize(up);
    }

    // The head as it stands faces the seat's way; without a seat, the base grid's forward.
    void MeasureHead(IMyShipController seat, MatrixD inverse)
    {
        Vector3D forward = Vector3D.Forward, up = Vector3D.Up;
        if (seat != null)
        {
            forward = Vector3D.TransformNormal(seat.WorldMatrix.Forward, inverse);
            up = Vector3D.TransformNormal(seat.WorldMatrix.Up, inverse);
        }
        upperLength = foreLength = 0;
        reference[0] = reference[2] = reference[4] = forward;
        reference[1] = reference[3] = Vector3D.Cross(forward, up);
        reference[5] = up;
    }

    ArmJoint First(int role) { foreach (var joint in Joints) if (joint.Role == role) return joint; return null; }
    ArmJoint Last(int role) { ArmJoint found = null; foreach (var joint in Joints) if (joint.Role == role) found = joint; return found; }

    static Vector3D Direction(ArmJoint from, ArmJoint to, MatrixD inverse, Vector3D fallback) { return Direction(from.Block.GetPosition(), to, inverse, fallback); }
    static Vector3D Direction(Vector3D from, ArmJoint to, MatrixD inverse, Vector3D fallback)
    {
        Vector3D delta = Vector3D.TransformNormal(to.Block.GetPosition() - from, inverse);
        return delta.LengthSquared() < 1e-4 ? fallback : Vector3D.Normalize(delta);
    }
    // Without a wrist joint the forearm is the grid on the elbow's limb side; blocks beside it along the elbow axis do not tilt it.
    static Vector3D Forearm(ArmJoint elbow, MatrixD inverse, Vector3D fallback)
    {
        var grid = elbow.DistalGrid;
        if (grid == null) return fallback;
        Vector3D middle = Vector3D.Transform(new Vector3D(grid.Min + grid.Max) * (grid.GridSize / 2), grid.WorldMatrix);
        Vector3D delta = Vector3D.TransformNormal(middle - elbow.Block.GetPosition(), inverse);
        delta -= elbow.Axis * Vector3D.Dot(delta, elbow.Axis);
        return delta.LengthSquared() < .01 ? fallback : Vector3D.Normalize(delta);
    }
    static double Length(ArmJoint from, ArmJoint to) { return Vector3D.Distance(from.Block.GetPosition(), to.Block.GetPosition()); }

    // Joint line: rest angle, axis xyz, the six reference vectors, upper arm and forearm lengths. Twin line: rest angle, sign.
    string Format(ArmJoint joint)
    {
        var values = new List<double> { joint.Rest, joint.Axis.X, joint.Axis.Y, joint.Axis.Z };
        foreach (var v in reference) { values.Add(v.X); values.Add(v.Y); values.Add(v.Z); }
        values.Add(upperLength); values.Add(foreLength);
        return Format(values);
    }
    string Format(Twin twin) { return Format(new List<double> { twin.Rest, twin.Sign }); }
    static string Format(List<double> values)
    {
        var text = new StringBuilder();
        foreach (double value in values) text.Append(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
        return text.ToString().TrimEnd();
    }

    public void Load(Dictionary<long, string> store)
    {
        Calibrated = false;
        foreach (var joint in Joints)
        {
            var values = Parse(store, joint.Block.EntityId, 24);
            if (values == null) return;
            joint.Rest = values[0];
            joint.Axis = new Vector3D(values[1], values[2], values[3]);
            for (int i = 0; i < 6; i++) reference[i] = new Vector3D(values[4 + 3 * i], values[5 + 3 * i], values[6 + 3 * i]);
            upperLength = values[22]; foreLength = values[23];
            foreach (var twin in joint.Twins)
            {
                var pair = Parse(store, twin.Block.EntityId, 2);
                if (pair == null) return;
                twin.Rest = pair[0]; twin.Sign = pair[1];
            }
        }
        Calibrated = true;
        Prepare();
    }

    static double[] Parse(Dictionary<long, string> store, long id, int count)
    {
        string line;
        if (!store.TryGetValue(id, out line)) return null;
        string[] fields = line.Split(' ');
        if (fields.Length != count) return null;
        var values = new double[count];
        for (int i = 0; i < count; i++)
            if (!double.TryParse(fields[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out values[i])) return null;
        return values;
    }

    void Prepare()
    {
        for (int i = 0; i < Joints.Length; i++)
        {
            solver.Axes[i] = Joints[i].Axis;
            solver.Roles[i] = Joints[i].Role;
            solver.Angles[i] = 0;
        }
        for (int i = 0; i < 6; i++) solver.Reference[i] = reference[i];
    }

    // The seat as it is now, so tracked arms stay in the pilot's view when joints between the cockpit and the arms move.
    public MatrixD Seat(IMyShipController controller) { return controller.WorldMatrix.GetOrientation() * MatrixD.Transpose(Base.WorldMatrix.GetOrientation()); }

    // interval: seconds since the previous message, 0 when the arm has just engaged.
    // scaled: place the mech hand at the player's hand position scaled by the arm length ratio; scaleSetting 0 measures it;
    // forearmSetting is the forearm length for arms without a wrist joint, 0 for the upper arm length.
    // target: the mech hand from its shoulder in the base grid frame when the program maps it (Origin chest), else null.
    public void Solve(IMyShipController controller, SevrJoints arm, SevrHand hand, double interval, bool scaled, double scaleSetting, double forearmSetting, Vector3D? target)
    {
        MatrixD live = Seat(controller);
        Vector3D upper = Vector3D.TransformNormal(arm.Elbow - arm.Shoulder, live), fore = Vector3D.TransformNormal(arm.Wrist - arm.Elbow, live);
        if (scaled || First(1) == null) Retarget(ref upper, ref fore, scaled, scaleSetting, forearmSetting, target);
        upper = Vector3D.Normalize(upper); fore = Vector3D.Normalize(fore);
        Vector3D bend = Vector3D.Cross(upper, fore);
        double bent = bend.Length();
        // The elbow's hinge is undefined with the arm straight.
        double hingeWeight = MathHelper.Clamp(bent / .3, 0, 1);
        if (bent > 1e-6) bend /= bent;
        solver.Target[0] = upper; solver.Target[1] = bend;
        solver.Target[2] = fore; solver.Target[3] = bend;
        solver.Target[4] = Vector3D.TransformNormal(hand.Pose.Forward, live); solver.Target[5] = Vector3D.TransformNormal(hand.Pose.Up, live);
        Apply(hingeWeight, interval);
    }

    // The head joints match the pilot's head forward and up, measured in the seat like the hands.
    public void SolveHead(IMyShipController controller, MatrixD head, double interval)
    {
        MatrixD live = Seat(controller);
        solver.Target[4] = Vector3D.TransformNormal(head.Forward, live); solver.Target[5] = Vector3D.TransformNormal(head.Up, live);
        for (int i = 0; i < 4; i++) solver.Target[i] = solver.Reference[i];
        Apply(1, interval);
    }

    void Apply(double hingeWeight, double interval)
    {
        for (int i = 0; i < Joints.Length; i++)
        {
            var joint = Joints[i];
            bool limited = Limited(joint.Block);
            solver.Lower[i] = limited ? joint.Block.LowerLimitRad - joint.Rest : double.NaN;
            solver.Upper[i] = limited ? joint.Block.UpperLimitRad - joint.Rest : double.NaN;
        }
        solver.Solve(hingeWeight);
        for (int i = 0; i < Joints.Length; i++)
        {
            var joint = Joints[i];
            double change = ArmSolver.Wrap(solver.Angles[i] - joint.Solved);
            // A change larger than the joints can turn between messages is the solver switching solutions; extrapolating it spikes the speed.
            joint.Rate = interval > 0 && Math.Abs(change) <= Jump ? change / interval : 0;
            joint.Solved = solver.Angles[i];
        }
    }

    static bool Limited(IMyMotorStator block) { return block.LowerLimitRad > -1e6f && block.UpperLimitRad < 1e6f; }

    // Player arm (upper, fore in base frame metres) to mech arm segments: the hand goes to the scaled hand position, the
    // elbow bends toward the player's elbow. Without an elbow joint the single segment points at the hand.
    void Retarget(ref Vector3D upper, ref Vector3D fore, bool scaled, double scaleSetting, double forearmSetting, Vector3D? target)
    {
        Vector3D wrist = upper + fore;
        double reach = wrist.Length(), playerReach = upper.Length() + fore.Length();
        if (reach < 1e-4 || playerReach < 1e-4) return;
        double mechUpper = upperLength > .01 ? upperLength : 1;
        double mechFore = foreLength > .01 ? foreLength : (forearmSetting > .01 ? forearmSetting : mechUpper);
        Scale = scaleSetting > 0 ? scaleSetting : (mechUpper + mechFore) / playerReach;
        if (target.HasValue && target.Value.LengthSquared() > 1e-8) { wrist = target.Value; reach = wrist.Length() / Scale; }
        Vector3D axis = Vector3D.Normalize(wrist);
        if (First(1) == null) { upper = axis; fore = axis; return; }
        double distance = MathHelper.Clamp(reach * Scale, Math.Abs(mechUpper - mechFore) + .01, mechUpper + mechFore - .01);
        Vector3D hint = upper - axis * Vector3D.Dot(upper, axis);
        if (hint.LengthSquared() > 1e-4) lastBend = Vector3D.Normalize(hint);
        else hint = lastBend - axis * Vector3D.Dot(lastBend, axis);
        if (hint.LengthSquared() < 1e-6) hint = Vector3D.Cross(axis, Vector3D.Right);
        hint = Vector3D.Normalize(hint);
        double along = (mechUpper * mechUpper - mechFore * mechFore + distance * distance) / (2 * distance);
        double height = Math.Sqrt(Math.Max(0, mechUpper * mechUpper - along * along));
        Vector3D elbow = axis * along + hint * height;
        upper = elbow;
        fore = axis * distance - elbow;
    }

    // Targets run ahead of the last message by its measured speed, up to one message interval, so 20-30 Hz data drives smoothly.
    // Speed changes are limited to maxAccel per second; a joint that pushes without moving is throttled until it catches up.
    public void Drive(double age, double gain, double shoulderGain, double deadband, double feedForward, double maxSpeed, double maxAccel)
    {
        const double step = 1 / 60.0;
        foreach (var joint in Joints)
        {
            double move = joint.Solved + joint.Rate * age;
            double speed = Speed(joint, move, joint.Rate, joint.Role == 0 ? shoulderGain : gain, deadband, feedForward, maxSpeed, maxAccel);
            double actual = double.IsNaN(joint.LastAngle) ? joint.Command : ArmSolver.Wrap(joint.Block.Angle - joint.LastAngle) / step;
            joint.LastAngle = joint.Block.Angle;
            bool pushing = Math.Abs(joint.Command) > .1 && Math.Abs(actual) < .15 * Math.Abs(joint.Command);
            joint.StallTime = pushing ? joint.StallTime + step : Math.Max(0, joint.StallTime - 2 * step);
            joint.Stalled = joint.StallTime > .25;
            if (joint.Stalled) speed *= .25;
            double limit = maxAccel > 0 ? maxAccel * step : double.MaxValue;
            joint.Command = MathHelper.Clamp(speed, joint.Command - limit, joint.Command + limit);
            Send(joint, Quantize(joint.Command));
            foreach (var twin in joint.Twins) Send(twin, Quantize(twin.Sign * joint.Command));
        }
    }

    static double Speed(Twin twin, double move, double rate, double gain, double deadband, double feedForward, double maxSpeed, double maxAccel)
    {
        double target = twin.Rest + move;
        // A limited joint's angle and target are both inside its limits; wrapping could pick the way through the stop.
        double error = Limited(twin.Block) ? target - twin.Block.Angle : ArmSolver.Wrap(target - twin.Block.Angle);
        // Correcting small errors a few ticks late feeds the mech's body sway, so a still target ignores them; the band fades out by .2 rad/s.
        double band = deadband * MathHelper.Clamp(1 - Math.Abs(rate) / .2, 0, 1);
        error = Math.Sign(error) * Math.Max(0, Math.Abs(error) - band);
        double approach = gain * Math.Abs(error);
        // Never faster than a stop at maxAccel allows within the remaining error, so the acceleration limit cannot overshoot.
        if (maxAccel > 0) approach = Math.Min(approach, Math.Sqrt(2 * maxAccel * Math.Max(Math.Abs(error) - .002, 0)));
        double speed = MathHelper.Clamp(Math.Sign(error) * approach + feedForward * rate, -maxSpeed, maxSpeed);
        if (Math.Abs(error) < .002 && Math.Abs(rate) < .01) speed = 0;
        return speed;
    }

    // Each change is replicated in multiplayer; steps of .02 rad/s are not visible.
    static double Quantize(double speed) { return Math.Round(speed / .02) * .02; }

    // Parked arms often have their hinges switched off, which ignores velocity commands.
    public void Engage()
    {
        foreach (var joint in Joints)
        {
            joint.Block.Enabled = true;
            foreach (var twin in joint.Twins) twin.Block.Enabled = true;
        }
    }

    // Weapons on the arm's own grids: everything beyond the first joint, whichever side its stators sit on.
    public void FindGuns(List<IMyUserControllableGun> all, List<IMyMechanicalConnectionBlock> mechanical)
    {
        guns.Clear();
        var carried = new HashSet<IMyCubeGrid> { Base };
        var pending = new List<IMyCubeGrid> { Joints[0].DistalGrid };
        while (pending.Count > 0)
        {
            var grid = pending[pending.Count - 1]; pending.RemoveAt(pending.Count - 1);
            if (grid == null || !carried.Add(grid)) continue;
            foreach (var block in mechanical)
            {
                if (block == Joints[0].Block || Joints[0].Twins.Exists(t => t.Block == block)) continue;
                if (block.CubeGrid == grid) pending.Add(block.TopGrid);
                else if (block.TopGrid == grid) pending.Add(block.CubeGrid);
            }
        }
        carried.Remove(Base);
        foreach (var gun in all) if (carried.Contains(gun.CubeGrid)) guns.Add(gun);
    }

    public void Fire(bool shoot)
    {
        if (shoot == firing) return;
        firing = shoot;
        foreach (var gun in guns) gun.Shoot = shoot;
    }

    public void Hold()
    {
        Fire(false);
        foreach (var joint in Joints)
        {
            joint.Rate = joint.Command = joint.StallTime = 0;
            joint.Stalled = false;
            joint.LastAngle = double.NaN;
            Send(joint, 0);
            foreach (var twin in joint.Twins) Send(twin, 0);
        }
    }

    public string Settle()
    {
        string moved = null;
        foreach (var joint in Joints)
        {
            if (Math.Abs(joint.Block.TargetVelocityRad) > 1e-4) { moved = moved ?? joint.Block.CustomName; joint.Block.TargetVelocityRad = 0; }
            joint.Sent = 0;
            foreach (var twin in joint.Twins)
            {
                if (Math.Abs(twin.Block.TargetVelocityRad) > 1e-4) { moved = moved ?? twin.Block.CustomName; twin.Block.TargetVelocityRad = 0; }
                twin.Sent = 0;
            }
        }
        return moved;
    }

    static void Send(Twin twin, double speed)
    {
        if (speed == twin.Sent) return;
        twin.Block.TargetVelocityRad = (float)speed;
        twin.Sent = speed;
    }
}

// Head-tracked lean through the legs. With the feet planted, turning a hip joint by d tilts the torso by -d about that joint's
// axis, so the wanted torso tilt is split over the hip rotors (MCS tag H) and the hip strafe joints on the torso (tag S).
public class MechLean
{
    // AfterAStorm's Mech Control Script: its block is the one whose Custom Data has this section, and its commands.
    const string McsSection = "[Controls]", LegsOff = "legs off", LegsOn = "legs on";
    public const string TurnCommand = "turn =";
    const char HipTag = 'H', StrafeTag = 'S';
    const double Gain = 4, MaxSpeed = .6, Deadzone = .03, Settle = .01, ReleaseTime = 1.5;
    class LegJoint { public IMyMotorStator Block; public double Captured; }
    readonly List<LegJoint> joints = new List<LegJoint>();
    IMyProgrammableBlock mcs;
    IMyShipController pilot;
    Vector3D neutral, wanted;
    double pitch, roll, releasing = -1;
    bool holding;
    public string Problem = "";
    public bool Active { get { return holding || releasing >= 0; } }
    public int Joints { get { return joints.Count; } }
    public IMyProgrammableBlock Mcs { get { return mcs; } }

    public void Scan(IMyGridTerminalSystem grid, IMyProgrammableBlock me, List<IMyMotorStator> stators)
    {
        if (Active) return;
        joints.Clear();
        mcs = null;
        var blocks = new List<IMyProgrammableBlock>();
        grid.GetBlocksOfType(blocks, b => b != me && b.IsSameConstructAs(me) && b.Enabled && b.CustomData.Contains(McsSection));
        if (blocks.Count > 0) mcs = blocks[0];
        foreach (var stator in stators)
        {
            char tag = Tag(stator.CustomName);
            // The ankle also has a strafe joint; only the one on the torso tilts it.
            if (tag == HipTag || tag == StrafeTag && stator.CubeGrid == me.CubeGrid) joints.Add(new LegJoint { Block = stator });
        }
    }

    // MCS names joints "Rotor HL+", "Hinge SR+": block type, then the tag letters before the side.
    static char Tag(string name)
    {
        int space = name.IndexOf(' ');
        if (space < 0 || space + 2 >= name.Length) return ' ';
        char tag = name[space + 1], side = name[space + 2];
        return side == 'L' || side == 'R' ? tag : ' ';
    }

    public void Update(bool want, IMyShipController controller, Vector3D head, double perMetre, double max)
    {
        Problem = "";
        if (want && !holding && releasing < 0)
        {
            if (mcs == null || joints.Count == 0) { Problem = "VR lean: no Mech Control Script block or hip joints found."; return; }
            pilot = controller;
            neutral = head;
            pitch = roll = 0;
            mcs.TryRun(LegsOff);
            foreach (var joint in joints) joint.Captured = joint.Block.Angle;
            holding = true;
        }
        else if (!want && holding) Release();
        if (!holding) { wanted = Vector3D.Zero; return; }
        Vector3D offset = head - neutral;
        wanted = new Vector3D(Lean(Vector3D.Dot(offset, Vector3D.Forward), perMetre, max), 0, Lean(Vector3D.Dot(offset, Vector3D.Right), perMetre, max));
    }

    static double Lean(double metres, double perMetre, double max)
    {
        double beyond = Math.Max(0, Math.Abs(metres) - Deadzone);
        return Math.Sign(metres) * Math.Min(MathHelper.ToRadians(max), MathHelper.ToRadians(perMetre) * beyond);
    }

    public void Release()
    {
        if (!holding) return;
        holding = false;
        releasing = 0;
        wanted = Vector3D.Zero;
    }

    public void Drive()
    {
        if (!Active || pilot == null) return;
        const double step = 1 / 60.0;
        pitch += (wanted.X - pitch) * Math.Min(1, step * 6);
        roll += (wanted.Z - roll) * Math.Min(1, step * 6);
        // Torso tilt as a rotation vector in the torso frame: forward lean turns the seat's forward toward its down, right lean
        // turns its up toward its right.
        MatrixD toTorso = MatrixD.Transpose(pilot.CubeGrid.WorldMatrix.GetOrientation());
        Vector3D forward = Vector3D.TransformNormal(pilot.WorldMatrix.Forward, toTorso), up = Vector3D.TransformNormal(pilot.WorldMatrix.Up, toTorso);
        Vector3D right = Vector3D.Cross(forward, up);
        Vector3D tilt = Vector3D.Cross(forward, -up) * pitch + Vector3D.Cross(up, right) * roll;
        bool settled = true;
        foreach (var joint in joints)
        {
            Vector3D axis = Vector3D.TransformNormal(joint.Block.WorldMatrix.Down, toTorso);
            double target = joint.Captured - Vector3D.Dot(tilt, axis);
            double error = ArmSolver.Wrap(target - joint.Block.Angle);
            settled &= Math.Abs(error) < Settle && Math.Abs(pitch) + Math.Abs(roll) < Settle;
            joint.Block.TargetVelocityRad = (float)MathHelper.Clamp(Gain * error, -MaxSpeed, MaxSpeed);
        }
        if (releasing < 0) return;
        releasing += step;
        if (!settled && releasing < ReleaseTime) return;
        foreach (var joint in joints) joint.Block.TargetVelocityRad = 0;
        mcs.TryRun(LegsOn);
        releasing = -1;
    }
}

// Steers the wheels on the programmable block's grid with the turn input; 0 hands steering back to the seat.
public class MechSteer
{
    readonly List<IMyMotorSuspension> wheels = new List<IMyMotorSuspension>();
    public int Wheels { get { return wheels.Count; } }

    public void Scan(IMyGridTerminalSystem grid, IMyProgrammableBlock me)
    {
        wheels.Clear();
        grid.GetBlocksOfType(wheels, w => w.CubeGrid == me.CubeGrid);
    }

    public void Steer(double turn)
    {
        foreach (var wheel in wheels) wheel.SteeringOverride = (float)turn;
    }
}

// Joint angles that turn the arm's calibration pose into the tracked pose, in the base grid frame.
// Each role matches a pair of vectors (primary, secondary): shoulder (upper arm, elbow hinge), elbow (forearm, elbow hinge),
// wrist (hand forward, hand up). The joints of a role are solved one at a time, proximal first, a few passes.
public class ArmSolver
{
    const int Passes = 6;
    const double Hysteresis = .5, Damp = .05;
    public readonly Vector3D[] Axes;
    public readonly int[] Roles;
    // Relative to the calibration angle; NaN for unlimited joints.
    public readonly double[] Angles, Lower, Upper;
    public readonly Vector3D[] Reference = new Vector3D[6], Target = new Vector3D[6];
    readonly QuaternionD[] turns;

    public ArmSolver(int count)
    {
        Axes = new Vector3D[count];
        Roles = new int[count];
        Angles = new double[count];
        Lower = new double[count];
        Upper = new double[count];
        turns = new QuaternionD[count];
    }

    public void Solve(double hingeWeight)
    {
        for (int role = 0; role < 3; role++)
        {
            int first = Array.IndexOf(Roles, role), last = Array.LastIndexOf(Roles, role);
            if (first < 0) continue;
            int count = last - first + 1;
            // A role with three joints can match both vectors; with fewer, the secondary only settles what the primary leaves free.
            double secondary = (count >= 3 ? .5 : .05) * (role < 2 ? hingeWeight : 1);
            for (int pass = 0; pass < (count > 1 ? Passes : 1); pass++)
                for (int i = first; i <= last; i++)
                    Step(i, last, role, secondary);
        }
    }

    void Step(int joint, int last, int role, double secondary)
    {
        QuaternionD before = QuaternionD.Identity, after = QuaternionD.Identity;
        for (int i = 0; i < joint; i++) before = before * Turn(i);
        for (int i = joint + 1; i <= last; i++) after = after * Turn(i);
        QuaternionD undo = QuaternionD.Conjugate(before);
        Vector3D axis = Axes[joint];
        double sine = 0, cosine = 0;
        for (int k = 0; k < 2; k++)
        {
            double weight = k == 0 ? 1 : secondary;
            Vector3D p = Vector3D.Transform(Reference[role * 2 + k], after), t = Vector3D.Transform(Target[role * 2 + k], undo);
            p -= axis * Vector3D.Dot(p, axis);
            t -= axis * Vector3D.Dot(t, axis);
            sine += weight * Vector3D.Dot(axis, Vector3D.Cross(p, t));
            cosine += weight * Vector3D.Dot(p, t);
        }
        // Near the joint's axis the best angle is undefined; keep the previous one.
        double strength = sine * sine + cosine * cosine;
        if (strength < 1e-4) return;
        // Close to that, the best angle swings wildly (the shoulder's singular pose); move only as far as the pose constrains the joint,
        // so the arm does not switch to the opposite solution through its body.
        double angle = Angles[joint] + Wrap(Math.Atan2(sine, cosine) - Angles[joint]) * Math.Min(1, strength / Damp);
        if (!double.IsNaN(Lower[joint])) angle = Limit(angle, Angles[joint], Lower[joint], Upper[joint]);
        Angles[joint] = angle;
    }

    QuaternionD Turn(int i) { return QuaternionD.CreateFromAxisAngle(Axes[i], Angles[i]); }

    // The turn of an angle inside the limits nearest the previous angle. Out of range, the joint stays at the stop it is
    // nearest unless the other stop is clearly closer, so a target beyond the range does not flip it between its stops.
    static double Limit(double angle, double previous, double lower, double upper)
    {
        double best = double.NaN;
        for (int k = -2; k <= 2; k++)
        {
            double turn = angle + k * MathHelper.TwoPi;
            if (turn >= lower && turn <= upper && (double.IsNaN(best) || Math.Abs(turn - previous) < Math.Abs(best - previous))) best = turn;
        }
        if (!double.IsNaN(best)) return best;
        double toLower = Math.Abs(Wrap(angle - lower)), toUpper = Math.Abs(Wrap(angle - upper));
        if (Math.Abs(previous - lower) < Math.Abs(previous - upper)) return toUpper + Hysteresis < toLower ? upper : lower;
        return toLower + Hysteresis < toUpper ? lower : upper;
    }

    public static double Wrap(double angle)
    {
        angle %= MathHelper.TwoPi;
        if (angle > Math.PI) angle -= MathHelper.TwoPi;
        else if (angle < -Math.PI) angle += MathHelper.TwoPi;
        return angle;
    }
}

// Copied from the SEVR VR data example script.
public class SevrData
{
    public const int Head = 2, Left = 4, Right = 8, Stopped = 16, LeftJoints = 32, RightJoints = 64;
    // Fields this parser reads. Newer plugin versions append fields, so longer messages are accepted.
    const int Base = 43, Fields = 61;
    public uint Sequence;
    public long Controller;
    public int Flags;
    public MatrixD HeadPose = MatrixD.Identity;
    public SevrHand LeftHand = new SevrHand(), RightHand = new SevrHand();
    public SevrJoints LeftArm = new SevrJoints(), RightArm = new SevrJoints();
    readonly double[] values = new double[Fields];

    public bool Has(int flag) { return (Flags & flag) != 0; }

    // False for anything that is not a complete SEVR1 message; the previous data is kept.
    public bool TryParse(string argument)
    {
        if (argument == null || !argument.StartsWith("SEVR1 ")) return false;
        string[] fields = argument.Split(' ');
        uint sequence;
        long controller;
        if (fields.Length < Base || !uint.TryParse(fields[1], out sequence) || !long.TryParse(fields[2], out controller)) return false;
        int count = Math.Min(fields.Length, Fields);
        for (int i = 3; i < count; i++)
            if (!double.TryParse(fields[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out values[i])) return false;
        Sequence = sequence;
        Controller = controller;
        Flags = (int)values[3];
        HeadPose = ReadPose(4);
        ReadHand(LeftHand, 11);
        ReadHand(RightHand, 27);
        if (count < Fields) Flags &= ~(LeftJoints | RightJoints);
        else
        {
            ReadJoints(LeftArm, 43);
            ReadJoints(RightArm, 52);
        }
        return true;
    }

    MatrixD ReadPose(int at)
    {
        MatrixD pose = MatrixD.CreateFromQuaternion(new QuaternionD(values[at + 3], values[at + 4], values[at + 5], values[at + 6]));
        pose.Translation = new Vector3D(values[at], values[at + 1], values[at + 2]);
        return pose;
    }

    void ReadHand(SevrHand hand, int at)
    {
        hand.Pose = ReadPose(at);
        hand.Trigger = (float)values[at + 7];
        hand.Grip = (float)values[at + 8];
        hand.Stick = new Vector2((float)values[at + 9], (float)values[at + 10]);
        for (int i = 0; i < 5; i++) hand.Curls[i] = (float)values[at + 11 + i];
    }

    void ReadJoints(SevrJoints arm, int at)
    {
        arm.Shoulder = new Vector3D(values[at], values[at + 1], values[at + 2]);
        arm.Elbow = new Vector3D(values[at + 3], values[at + 4], values[at + 5]);
        arm.Wrist = new Vector3D(values[at + 6], values[at + 7], values[at + 8]);
    }
}

public class SevrHand
{
    public MatrixD Pose = MatrixD.Identity;
    public float Trigger, Grip;
    public Vector2 Stick;
    // Thumb, index, middle, ring, little: 0 open, 1 closed.
    public float[] Curls = new float[5];
}

// Estimated from the head and hand: the shoulder assumes an upright torso facing the seat, the elbow is the VR avatar's.
public class SevrJoints
{
    public Vector3D Shoulder, Elbow, Wrist;
}
