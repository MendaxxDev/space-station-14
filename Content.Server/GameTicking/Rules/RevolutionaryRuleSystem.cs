using Content.Server.Administration.Logs;
using Content.Server.Antag;
using Content.Server.EUI;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Mind;
using Content.Server.Popups;
using Content.Server.Revolutionary;
using Content.Server.Revolutionary.Components;
using Content.Server.Roles;
using Content.Server.RoundEnd;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Cuffs.Components;
using Content.Shared.Database;
using Content.Shared.Flash;
using Content.Shared.GameTicking.Components;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Mind.Components;
using Content.Shared.Mindshield.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Revolutionary.Components;
using Content.Shared.Roles.Components;
using Content.Server.Shuttles.Components;
using Content.Shared.Stunnable;
using Content.Shared.Zombies;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.GameTicking.Rules;

/// <summary>
/// Where all the main stuff for Revolutionaries happens (Assigning Head Revs, Command on station, and checking for the game to end.)
/// </summary>
public sealed class RevolutionaryRuleSystem : GameRuleSystem<RevolutionaryRuleComponent>
{
    [Dependency] private readonly AntagSelectionSystem _antag = default!;
    [Dependency] private readonly EmergencyShuttleSystem _emergencyShuttle = default!;
    [Dependency] private readonly EuiManager _euiMan = default!;
    [Dependency] private readonly IAdminLogManager _adminLogManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly LoyaltyHealthSystem _loyaltyHealth = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly RoleSystem _role = default!;
    [Dependency] private readonly RoundEndSystem _roundEnd = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly StationSystem _stationSystem = default!;

    //Used in OnPostFlash, no reference to the rule component is available
    public readonly ProtoId<NpcFactionPrototype> RevolutionaryNpcFaction = "Revolutionary";
    public readonly ProtoId<NpcFactionPrototype> RevPrototypeId = "Rev";

    // 91 LHP: enough to push a full-health target to ~9 (Convertable), and fully drains a 100-LHP mindshield.
    private const float FlashLoyaltyDamage = 91f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CommandStaffComponent, MobStateChangedEvent>(OnCommandMobStateChanged);

        SubscribeLocalEvent<HeadRevolutionaryComponent, AfterFlashedEvent>(OnPostFlash);
        SubscribeLocalEvent<HeadRevolutionaryComponent, MobStateChangedEvent>(OnHeadRevMobStateChanged);

        SubscribeLocalEvent<RevolutionaryRoleComponent, GetBriefingEvent>(OnGetBriefing);

