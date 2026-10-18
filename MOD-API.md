# Space Engineers VR mod API

Space Engineers VR offers a small API so Workshop mods can adapt to VR players. The same mod build works with and without the plugin: without it, the API never arrives and the mod keeps its normal behaviour.

## Check first: you may not need it

- **Native use objects already work.** The plugin moves the game's own use detector to the player's finger ray, and a finger poke presses button panel buttons directly. Button panels, terminals, doors and any block whose model uses the game's interactive dummies (for example `detector_panel_button_1`) work in VR with no mod changes. Access checks and multiplayer sync stay native.
- **Text HUD API and Rich HUD Framework HUDs are already adapted.** Their near-camera billboards are pushed out along their view ray to a readable depth. A mod that draws its own billboards near the camera is not adjusted.

Use the API when the mod does its own targeting or input, for example a camera raycast checked on `MyControlsSpace.USE`. In VR that ray comes from the head instead of the hand, and a VR Use press never raises the game's `USE` control.

## Setup

1. Copy [`ModApi/SevrApi.cs`](ModApi/SevrApi.cs) into your mod's scripts unchanged. It compiles under the mod script whitelist.
2. Create one `SevrApi` per session: call `Load()` in `LoadData` and `Unload()` in `UnloadData`.
3. Read members when you need them. Check `IsActive` each time; it is false without the plugin, outside VR, and while VR is restarting.

```csharp
using Sandbox.ModAPI;
using SpaceEngineersVR.Api;
using VRage.Game.Components;
using VRageMath;

[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
public class ElevatorVr : MySessionComponentBase
{
    private readonly SevrApi vr = new SevrApi();

    public override void LoadData()
    {
        vr.Load();
        vr.Use += OnVrUse;
    }

    protected override void UnloadData()
    {
        vr.Use -= OnVrUse;
        vr.Unload();
    }

    // A VR Use press: handle it like the desktop USE key, with the hand's ray.
    private void OnVrUse(bool main, LineD ray)
    {
        TryPressButton(ray);
    }

    // The ray to target with: the hand ray in VR, the camera ray otherwise.
    public LineD AimRay()
    {
        if (vr.IsActive)
        {
            LineD? hand = vr.InteractionRay;
            if (hand.HasValue) return hand.Value;
        }
        MatrixD camera = MyAPIGateway.Session.Camera.WorldMatrix;
        return new LineD(camera.Translation, camera.Translation + camera.Forward * 2.5);
    }

    private void TryPressButton(LineD ray)
    {
        // The mod's existing button hit test and press logic.
    }
}
```

## Members

All poses are in world space and refer to the local player only. Hands are named by role, not side: `true` is the player's main (trigger) hand and `false` the other. The plugin maps left-handed players, so mods need no handedness handling. Members that return a nullable value return `null` when the value is unavailable, for example without the plugin, outside VR or with an untracked controller.

| Member | Type | Meaning |
|---|---|---|
| `Loaded` | `bool` | The plugin has sent the API. |
| `Version` | `int` | API version; 0 until loaded. This document describes version 1. |
| `IsActive` | `bool` | The player is in VR now. |
| `HeadMatrix` | `MatrixD?` | Headset pose. `Forward` is the view direction. |
| `HandMatrix(bool main)` | `MatrixD?` | Controller pose. `Forward` is where the controller points. |
| `FingerTip(bool main)` | `Vector3D?` | The character's index fingertip. For touch or poke interactions, check both hands. |
| `InteractionRay` | `LineD?` | The ray the game's Use currently follows: from the fingertip, limited to the normal use distance measured from the head. `null` when Use has no ray, for example while seated or with a menu open. |
| `Use` | `event Action<bool, LineD>` | Raised on each VR Use press that has an interaction ray (Use button, or the trigger with empty hands), with the hand (true for the main hand) and that ray. It runs before the game's own Use, which still runs; the mod cannot cancel it. Not raised by finger pokes on native buttons. |
| `Ready` | `event Action` | Raised each time the plugin sends the API, including after VR restarts. |

## Rules

- Call members on the game thread only (session component updates, block logic, event handlers).
- Do not register or unregister mod message handlers inside `Ready` or `Use`. The game delivers mod messages by iterating its handler list.
- Multiplayer: the API reports only this machine's player. Send whatever your mod already sends to the server when a button is pressed; the API does not sync anything.
- Desktop players are unaffected: keep the existing camera ray and `USE` key handling, and switch to the API only while `IsActive` is true.

## Protocol

`SevrApi.cs` implements this; it is listed for mods that prefer their own client.

- Channel: `6000296990326476873` (`"SEVR_API"` in ASCII as a big-endian long).
- When a world starts, the plugin registers on the channel and sends a `Dictionary<string, Delegate>`.
- A mod that loads later sends the string `"SpaceEngineersVR.ApiRequest"` on the channel; the plugin sends the dictionary again.
- Dictionary entries: `Version` `Func<int>`, `IsActive` `Func<bool>`, `HeadMatrix` `Func<MatrixD?>`, `HandMatrix` `Func<bool, MatrixD?>`, `FingerTip` `Func<bool, Vector3D?>`, `InteractionRay` `Func<LineD?>`, `RegisterUse` and `UnregisterUse` `Action<Action<bool, LineD>>`. Registering the same callback twice has no effect, and the plugin clears callbacks when VR restarts, so register again each time the dictionary arrives.
- Versioning: new members raise `Version`. Existing members keep their names, types and meaning.
