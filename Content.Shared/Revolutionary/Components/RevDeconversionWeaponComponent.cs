namespace Content.Shared.Revolutionary.Components;

/// <summary>
/// When a weapon with this component hits a Revolutionary,
/// the target recovers <see cref="DeconversionAmount"/> LHP.
/// Used on the stun baton to allow security to deconvert revolutionaries.
/// </summary>
[RegisterComponent]
public sealed partial class RevDeconversionWeaponComponent : Component
{
    /// <summary>
    /// How much LHP to restore to a revolutionary per hit.
    /// </summary>
    [DataField]
    public float DeconversionAmount = 10f;
}
