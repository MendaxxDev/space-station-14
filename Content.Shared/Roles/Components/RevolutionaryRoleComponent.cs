using Robust.Shared.GameStates;

namespace Content.Shared.Roles.Components;

/// <summary>
/// Added to mind role entities to tag that they are a Revolutionary.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RevolutionaryRoleComponent : BaseMindRoleComponent
{
    /// <summary>
    /// For headrevs, how many people you have converted.
    /// </summary>
    [DataField, AutoNetworkedField]
    public uint ConvertedCount = 0;

    /// <summary>
    /// When this revolutionary last triggered a codeword effect.
    /// Used to enforce the per-speaker cooldown. Server-side tracking only.
    /// </summary>
    public TimeSpan LastCodewordTime = TimeSpan.Zero;

    /// <summary>
    /// Whether this Head Revolutionary has already used their one-time announcement codeword effect.
    /// Server-side tracking only.
    /// </summary>
    public bool UsedAnnouncementCodeword = false;
}
