using Content.Shared.Revolutionary;
using Robust.Shared.GameStates;
using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Mindshield.Components;

/// <summary>
/// Grants the wearer a shield that absorbs loyalty health (LHP) damage before it reaches the wearer's own LHP.
/// The shield recharges over time, but stops recharging if the wearer is converted to the Revolution.
/// Does not prevent conversion outright — sustained effort can drain both the shield and the wearer's LHP.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(SharedRevolutionarySystem))]
public sealed partial class MindShieldComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public ProtoId<SecurityIconPrototype> MindShieldStatusIcon = "MindShieldIcon";

    /// <summary>
    /// Current shield LHP. Absorbs incoming loyalty damage before the wearer's own LHP is affected.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float ShieldHealth = 100f;

    /// <summary>
    /// Maximum shield LHP. A freshly implanted mindshield always starts at this value.
    /// </summary>
    [DataField]
    public float MaxShieldHealth = 100f;

    /// <summary>
    /// Passive shield recharge rate in LHP per second. Stops working when the wearer is a Revolutionary.
    /// </summary>
    [DataField]
    public float RechargeRate = 0.2f;
}
