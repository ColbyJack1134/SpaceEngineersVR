# VR data for programmable blocks

Space Engineers VR sends the pilot's head, hands and estimated arm joints to programmable blocks.

Examples in [`IngameScripts`](IngameScripts):

- [`SevrVrData.cs`](IngameScripts/SevrVrData.cs) shows the data on the block's screen. Copy its `SevrData`, `SevrHand` and `SevrJoints` classes to parse it.
- [`SevrMechArms.cs`](IngameScripts/SevrMechArms.cs) makes a mech's arms copy the pilot's.

## Receiving data

Put `[SEVR]` in the block's name. While a VR player controls a cockpit, seat or remote control on the same grid or its subgrids, the block is run with a `SEVR1` argument and `UpdateType.Mod`.

```csharp
SevrData vr = new SevrData();

public void Main(string argument, UpdateType updateSource)
{
    if ((updateSource & UpdateType.Mod) != 0)
    {
        if (vr.TryParse(argument)) { /* use vr.HeadPose, vr.LeftHand, vr.RightArm ... */ }
        return;
    }
    // Your own arguments, for example "arms on".
}
```

- About 30 messages a second in single player or when hosting, 20 from a client.
- Unchanged data is resent once a second. Treat data older than 1.5 s as stale.
- When the pilot leaves, the block gets one message with the `Stopped` flag.
- Two VR pilots on one grid send interleaved messages. `Controller` tells them apart.
- Each `[SEVR]` block gets its own copy. To feed several scripts, tag one and pass the message on with `TryRun`.

## Message format

Space-separated fields, `.` decimal separator. Fields never move. New ones are appended, so accept longer messages.

| Index | Field |
|---|---|
| 0 | `SEVR1` |
| 1 | Sequence (per sender, wraps) |
| 2 | Controller entity id |
| 3 | Flags |
| 4-10 | Head: position xyz, quaternion xyzw |
| 11-26 | Left hand: pose (7), trigger, grip, stick xy, curls thumb to little |
| 27-42 | Right hand |
| 43-51 | Left arm: shoulder, elbow, wrist xyz |
| 52-60 | Right arm |

Flags: 2 head tracked, 4 left hand, 8 right hand, 16 stopped, 32 left arm joints, 64 right arm joints.

- Metres. Trigger, grip and curls 0 to 1, sticks -1 to 1.
- Poses are relative to the pilot's recentred eye, in the controlled block's axes: x right, y up, z backward. Use the block's `WorldMatrix` for world coordinates.
- Hand poses follow the OpenXR grip convention: forward along a pointing index finger, x through the palm.
- Joints are estimated from the head and hands, assuming an upright body facing the seat's way.

## Mech arms script

1. Put `[SEVR]` in the block's name.
2. Tag arm joints `[SEVR L shoulder]`, `[SEVR L elbow]`, `[SEVR L wrist]` (`R` for right) and head joints `[SEVR head]`. Any number per role, mounted either way.
3. Pose the arms and run `calibrate`.
4. Run `arms` to follow and again to hold.

Switches are `arms`, `head`, `lean` (needs Mech Control Script) and `headturn`. Alone they toggle. Add `on` or `off` to set them.

Settings are in Custom Data under `[SEVR Arms]`. Run `reload` after editing.
