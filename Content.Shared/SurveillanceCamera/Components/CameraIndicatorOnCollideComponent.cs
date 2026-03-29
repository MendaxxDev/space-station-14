using Robust.Shared.GameStates;

namespace Content.Shared.SurveillanceCamera.Components;

/// <summary>
/// Marks a camera as showing "in use" visuals when collided with by a <see cref="CameraIndicatorColliderComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CameraIndicatorOnCollideComponent : Component
{
    [ViewVariables]
    public int ActiveColliders;
}