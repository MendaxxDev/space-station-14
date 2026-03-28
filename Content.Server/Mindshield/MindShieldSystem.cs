using Content.Server.Popups;
using Content.Server.Revolutionary;
using Content.Shared.Implants;
using Content.Shared.Mindshield.Components;
using Content.Shared.Revolutionary.Components;

namespace Content.Server.Mindshield;

/// <summary>
/// System used for adding or removing components with a mindshield implant
/// as well as checking if the implanted is a Rev or Head Rev.
/// </summary>
public sealed class MindShieldSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly RevolutionarySystem _revolutionary = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MindShieldImplantComponent, ImplantImplantedEvent>(OnImplantImplanted);
        SubscribeLocalEvent<MindShieldImplantComponent, ImplantRemovedEvent>(OnImplantRemoved);
    }

    private void OnImplantImplanted(Entity<MindShieldImplantComponent> ent, ref ImplantImplantedEvent ev)
    {
        EnsureComp<MindShieldComponent>(ev.Implanted);
        // A freshly implanted mindshield always starts at full shield health, per spec.
        _revolutionary.ResetShield(ev.Implanted);
        MindShieldRemovalCheck(ev.Implanted, ev.Implant);
    }

    /// <summary>
    /// Checks if the implanted person is a Head Rev. Head Revs are immune to mindshield implants — the implant is destroyed.
    /// Regular revolutionaries are NOT deconverted by mindshield implantation; the mindshield instead
    /// suppresses passive shield recharge while they remain converted.
    /// </summary>
    private void MindShieldRemovalCheck(EntityUid implanted, EntityUid implant)
    {
        if (HasComp<HeadRevolutionaryComponent>(implanted))
        {
            _popupSystem.PopupEntity(Loc.GetString("head-rev-break-mindshield"), implanted);
            QueueDel(implant);
        }
    }

    private void OnImplantRemoved(Entity<MindShieldImplantComponent> ent, ref ImplantRemovedEvent args)
    {
        RemComp<MindShieldComponent>(args.Implanted);
    }
}
