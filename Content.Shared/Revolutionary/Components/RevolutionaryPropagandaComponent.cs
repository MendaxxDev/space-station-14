namespace Content.Shared.Revolutionary.Components;

/// <summary>
/// When added to an entity, it will periodically drain (or heal, if loyal) the loyalty health of nearby
/// non-revolutionary crew. Only a limited number of overlapping posters can stack their effect on a single target per tick.
/// </summary>
[RegisterComponent]
public sealed partial class RevolutionaryPropagandaComponent : Component
{
    /// <summary>
    /// How much loyalty health is drained (or healed, if <see cref="IsLoyal"/>) per second.
    /// </summary>
    [DataField]
    public float DrainRate = 1f;

    /// <summary>
    /// Radius within which non-revolutionary crew are affected.
    /// </summary>
    [DataField]
    public float Range = 3f;

    /// <summary>
    /// How often (in seconds) to apply the loyalty drain tick.
    /// </summary>
    [DataField]
    public float TickInterval = 1f;

    /// <summary>
    /// Accumulated time since last tick.
    /// </summary>
    public float AccumulatedTime = 0f;

    /// <summary>
    /// If true, this poster heals nearby crew's LHP instead of draining it.
    /// Used for NanoTrasen loyalty propaganda ("loyal posters").
    /// </summary>
    [DataField]
    public bool IsLoyal = false;

    /// <summary>
    /// If true, targets must have line-of-sight to this poster to be affected.
    /// </summary>
    [DataField]
    public bool RequiresLoS = true;

    /// <summary>
    /// LHP burst applied to nearby crew when this poster is destroyed.
    /// Positive value = damage (for loyal posters being destroyed by revs).
    /// Negative value = heal (for rev posters being destroyed by crew).
    /// Zero = no destruction burst.
    /// </summary>
    [DataField]
    public float DestructionBurst = 0f;
}