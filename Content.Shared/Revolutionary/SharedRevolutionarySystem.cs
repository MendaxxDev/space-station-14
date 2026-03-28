using Content.Shared.Antag;
using Content.Shared.Mindshield.Components;
using Content.Shared.Revolutionary.Components;
using Robust.Shared.GameStates;
using Robust.Shared.Player;

namespace Content.Shared.Revolutionary;

public abstract class SharedRevolutionarySystem : EntitySystem
{

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MindShieldComponent, MapInitEvent>(MindShieldImplanted);
        SubscribeLocalEvent<RevolutionaryComponent, ComponentGetStateAttemptEvent>(OnRevCompGetStateAttempt);
        SubscribeLocalEvent<HeadRevolutionaryComponent, ComponentGetStateAttemptEvent>(OnRevCompGetStateAttempt);
        SubscribeLocalEvent<RevolutionaryComponent, ComponentStartup>(DirtyRevComps);
        SubscribeLocalEvent<HeadRevolutionaryComponent, ComponentStartup>(OnHeadRevStartup);
        SubscribeLocalEvent<ShowAntagIconsComponent, ComponentStartup>(DirtyRevComps);
    }

    /// <summary>
    /// When the mindshield is implanted in a Head Rev, the component is immediately removed (Head Revs are immune).
    /// Regular revolutionaries are no longer deconverted by mindshield implantation — instead the mindshield
    /// suppresses their passive loyalty health regeneration (handled by LoyaltyHealthSystem on the server).
    /// </summary>
    private void MindShieldImplanted(EntityUid uid, MindShieldComponent comp, MapInitEvent init)
    {
        if (HasComp<HeadRevolutionaryComponent>(uid))
        {
            RemCompDeferred<MindShieldComponent>(uid);
        }
    }

    /// <summary>
    /// Called when <see cref="HeadRevolutionaryComponent"/> starts up on an entity.
    /// Override in server-side systems to apply server-only HeadRev initialization logic.
    /// </summary>
    protected virtual void OnHeadRevStartup(EntityUid uid, HeadRevolutionaryComponent comp, ComponentStartup args)
    {
        DirtyRevComps(uid, comp, args);
    }

    /// <summary>
    /// Determines if a HeadRev component should be sent to the client.
    /// </summary>
    private void OnRevCompGetStateAttempt(EntityUid uid, HeadRevolutionaryComponent comp, ref ComponentGetStateAttemptEvent args)
    {
        args.Cancelled = !CanGetState(args.Player);
    }

    /// <summary>
    /// Determines if a Rev component should be sent to the client.
    /// </summary>
    private void OnRevCompGetStateAttempt(EntityUid uid, RevolutionaryComponent comp, ref ComponentGetStateAttemptEvent args)
    {
        args.Cancelled = !CanGetState(args.Player);
    }

    /// <summary>
    /// The criteria that determine whether a Rev/HeadRev component should be sent to a client.
    /// </summary>
    /// <param name="player"> The Player the component will be sent to.</param>
    /// <returns></returns>
    private bool CanGetState(ICommonSession? player)
    {
        //Apparently this can be null in replays so I am just returning true.
        if (player?.AttachedEntity is not {} uid)
            return true;

        if (HasComp<RevolutionaryComponent>(uid) || HasComp<HeadRevolutionaryComponent>(uid))
            return true;

        return HasComp<ShowAntagIconsComponent>(uid);
    }
    /// <summary>
    /// Dirties all the Rev components so they are sent to clients.
    ///
    /// We need to do this because if a rev component was not earlier sent to a client and for example the client
    /// becomes a rev then we need to send all the components to it. To my knowledge there is no way to do this on a
    /// per client basis so we are just dirtying all the components.
    /// </summary>
    private void DirtyRevComps<T>(EntityUid someUid, T someComp, ComponentStartup ev)
    {
        var revComps = AllEntityQuery<RevolutionaryComponent>();
        while (revComps.MoveNext(out var uid, out var comp))
        {
            Dirty(uid, comp);
        }

        var headRevComps = AllEntityQuery<HeadRevolutionaryComponent>();
        while (headRevComps.MoveNext(out var uid, out var comp))
        {
            Dirty(uid, comp);
        }
    }
}