        // Ensure late-joining humanoids also receive a LoyaltyHealthComponent during an active rev round.
        SubscribeLocalEvent<HumanoidProfileComponent, ComponentStartup>(OnHumanoidStartup);
    }

    private void OnHumanoidStartup(EntityUid uid, HumanoidProfileComponent comp, ComponentStartup args)
    {
        // Only add LoyaltyHealthComponent if a Revolutionary game rule is currently active.
        var rules = EntityQueryEnumerator<RevolutionaryRuleComponent, GameRuleComponent>();
        while (rules.MoveNext(out var ruleUid, out _, out var gameRule))
        {
            if (!GameTicker.IsGameRuleActive(ruleUid, gameRule))
                continue;

            EnsureComp<LoyaltyHealthComponent>(uid);
            return;
        }
    }

    protected override void Started(EntityUid uid, RevolutionaryRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        component.CommandCheck = _timing.CurTime + component.TimerWait;

        // Give all existing humanoid crew a LoyaltyHealthComponent so they can be converted via LHP.
        var humanoids = EntityQueryEnumerator<HumanoidProfileComponent, MobStateComponent>();
        while (humanoids.MoveNext(out var crewUid, out _, out _))
        {
            EnsureComp<LoyaltyHealthComponent>(crewUid);
        }

    }

    protected override void ActiveTick(EntityUid uid, RevolutionaryRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);
        if (component.CommandCheck <= _timing.CurTime)
        {
            component.CommandCheck = _timing.CurTime + component.TimerWait;

            if (CheckCommandLose())
            {
                _roundEnd.DoRoundEndBehavior(RoundEndBehavior.ShuttleCall, component.ShuttleCallTime);
                GameTicker.EndGameRule(uid, gameRule);
            }
        }
    }

    protected override void AppendRoundEndText(EntityUid uid,
        RevolutionaryRuleComponent component,
        GameRuleComponent gameRule,
        ref RoundEndTextAppendEvent args)
    {
        base.AppendRoundEndText(uid, component, gameRule, ref args);

        var revsLost = CheckRevsLose();
        var commandLost = CheckCommandLose();

        // Determine shuttle-based victory tier
        var outcomeKey = DetermineVictoryOutcome(revsLost, commandLost);
        args.AddLine(Loc.GetString(outcomeKey));

        var sessionData = _antag.GetAntagIdentifiers(uid);
        args.AddLine(Loc.GetString("rev-headrev-count", ("initialCount", sessionData.Count)));
        foreach (var (mind, data, name) in sessionData)
        {
            _role.MindHasRole<RevolutionaryRoleComponent>(mind, out var role);
            var count = CompOrNull<RevolutionaryRoleComponent>(role)?.ConvertedCount ?? 0;

            args.AddLine(Loc.GetString("rev-headrev-name-user",
                ("name", name),
                ("username", data.UserName),
                ("count", count)));
        }
        args.AddLine("");
    }

    /// <summary>
    /// Determines the victory outcome string key using shuttle-based criteria.
    /// Checks who is aboard the emergency shuttle and what fraction of Command was converted.
    /// Falls back to the old binary check if no shuttle data is available.
    /// </summary>
    private string DetermineVictoryOutcome(bool revsLost, bool commandLost)
    {
        // If all Head Revs are dead, revolution failed regardless of shuttle contents
        if (revsLost && !commandLost)
            return "rev-lost";

        // Count Command staff: converted vs loyal, and who made it onto the shuttle
        var totalCommand = 0;
        var convertedCommand = 0;
        var loyalCommandOnShuttle = 0;

        var commandQuery = EntityQueryEnumerator<CommandStaffComponent, MobStateComponent>();
        while (commandQuery.MoveNext(out var cmdUid, out _, out var mobState))
        {
            if (mobState.CurrentState == MobState.Dead || mobState.CurrentState == MobState.Invalid)
                continue;

            totalCommand++;

            var isConverted = HasComp<RevolutionaryComponent>(cmdUid) || HasComp<HeadRevolutionaryComponent>(cmdUid);
            if (isConverted)
            {
                convertedCommand++;
                continue;
            }

            // Check if this loyal Command member is aboard the emergency shuttle unrestrained
            var onShuttle = HasComp<EmergencyShuttleComponent>(Transform(cmdUid).GridUid);
            var isRestrained = TryComp<CuffableComponent>(cmdUid, out var cuffs) && cuffs.CuffedHandCount > 0;

            if (onShuttle && !isRestrained)
                loyalCommandOnShuttle++;
        }

        // If shuttle data is not meaningful (e.g. shuttle wasn't used), fall back to basic outcome
        if (totalCommand == 0)
            return commandLost ? "rev-won" : "rev-lost";

        var conversionFraction = (float) convertedCommand / totalCommand;
        var majorityConverted = conversionFraction > 0.5f;

        if (loyalCommandOnShuttle == 0)
        {
            return majorityConverted
                ? "rev-major-victory"
                : "rev-minor-victory";
        }
        else
        {
            return majorityConverted
                ? "crew-minor-victory"
                : "crew-major-victory";
        }
    }

    private void OnGetBriefing(EntityUid uid, RevolutionaryRoleComponent comp, ref GetBriefingEvent args)
    {
        var ent = args.Mind.Comp.OwnedEntity;
        var head = HasComp<HeadRevolutionaryComponent>(ent);
        args.Append(Loc.GetString(head ? "head-rev-briefing" : "rev-briefing"));
    }

    /// <summary>
    /// Called when a Head Rev uses a flash in melee.
    /// Instead of instantly converting, applies a large amount of loyalty damage.
    /// This will convert the target if they have no mindshield and enough damage is dealt.
    /// </summary>
    private void OnPostFlash(EntityUid uid, HeadRevolutionaryComponent comp, ref AfterFlashedEvent ev)
    {
        if (uid != ev.User || !ev.Melee)
            return;

        if (!ev.Target.IsValid())
            return;

        EnsureComp<LoyaltyHealthComponent>(ev.Target);

        var converted = _loyaltyHealth.ApplyLoyaltyDamage(ev.Target, FlashLoyaltyDamage, canConvert: true);

        if (converted && ev.User != null)
        {
            _adminLogManager.Add(LogType.Mind,
                LogImpact.Medium,
                $"{ToPrettyString(ev.User.Value)} converted {ToPrettyString(ev.Target)} into a Revolutionary via flash.");

            if (_mind.TryGetMind(ev.User.Value, out var revMindId, out _))
            {
                if (_role.MindHasRole<RevolutionaryRoleComponent>(revMindId, out var role))
                {
                    role.Value.Comp2.ConvertedCount++;
                    Dirty(role.Value.Owner, role.Value.Comp2);
                }
            }
        }
    }

    //TODO: Enemies of the revolution
    private void OnCommandMobStateChanged(EntityUid uid, CommandStaffComponent comp, MobStateChangedEvent ev)
    {
        if (ev.NewMobState == MobState.Dead || ev.NewMobState == MobState.Invalid)
            CheckCommandLose();
    }

    /// <summary>
    /// Checks if all of command is dead and if so will remove all sec and command jobs if there were any left.
    /// </summary>
    private bool CheckCommandLose()
    {
        var commandList = new List<EntityUid>();

        var heads = AllEntityQuery<CommandStaffComponent>();
        while (heads.MoveNext(out var id, out _))
        {
            commandList.Add(id);
        }

        return IsGroupDetainedOrDead(commandList, true, true, true);
    }

    private void OnHeadRevMobStateChanged(EntityUid uid, HeadRevolutionaryComponent comp, MobStateChangedEvent ev)
    {
        if (ev.NewMobState == MobState.Dead || ev.NewMobState == MobState.Invalid)
            CheckRevsLose();
    }

    /// <summary>
    /// Checks if all the Head Revs are dead and if so will deconvert all regular revs.
    /// </summary>
    private bool CheckRevsLose()
    {
        var stunTime = TimeSpan.FromSeconds(4);
        var headRevList = new List<EntityUid>();

        var headRevs = AllEntityQuery<HeadRevolutionaryComponent, MobStateComponent>();
        while (headRevs.MoveNext(out var uid, out _, out _))
        {
            headRevList.Add(uid);
        }

        // If no Head Revs are alive all normal Revs will lose their Rev status and rejoin Nanotrasen
        // Cuffing Head Revs is not enough - they must be killed.
        if (IsGroupDetainedOrDead(headRevList, false, false, false))
        {
            var rev = AllEntityQuery<RevolutionaryComponent, MindContainerComponent>();
            while (rev.MoveNext(out var uid, out _, out var mc))
            {
                if (HasComp<HeadRevolutionaryComponent>(uid))
                    continue;

                _npcFaction.RemoveFaction(uid, RevolutionaryNpcFaction);
                _stun.TryUpdateParalyzeDuration(uid, stunTime);
                RemCompDeferred<RevolutionaryComponent>(uid);
                _popup.PopupEntity(Loc.GetString("rev-break-control", ("name", Identity.Entity(uid, EntityManager))), uid);
                _adminLogManager.Add(LogType.Mind, LogImpact.Medium, $"{ToPrettyString(uid)} was deconverted due to all Head Revolutionaries dying.");

                if (!_mind.TryGetMind(uid, out var mindId, out var mind, mc))
                    continue;

                // remove their antag role
                _role.MindRemoveRole<RevolutionaryRoleComponent>(mindId);

                // make it very obvious to the rev they've been deconverted since
                // they may not see the popup due to antag and/or new player tunnel vision
                if (_player.TryGetSessionById(mind.UserId, out var session))
                    _euiMan.OpenEui(new DeconvertedEui(), session);
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Will take a group of entities and check if these entities are alive, dead or cuffed.
    /// </summary>
    /// <param name="list">The list of the entities</param>
    /// <param name="checkOffStation">Bool for if you want to check if someone is in space and consider them missing in action. (Won't check when emergency shuttle arrives just in case)</param>
    /// <param name="countCuffed">Bool for if you don't want to count cuffed entities.</param>
    /// <param name="countRevolutionaries">Bool for if you want to count revolutionaries.</param>
    /// <returns></returns>
    private bool IsGroupDetainedOrDead(List<EntityUid> list, bool checkOffStation, bool countCuffed, bool countRevolutionaries)
    {
        var gone = 0;

        foreach (var entity in list)
        {
            if (TryComp<CuffableComponent>(entity, out var cuffed) && cuffed.CuffedHandCount > 0 && countCuffed)
            {
                gone++;
                continue;
            }

            if (TryComp<MobStateComponent>(entity, out var state))
            {
                if (state.CurrentState == MobState.Dead || state.CurrentState == MobState.Invalid)
                {
                    gone++;
                    continue;
                }

                if (checkOffStation && _stationSystem.GetOwningStation(entity) == null && !_emergencyShuttle.EmergencyShuttleArrived)
                {
                    gone++;
                    continue;
                }
            }
            //If they don't have the MobStateComponent they might as well be dead.
            else
            {
                gone++;
                continue;
            }

            if ((HasComp<RevolutionaryComponent>(entity) || HasComp<HeadRevolutionaryComponent>(entity)) && countRevolutionaries)
            {
                gone++;
                continue;
            }
        }

        return gone == list.Count || list.Count == 0;
    }

}
