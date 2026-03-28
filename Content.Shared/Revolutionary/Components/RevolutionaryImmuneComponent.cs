namespace Content.Shared.Revolutionary.Components;

/// <summary>
/// Marks an entity as immune to revolutionary conversion mechanics.
/// Their LHP cannot be damaged and they cannot be converted.
/// Intended for the Captain, who is always at full loyalty.
/// </summary>
[RegisterComponent]
public sealed partial class RevolutionaryImmuneComponent : Component
{
}
