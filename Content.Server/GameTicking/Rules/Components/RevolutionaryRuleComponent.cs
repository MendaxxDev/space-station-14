using Content.Server.Codewords;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.GameTicking.Rules.Components;

/// <summary>
/// Component for the RevolutionaryRuleSystem that stores info about winning/losing, player counts required for starting, as well as prototypes for Revolutionaries and their gear.
/// All tunable numeric values for the Loyalty Health system live here so they can be tweaked in YAML.
/// </summary>
[RegisterComponent]
public sealed partial class RevolutionaryRuleComponent : Component
{
    /// <summary>
    /// When the round will end if all the command are dead (in case they are in space).
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan CommandCheck;

    /// <summary>
    /// The amount of time between each command-loss check.
    /// </summary>
    [DataField]
    public TimeSpan TimerWait = TimeSpan.FromSeconds(20);

    /// <summary>
    /// The time it takes after the last head rev is killed for the shuttle to arrive.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan ShuttleCallTime = TimeSpan.FromMinutes(5);

    // --- Codeword faction IDs (three tiers) ---

    [DataField]
    public ProtoId<CodewordFactionPrototype> LowCodewordFaction = "RevolutionaryLow";

    [DataField]
    public ProtoId<CodewordFactionPrototype> MidCodewordFaction = "RevolutionaryMid";

    [DataField]
    public ProtoId<CodewordFactionPrototype> HighCodewordFaction = "RevolutionaryHigh";

    // --- Codeword damage per tier ---

    [DataField]
    public float LowCodewordDamage = 10f;

    [DataField]
    public float MidCodewordDamage = 20f;

    [DataField]
    public float HighCodewordDamage = 30f;

    /// <summary>Radio codeword damage multiplier: only 20% effective when spoken over radio.</summary>
    [DataField]
    public float RadioDamageMultiplier = 0.2f;

    /// <summary>Range (in tiles) within which crew hear a codeword and take LHP damage.</summary>
    [DataField]
    public float CodewordRange = 7f;

    /// <summary>Per-speaker cooldown between codeword damage triggers.</summary>
    [DataField]
    public TimeSpan CodewordCooldown = TimeSpan.FromSeconds(10);

    // --- Propaganda poster values ---

    /// <summary>Maximum number of overlapping propaganda posters that stack their effect on a single target per tick.</summary>
    [DataField]
    public int MaxPosterStacks = 4;

    /// <summary>Flat codeword damage multiplier when the speaker has at least one revolutionary poster nearby with LoS.</summary>
    [DataField]
    public float PosterCodewordMultiplier = 1.25f;

    /// <summary>Radius of the LHP burst applied when a propaganda poster is destroyed.</summary>
    [DataField]
    public float PosterDestructionBurstRange = 5f;

    // --- Death mechanics ---

    [DataField]
    public float DeathBurstDamage = 15f;

    [DataField]
    public float DeathBurstRange = 8f;

    [DataField]
    public float DeathAuraRange = 4f;

    [DataField]
    public float DeathAuraRate = 0.1f;

    /// <summary>Cooldown on the per-entity death LHP burst to prevent spam.</summary>
    [DataField]
    public TimeSpan DeathBurstCooldown = TimeSpan.FromSeconds(120);

    // --- Deconversion ---

    /// <summary>How much LHP per second a cuffed revolutionary passively recovers.</summary>
    [DataField]
    public float CuffedDeconversionRate = 0.5f;

    // --- Flash conversion ---

    /// <summary>LHP damage dealt by a Head Rev flash. 91 is enough to push a full-health target to Convertable and fully drain a 100-LHP mindshield.</summary>
    [DataField]
    public float FlashLoyaltyDamage = 91f;
}
