namespace Content.Shared.Revolutionary.Components;

/// <summary>
/// When worn in the head slot by a Revolutionary, their codeword LHP damage is multiplied.
/// Crafted from a bandana and a sharp object.
/// </summary>
[RegisterComponent]
public sealed partial class RevolutionaryHeadbandComponent : Component
{
    /// <summary>
    /// Multiplier applied to codeword LHP damage when the wearer is a Revolutionary.
    /// </summary>
    [DataField]
    public float DamageMultiplier = 1.5f;
}
