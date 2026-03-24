using Fusion;
using UnityEngine;

// Network input payload used by Fusion.
// Clients fill this in OnInput, and the server reads it in FixedUpdateNetwork.
public struct PlayerNetworkInput : INetworkInput
{
    // WASD / stick movement input.
    public Vector2 Move;
    public float CameraYaw;

    // Jump.
    public NetworkBool Jump;

    // Short left click: use held item, or punch if none is held.
    public NetworkBool Punch;

    // Hold left click: continuous-use item state, e.g. flamethrower.
    public NetworkBool PrimaryUseHold;

    // Drop held item.
    public NetworkBool Drop;

    // Short right click: throw held target, airborne flying kick, or grounded secondary fallback (pickup / kick).
    public NetworkBool Throw;

    // Hold left click past threshold: keep grab intent active.
    public NetworkBool LeftGrabHold;

    // Hold right click past threshold: keep right-hand grab active.
    public NetworkBool RightGrabHold;

    // Middle mouse button.
    public NetworkBool Headbutt;

    // Sprint.
    public NetworkBool Sprint;
}
