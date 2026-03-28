using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Revolutionary.Components;

/// <summary>
/// Tracks an entity's Loyalty Health Points (LHP).
/// LHP normally ranges from <see cref="MinLoyaltyHealth"/> to <see cref="MaxLoyaltyHealth"/>.
/// Non-revolutionaries cannot drop below 0 LHP; when below <see cref="ConvertableThreshold"/> they enter a
/// "Convertable" state where certain Revolutionary actions can fully convert them.
/// When converted, LHP is set to <see cref="PostConversionLoyalty"/>. A Revolutionary whose LHP rises
/// back to 0 or above is automatically deconverted.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class LoyaltyHealthComponent : Component
{
    /// <summary>
    /// Current loyalty health points.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float LoyaltyHealth = 100f;

    /// <summary>
    /// Maximum loyalty health points. Normally 100 for all crew.
    /// </summary>
    [DataField]
    public float MaxLoyaltyHealth = 100f;

    /// <summary>
    /// Minimum loyalty health points. -100 for regular crew, -500 for Head Revolutionaries.
    /// </summary>
    [DataField]
    public float MinLoyaltyHealth = -100f;

    /// <summary>
    /// LHP below which a non-revolutionary enters the Convertable state.
    /// </summary>
    [DataField]
    public float ConvertableThreshold = 10f;

    /// <summary>
    /// LHP set on the entity when they are first converted to a Revolutionary.
    /// </summary>
    [DataField]
    public float PostConversionLoyalty = -25f;

    /// <summary>
    /// LHP set on the entity when they are deconverted (returned to crew).
    /// </summary>
    [DataField]
    public float PostDeconversionLoyalty = 25f;

    /// <summary>
    /// Rate of passive LHP regeneration per second for non-revolutionary crew.
    /// Does not apply to Revolutionaries (they do not passively heal LHP).
    /// </summary>
    [DataField]
    public float RegenRate = 0.1f;

    /// <summary>
    /// How long after taking loyalty damage before passive regeneration resumes.
    /// </summary>
    [DataField]
    public TimeSpan RegenCooldown = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Time when loyalty damage was last taken. Used to determine when regen resumes.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan LastDamageTime = TimeSpan.Zero;

    /// <summary>
    /// Time when the death loyalty burst was last triggered for this entity.
    /// Used to enforce the 120-second cooldown on death bursts.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan LastDeathBurstTime = TimeSpan.Zero;

    /// <summary>
    /// The entity that most recently dealt damage to this entity.
    /// Used by death mechanics to determine burst direction: crew killer → damage burst; rev killer → heal burst.
    /// </summary>
    [DataField]
    public EntityUid? LastDamageSource;
}
