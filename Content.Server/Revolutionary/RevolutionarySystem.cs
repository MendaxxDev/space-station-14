using Content.Shared.Mindshield.Components;
using Content.Shared.Revolutionary;
using Content.Shared.Revolutionary.Components;
using Robust.Shared.GameObjects;

namespace Content.Server.Revolutionary;

/// <summary>
/// Server-side extension of <see cref="SharedRevolutionarySystem"/>.
/// Also provides write-access helpers for <see cref="MindShieldComponent"/> fields
/// (since MindShieldComponent restricts write access to SharedRevolutionarySystem descendants).
/// </summary>
public sealed class RevolutionarySystem : SharedRevolutionarySystem
{
    [Dependency] private readonly LoyaltyHealthSystem _loyaltyHealth = default!;

    public override void Initialize()
    {
        base.Initialize();
    }

    /// <inheritdoc/>
    protected override void OnHeadRevStartup(EntityUid uid, HeadRevolutionaryComponent comp, ComponentStartup args)
    {
        base.OnHeadRevStartup(uid, comp, args);
        _loyaltyHealth.InitHeadRevLoyaltyHealth(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Mindshield passive recharge — stops when the wearer is a revolutionary.
        // This runs here because RevolutionarySystem has write access to MindShieldComponent.
        var query = EntityQueryEnumerator<MindShieldComponent>();
        while (query.MoveNext(out var uid, out var shield))
        {
            if (shield.ShieldHealth >= shield.MaxShieldHealth)
                continue;

            if (HasComp<RevolutionaryComponent>(uid) || HasComp<HeadRevolutionaryComponent>(uid))
                continue;

            shield.ShieldHealth = MathF.Min(shield.ShieldHealth + shield.RechargeRate * frameTime, shield.MaxShieldHealth);
        }
    }

    /// <summary>
    /// Attempts to absorb incoming loyalty damage with the entity's mindshield.
    /// The shield absorbs as much damage as it can; the remaining damage is returned.
    /// </summary>
    /// <param name="target">Entity that owns the shield.</param>
    /// <param name="damage">Incoming loyalty damage to absorb.</param>
    /// <returns>Remaining damage after shield absorption (0 if fully absorbed).</returns>
    public float AbsorbLoyaltyDamage(EntityUid target, float damage)
    {
        if (!TryComp<MindShieldComponent>(target, out var shield) || shield.ShieldHealth <= 0f)
            return damage;

        var absorbed = MathF.Min(shield.ShieldHealth, damage);
        shield.ShieldHealth -= absorbed;
        return damage - absorbed;
    }

    /// <summary>
    /// Resets the mindshield of an entity to full shield health.
    /// Called when a new mindshield is implanted.
    /// </summary>
    public void ResetShield(EntityUid target)
    {
        if (!TryComp<MindShieldComponent>(target, out var shield))
            return;

        shield.ShieldHealth = shield.MaxShieldHealth;
    }
}
