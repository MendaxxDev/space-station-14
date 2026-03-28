namespace Content.Shared.Revolutionary.Components;

/// <summary>
/// When worn, passively restores the wearer's Loyalty Health Points (LHP) over time if they are a Revolutionary.
/// </summary>
[RegisterComponent]
public sealed partial class RevDeconversionEquipmentComponent : Component
{
    /// <summary>
    /// LHP restored per second while this item is worn by a Revolutionary.
    /// </summary>
    [DataField]
    public float HealRate = 0.5f;
}
