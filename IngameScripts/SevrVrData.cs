// Space Engineers VR: programmable block data example.
//
// Put [SEVR] in this programmable block's name. While a VR player controls a cockpit or remote control
// on the same grid (subgrids included), the plugin runs this block with the player's head and hand data.
// This example shows the latest data on the block's screen. Copy SevrData, SevrHand and SevrJoints into your own script.
//
// Poses are in metres relative to the player's recentered seated eye position, in the controlled block's axes:
// x right, y up, z backward (forward is -z). Hands are physical left and right.
// VR data arrives with UpdateType.Mod; anything else, such as a toolbar "Run" with an argument, is your script's own command.

SevrData vr = new SevrData();
IMyTextSurface screen;
double clock, received = -1;

public Program()
{
    screen = Me.GetSurface(0);
    screen.ContentType = ContentType.TEXT_AND_IMAGE;
    screen.Font = "Monospace";
    screen.FontSize = 0.55f;
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
}

public void Main(string argument, UpdateType updateSource)
{
    clock += Runtime.TimeSinceLastRun.TotalSeconds;
    if ((updateSource & UpdateType.Mod) != 0)
    {
        if (vr.TryParse(argument)) received = clock;
        return;
    }
    if ((updateSource & UpdateType.Update10) != 0) Show();
}

void Show()
{
    var text = new StringBuilder();
    if (received < 0)
    {
        text.AppendLine("Waiting for VR data.");
        text.AppendLine("Control a cockpit on this grid in VR.");
    }
    else
    {
        text.AppendLine("SEVR #" + vr.Sequence + "  " + (clock - received).ToString("0.0") + " s ago" + (vr.Has(SevrData.Stopped) ? "  stopped" : ""));
        text.AppendLine("Head  " + (vr.Has(SevrData.Head) ? Pose(vr.HeadPose) : "not tracked"));
        Hand(text, "Left ", vr.LeftHand, vr.Has(SevrData.Left));
        Hand(text, "Right", vr.RightHand, vr.Has(SevrData.Right));
        Joints(text, "Left ", vr.LeftArm, vr.Has(SevrData.LeftJoints));
        Joints(text, "Right", vr.RightArm, vr.Has(SevrData.RightJoints));
    }
    screen.WriteText(text);
}

static void Hand(StringBuilder text, string name, SevrHand hand, bool tracked)
{
    text.AppendLine(name + " " + (tracked ? Pose(hand.Pose) : "not tracked"));
    text.AppendLine("  trigger " + hand.Trigger.ToString("0.00") + "  grip " + hand.Grip.ToString("0.00") +
        "  stick " + hand.Stick.X.ToString("0.00") + " " + hand.Stick.Y.ToString("0.00"));
    text.Append("  curls");
    foreach (float curl in hand.Curls) text.Append(" " + curl.ToString("0.00"));
    text.AppendLine();
}

static void Joints(StringBuilder text, string name, SevrJoints arm, bool estimated)
{
    text.AppendLine(name + " arm " + (estimated ? "shoulder " + Point(arm.Shoulder) + "  elbow " + Point(arm.Elbow) : "not estimated"));
}

static string Point(Vector3D p)
{
    return p.X.ToString("0.00") + " " + p.Y.ToString("0.00") + " " + p.Z.ToString("0.00");
}

static string Pose(MatrixD pose)
{
    return Point(pose.Translation) + "  fwd " + Point(pose.Forward);
}

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
