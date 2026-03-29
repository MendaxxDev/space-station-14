using Robust.Shared.GameStates;

namespace Content.Shared.SurveillanceCamera.Components;

/// <summary>
/// When colliding with a <see cref="CameraIndicatorOnCollideComponent"/>, sets the camera's visual state to "in use".
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CameraIndicatorColliderComponent : Component
{
    [DataField]
    public string FixtureId = "indicatorTrigger";
}
