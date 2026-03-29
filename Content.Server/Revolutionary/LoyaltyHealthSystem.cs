using Content.Server.Administration.Logs;
using Content.Server.Antag;
using Content.Server.Codewords;
using Content.Server.Communications;
using Content.Server.EUI;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Mind;
using Content.Server.Popups;
using Content.Server.Roles;
using Content.Shared.Chat;
using Content.Shared.Cuffs.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Revolutionary.Components;
using Content.Shared.Roles.Components;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Zombies;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Revolutionary;

/// <summary>
/// Handles Loyalty Health Point (LHP) tracking, passive regeneration, codeword-based conversion,
/// propaganda effects, and violence-triggered loyalty changes for the Revolutionary gamemode.
/// </summary>
public sealed class LoyaltyHealthSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly AntagSelectionSystem _antag = default!;
    [Dependency] private readonly CodewordSystem _codewords = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly EuiManager _euiMan = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly RevolutionarySystem _revolutionary = default!;
    [Dependency] private readonly RoleSystem _role = default!;
    [Dependency] private readonly SharedInteractionSystem _interact = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;

    /// <summary>The NPC faction id for all revolutionaries.</summary>
    public static readonly ProtoId<NpcFactionPrototype> RevolutionaryFaction = "Revolutionary";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LoyaltyHealthComponent, MobStateChangedEvent>(OnEntityDied);
        SubscribeLocalEvent<LoyaltyHealthComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<RevDeconversionWeaponComponent, MeleeHitEvent>(OnDeconversionWeaponHit);
        SubscribeLocalEvent<EntitySpokeEvent>(OnEntitySpoke);
        SubscribeLocalEvent<CommunicationConsoleAnnouncementEvent>(OnAnnouncementSent);
        SubscribeLocalEvent<RevolutionaryPropagandaComponent, ComponentShutdown>(OnPropagandaShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var ruleQuery = EntityQueryEnumerator<RevolutionaryRuleComponent>();
        if (!ruleQuery.MoveNext(out _, out var rule))
            return;

        var curTime = _timing.CurTime;

        // Passive LHP regeneration for non-revolutionary crew only
        var loyQuery = EntityQueryEnumerator<LoyaltyHealthComponent>();
        while (loyQuery.MoveNext(out var uid, out var lhp))
        {
            if (HasComp<RevolutionaryComponent>(uid) || HasComp<HeadRevolutionaryComponent>(uid))
                continue;

            if (lhp.LoyaltyHealth >= lhp.MaxLoyaltyHealth)
                continue;

            // Regen is suppressed briefly after taking loyalty damage
            if (curTime < lhp.LastDamageTime + lhp.RegenCooldown)
                continue;

            lhp.LoyaltyHealth = MathF.Min(lhp.LoyaltyHealth + lhp.RegenRate * frameTime, lhp.MaxLoyaltyHealth);
        }

        // Dead body aura — passive LHP drain/heal per second within range (requires LoS)
        var deadQuery = EntityQueryEnumerator<LoyaltyHealthComponent, MobStateComponent>();
        while (deadQuery.MoveNext(out var uid, out var auraLhp, out var mobState))
        {
            if (mobState.CurrentState != MobState.Dead)
                continue;

            // Direction is based on who killed this entity, not what faction they were.
            var killerIsRev = auraLhp.LastDamageSource.HasValue
                && (HasComp<RevolutionaryComponent>(auraLhp.LastDamageSource.Value)
                    || HasComp<HeadRevolutionaryComponent>(auraLhp.LastDamageSource.Value));
            var auraAmount = rule.DeathAuraRate * frameTime;

            var nearby = new HashSet<Entity<LoyaltyHealthComponent>>();
            _lookup.GetEntitiesInRange(Transform(uid).Coordinates, rule.DeathAuraRange, nearby);

            foreach (var (nearUid, nearLhp) in nearby)
            {
                if (nearUid == uid || !_mobState.IsAlive(nearUid))
                    continue;

                if (HasComp<RevolutionaryComponent>(nearUid) || HasComp<HeadRevolutionaryComponent>(nearUid))
                    continue;

                if (!_interact.InRangeUnobstructed(uid, nearUid, rule.DeathAuraRange + 0.5f))
                    continue;

                if (killerIsRev)
                    nearLhp.LoyaltyHealth = MathF.Min(nearLhp.LoyaltyHealth + auraAmount, nearLhp.MaxLoyaltyHealth);
                else
                    ApplyLoyaltyDamageInternal(nearUid, nearLhp, auraAmount, canConvert: false);
            }
        }

        // Cuffed deconversion — restrained revolutionaries (including HeadRevs) slowly recover loyalty
        var cuffedQuery = EntityQueryEnumerator<CuffableComponent, LoyaltyHealthComponent>();
        while (cuffedQuery.MoveNext(out var uid, out var cuffable, out var lhp))
        {
            if (cuffable.CuffedHandCount <= 0)
                continue;

            if (!HasComp<RevolutionaryComponent>(uid) && !HasComp<HeadRevolutionaryComponent>(uid))
                continue;

            if (!_mobState.IsAlive(uid))
                continue;

            lhp.LoyaltyHealth += rule.CuffedDeconversionRate * frameTime;
            if (lhp.LoyaltyHealth >= 0f)
                TryDeconvert(uid, lhp);
        }

        // Deconversion equipment (electro pack) — wearing it passively restores LHP for revs and HeadRevs
        var equipQuery = EntityQueryEnumerator<LoyaltyHealthComponent>();
        while (equipQuery.MoveNext(out var uid, out var lhp))
        {
            if (!HasComp<RevolutionaryComponent>(uid) && !HasComp<HeadRevolutionaryComponent>(uid))
                continue;

            if (!_mobState.IsAlive(uid))
                continue;

            // Check all inventory slots for deconversion equipment
            RevDeconversionEquipmentComponent? equip = null;
            if (_inventory.TryGetContainerSlotEnumerator(uid, out var slotEnum))
            {
                while (slotEnum.MoveNext(out var slot))
                {
                    if (slot.ContainedEntity is not { } slotItem)
                        continue;
                    if (TryComp(slotItem, out equip))
                        break;
                }
            }

            if (equip == null)
                continue;

            lhp.LoyaltyHealth += equip.HealRate * frameTime;
            if (lhp.LoyaltyHealth >= 0f)
                TryDeconvert(uid, lhp);
        }

        // Propaganda drain/heal — with stacking cap and LoS
        // We accumulate per-target totals across all posters this tick,
        // capping how many posters can affect the same target.
        var damageAccumulator = new Dictionary<EntityUid, float>();
        var stackCounts = new Dictionary<EntityUid, int>();

        var propagandaQuery = EntityQueryEnumerator<RevolutionaryPropagandaComponent>();
        while (propagandaQuery.MoveNext(out var posterUid, out var propaganda))
        {
            propaganda.AccumulatedTime += frameTime;
            if (propaganda.AccumulatedTime < propaganda.TickInterval)
                continue;

            propaganda.AccumulatedTime -= propaganda.TickInterval;

            var amount = propaganda.DrainRate * propaganda.TickInterval;

            var nearbyTargets = new HashSet<Entity<LoyaltyHealthComponent>>();
            _lookup.GetEntitiesInRange(Transform(posterUid).Coordinates, propaganda.Range, nearbyTargets);

            foreach (var (targetUid, _) in nearbyTargets)
            {
                if (HasComp<RevolutionaryComponent>(targetUid) || HasComp<HeadRevolutionaryComponent>(targetUid))
                    continue;

                if (!_mobState.IsAlive(targetUid))
                    continue;

                // Enforce the per-target stacking cap
                stackCounts.TryGetValue(targetUid, out var stacks);
                if (stacks >= rule.MaxPosterStacks)
                    continue;

                if (propaganda.RequiresLoS
                    && !_interact.InRangeUnobstructed(targetUid, posterUid, propaganda.Range + 0.5f))
                    continue;

                stackCounts[targetUid] = stacks + 1;

                damageAccumulator.TryGetValue(targetUid, out var existing);
                damageAccumulator[targetUid] = propaganda.IsLoyal
                    ? existing - amount
                    : existing + amount;
            }
        }

        foreach (var (targetUid, total) in damageAccumulator)
        {
            if (!TryComp<LoyaltyHealthComponent>(targetUid, out var lhp))
                continue;

            if (total > 0f)
                ApplyLoyaltyDamageInternal(targetUid, lhp, total, canConvert: false);
            else if (total < 0f)
                lhp.LoyaltyHealth = MathF.Min(lhp.LoyaltyHealth + (-total), lhp.MaxLoyaltyHealth);
        }
    }

    /// <summary>
    /// Applies a fixed amount of loyalty damage directly to a target, bypassing hearer-splitting.
    /// Shield absorbs first. Can optionally convert if the target is in the Convertable state.
    /// </summary>
    public bool ApplyLoyaltyDamage(EntityUid target, float damage, bool canConvert = true)
    {
        if (!TryComp<LoyaltyHealthComponent>(target, out var lhp))
            return false;

        return ApplyLoyaltyDamageInternal(target, lhp, damage, canConvert);
    }

    /// <summary>
    /// Restores loyalty health to a revolutionary. If LHP returns to >= 0, they are deconverted.
    /// </summary>
    public bool RestoreLoyaltyHealth(EntityUid target, float amount)
    {
        if (!TryComp<LoyaltyHealthComponent>(target, out var lhp))
            return false;

        if (!HasComp<RevolutionaryComponent>(target))
            return false;

        lhp.LoyaltyHealth = MathF.Min(lhp.LoyaltyHealth + amount, lhp.MaxLoyaltyHealth);

        // A revolutionary whose LHP returns to 0 or above is deconverted
        if (lhp.LoyaltyHealth >= 0f)
            return TryDeconvert(target, lhp);

        return false;
    }

    /// <summary>
    /// Returns true if the entity is in the Convertable state (LHP below threshold, not yet converted).
    /// </summary>
    public bool IsConvertable(EntityUid target)
    {
        if (!TryComp<LoyaltyHealthComponent>(target, out var lhp))
            return false;

        if (HasComp<RevolutionaryComponent>(target) || HasComp<HeadRevolutionaryComponent>(target))
            return false;

        return lhp.LoyaltyHealth < lhp.ConvertableThreshold;
    }

    /// <summary>
    /// Attempts to convert an entity into a Revolutionary.
    /// Requires the entity to be in the Convertable state unless <paramref name="forceConvert"/> is true.
    /// </summary>
    public bool TryConvert(EntityUid target, bool forceConvert = false)
    {
        if (!TryComp<LoyaltyHealthComponent>(target, out var lhp))
            return false;

        return TryConvertInternal(target, lhp, forceConvert);
    }

    /// <summary>
    /// Deconverts a revolutionary entity, removing their revolutionary status.
    /// </summary>
    public bool TryDeconvert(EntityUid target)
    {
        if (!TryComp<LoyaltyHealthComponent>(target, out var lhp))
            return false;

        return TryDeconvert(target, lhp);
    }

    private bool ApplyLoyaltyDamageInternal(EntityUid target, LoyaltyHealthComponent lhp, float damage, bool canConvert)
    {
        if (damage <= 0f)
            return false;

        if (!_mobState.IsAlive(target))
            return false;

        if (HasComp<RevolutionaryImmuneComponent>(target))
            return false;

        damage = _revolutionary.AbsorbLoyaltyDamage(target, damage);
        if (damage <= 0f)
            return false;

        var isRev = HasComp<RevolutionaryComponent>(target) || HasComp<HeadRevolutionaryComponent>(target);

        if (isRev)
            lhp.LoyaltyHealth = MathF.Max(lhp.LoyaltyHealth - damage, lhp.MinLoyaltyHealth);
        else
            lhp.LoyaltyHealth = MathF.Max(lhp.LoyaltyHealth - damage, 0f);

        lhp.LastDamageTime = _timing.CurTime;

        if (canConvert && !isRev && lhp.LoyaltyHealth < lhp.ConvertableThreshold)
            return TryConvertInternal(target, lhp, forceConvert: false);

        return false;
    }

    private bool TryConvertInternal(EntityUid target, LoyaltyHealthComponent lhp, bool forceConvert)
    {
        if (HasComp<RevolutionaryComponent>(target) || HasComp<HeadRevolutionaryComponent>(target))
            return false;

        var alwaysConvertible = HasComp<AlwaysRevolutionaryConvertibleComponent>(target);

        if (!forceConvert && lhp.LoyaltyHealth >= lhp.ConvertableThreshold)
            return false;

        if (!_mind.TryGetMind(target, out var mindId, out var mind) && !alwaysConvertible)
            return false;

        if (!HasComp<HumanoidProfileComponent>(target) && !alwaysConvertible)
            return false;

        if (!_mobState.IsAlive(target) || HasComp<ZombieComponent>(target))
            return false;

        _npcFaction.AddFaction(target, RevolutionaryFaction);
        var revComp = EnsureComp<RevolutionaryComponent>(target);
        lhp.LoyaltyHealth = lhp.PostConversionLoyalty;

        _adminLog.Add(LogType.Mind, LogImpact.Medium,
            $"{ToPrettyString(target)} was converted into a Revolutionary.");

        if (mindId != default && !_role.MindHasRole<RevolutionaryRoleComponent>(mindId))
            _role.MindAddRole(mindId, "MindRoleRevolutionary");

        if (mind is { UserId: not null } && _player.TryGetSessionById(mind.UserId, out var session))
            _antag.SendBriefing(session, Loc.GetString("rev-role-greeting"), Color.Red, revComp.RevStartSound);

        return true;
    }

    private bool TryDeconvert(EntityUid target, LoyaltyHealthComponent lhp)
    {
        var isRev = HasComp<RevolutionaryComponent>(target);
        var isHeadRev = HasComp<HeadRevolutionaryComponent>(target);

        if (!isRev && !isHeadRev)
            return false;

        var stunTime = TimeSpan.FromSeconds(1);
        var name = Identity.Entity(target, EntityManager);
        _npcFaction.RemoveFaction(target, RevolutionaryFaction);
        if (isRev) RemComp<RevolutionaryComponent>(target);
        if (isHeadRev) RemComp<HeadRevolutionaryComponent>(target);
        _stun.TryUpdateParalyzeDuration(target, stunTime);
        _popup.PopupEntity(Loc.GetString("rev-break-control", ("name", name)), target);
        lhp.LoyaltyHealth = lhp.PostDeconversionLoyalty;

        _adminLog.Add(LogType.Mind, LogImpact.Medium,
            $"{ToPrettyString(target)} was deconverted.");

        if (!_mind.TryGetMind(target, out var mindId, out var mind))
            return true;

        _role.MindRemoveRole<RevolutionaryRoleComponent>(mindId);

        if (_player.TryGetSessionById(mind.UserId, out var session))
            _euiMan.OpenEui(new DeconvertedEui(), session);

        return true;
    }

    private void ApplyAreaLoyaltyDamage(EntityUid source, float range, float damage, bool canConvert)
    {
        var nearby = new HashSet<Entity<LoyaltyHealthComponent>>();
        _lookup.GetEntitiesInRange(Transform(source).Coordinates, range, nearby);

        foreach (var (uid, nearLhp) in nearby)
        {
            if (HasComp<RevolutionaryComponent>(uid) || HasComp<HeadRevolutionaryComponent>(uid))
                continue;

            ApplyLoyaltyDamageInternal(uid, nearLhp, damage, canConvert);
        }
    }

    /// <summary>
    /// Called by <see cref="RevolutionarySystem.OnHeadRevStartup"/> when HeadRevolutionaryComponent is added.
    /// Sets the entity's LHP range to -500–100 and initialises LHP to -500.
    /// </summary>
    public void InitHeadRevLoyaltyHealth(EntityUid uid)
    {
        var lhp = EnsureComp<LoyaltyHealthComponent>(uid);
        lhp.MinLoyaltyHealth = -500f;
        lhp.LoyaltyHealth = -500f;
    }

    /// <summary>
    /// When a deconversion weapon (e.g. stun baton) hits a revolutionary, restore their LHP.
    /// </summary>
    private void OnDeconversionWeaponHit(EntityUid uid, RevDeconversionWeaponComponent comp, MeleeHitEvent args)
    {
        if (!args.IsHit)
            return;

        foreach (var hit in args.HitEntities)
        {
            if (!HasComp<RevolutionaryComponent>(hit))
                continue;

            if (!TryComp<LoyaltyHealthComponent>(hit, out var lhp))
                continue;

            lhp.LoyaltyHealth += comp.DeconversionAmount;
            if (lhp.LoyaltyHealth >= 0f)
                TryDeconvert(hit, lhp);
        }
    }

    /// <summary>
    /// When a propaganda poster entity is deleted, apply a burst LHP effect to nearby crew.
    /// Revolutionary posters heal nearby crew when destroyed (removing influence).
    /// Loyal posters damage nearby crew when destroyed (loss of moral support).
    /// </summary>
    private void OnPropagandaShutdown(EntityUid uid, RevolutionaryPropagandaComponent comp, ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(uid) || comp.DestructionBurst <= 0f)
            return;

        var ruleQuery = EntityQueryEnumerator<RevolutionaryRuleComponent>();
        if (!ruleQuery.MoveNext(out _, out var rule))
            return;

        var coords = Transform(uid).Coordinates;
        var nearby = new HashSet<Entity<LoyaltyHealthComponent>>();
        _lookup.GetEntitiesInRange(coords, rule.PosterDestructionBurstRange, nearby);

        foreach (var (nearUid, nearLhp) in nearby)
        {
            if (HasComp<RevolutionaryComponent>(nearUid) || HasComp<HeadRevolutionaryComponent>(nearUid))
                continue;

            if (!_mobState.IsAlive(nearUid))
                continue;

            if (comp.IsLoyal)
                ApplyLoyaltyDamageInternal(nearUid, nearLhp, comp.DestructionBurst, canConvert: false);
            else
                nearLhp.LoyaltyHealth = MathF.Min(nearLhp.LoyaltyHealth + comp.DestructionBurst, nearLhp.MaxLoyaltyHealth);
        }
    }

    /// <summary>
    /// When a Head Rev uses an announcement console, their codewords deal station-wide non-split
    /// LHP damage. This can only trigger once per Head Rev per round.
    /// </summary>
    private void OnAnnouncementSent(ref CommunicationConsoleAnnouncementEvent ev)
    {
        if (ev.Sender is not { } sender)
            return;

        if (!HasComp<HeadRevolutionaryComponent>(sender))
            return;

        var ruleQuery = EntityQueryEnumerator<RevolutionaryRuleComponent>();
        if (!ruleQuery.MoveNext(out _, out var rule))
            return;

        // Only fires once per head rev
        if (!_mind.TryGetMind(sender, out var mindId, out _))
            return;

        if (!_role.MindHasRole<RevolutionaryRoleComponent>(mindId, out var roleEntity))
            return;

        var roleComp = roleEntity.Value.Comp2;
        if (roleComp.UsedAnnouncementCodeword)
            return;

        var (damage, _) = GetHighestCodewordTierInMessage(ev.Text, rule);
        if (damage <= 0f)
            return;

        roleComp.UsedAnnouncementCodeword = true;

        // Non-split, station-wide loyalty damage — affect all non-rev crew with LHP
        var allCrew = EntityQueryEnumerator<LoyaltyHealthComponent>();
        while (allCrew.MoveNext(out var uid, out var lhp))
        {
            if (HasComp<RevolutionaryComponent>(uid) || HasComp<HeadRevolutionaryComponent>(uid))
                continue;

            ApplyLoyaltyDamageInternal(uid, lhp, damage, canConvert: false);
        }

        _adminLog.Add(LogType.Mind, LogImpact.Medium,
            $"{ToPrettyString(sender)} used announcement codeword for station-wide {damage} LHP damage.");
    }

    private void OnEntitySpoke(EntitySpokeEvent ev)
    {
        var source = ev.Source;

        if (!HasComp<RevolutionaryComponent>(source) && !HasComp<HeadRevolutionaryComponent>(source))
            return;

        var ruleQuery = EntityQueryEnumerator<RevolutionaryRuleComponent>();
        if (!ruleQuery.MoveNext(out _, out var rule))
            return;

        // Enforce per-speaker cooldown via RevolutionaryRoleComponent
        if (!_mind.TryGetMind(source, out var mindId, out _))
            return;

        if (!_role.MindHasRole<RevolutionaryRoleComponent>(mindId, out var roleEntity))
            return;

        var roleComp = roleEntity.Value.Comp2;
        var curTime = _timing.CurTime;
        if (curTime - roleComp.LastCodewordTime < rule.CodewordCooldown)
            return;

        var message = ev.Message;

        // Determine the highest-tier codeword found in the message
        var (damage, canConvert) = GetHighestCodewordTierInMessage(message, rule);
        if (damage <= 0f)
            return;

        roleComp.LastCodewordTime = curTime;

        // Apply radio multiplier when spoken over a radio channel
        var isRadio = ev.Channel != null;
        if (isRadio)
            damage *= rule.RadioDamageMultiplier;

        // Headband multiplier: wearing the revolutionary headband boosts codeword damage
        if (_inventory.TryGetSlotEntity(source, "head", out var headItem)
            && TryComp<RevolutionaryHeadbandComponent>(headItem, out var headband))
        {
            damage *= headband.DamageMultiplier;
        }

        // Poster codeword multiplier: if at least one revolutionary poster is nearby with LoS, boost damage
        if (HasNearbyRevPoster(source, rule.CodewordRange))
            damage *= rule.PosterCodewordMultiplier;

        // Collect nearby crew who can hear this codeword
        var hearers = new HashSet<Entity<LoyaltyHealthComponent>>();
        _lookup.GetEntitiesInRange(Transform(source).Coordinates, rule.CodewordRange, hearers);

        // Exclude the speaker and all revolutionaries
        hearers.RemoveWhere(e =>
            e.Owner == source
            || HasComp<RevolutionaryComponent>(e.Owner)
            || HasComp<HeadRevolutionaryComponent>(e.Owner));

        if (hearers.Count == 0)
            return;

        // Damage is split among all hearers (targeted speech is more effective)
        var splitDamage = damage / hearers.Count;

        foreach (var (uid, hearerLhp) in hearers)
        {
            ApplyLoyaltyDamageInternal(uid, hearerLhp, splitDamage, canConvert);
        }
    }

    /// <summary>
    /// Returns true if there is at least one non-loyal revolutionary propaganda poster
    /// within <paramref name="range"/> of <paramref name="speaker"/> with line-of-sight.
    /// </summary>
    private bool HasNearbyRevPoster(EntityUid speaker, float range)
    {
        var nearbyPosters = new HashSet<Entity<RevolutionaryPropagandaComponent>>();
        _lookup.GetEntitiesInRange(Transform(speaker).Coordinates, range, nearbyPosters);

        foreach (var (posterUid, poster) in nearbyPosters)
        {
            if (poster.IsLoyal)
                continue;

            if (!poster.RequiresLoS || _interact.InRangeUnobstructed(speaker, posterUid, range + 0.5f))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Scans a message for revolutionary codewords across all tiers.
    /// Returns the damage and canConvert flag for the highest tier codeword found.
    /// Only the highest tier in a message applies; multiple codewords in one sentence do nothing extra.
    /// </summary>
    private (float damage, bool canConvert) GetHighestCodewordTierInMessage(string message, RevolutionaryRuleComponent rule)
    {
        // Check High tier first
        foreach (var word in _codewords.GetCodewords(rule.HighCodewordFaction))
        {
            if (message.Contains(word, StringComparison.OrdinalIgnoreCase))
                return (rule.HighCodewordDamage, canConvert: true);
        }

        foreach (var word in _codewords.GetCodewords(rule.MidCodewordFaction))
        {
            if (message.Contains(word, StringComparison.OrdinalIgnoreCase))
                return (rule.MidCodewordDamage, canConvert: false);
        }

        foreach (var word in _codewords.GetCodewords(rule.LowCodewordFaction))
        {
            if (message.Contains(word, StringComparison.OrdinalIgnoreCase))
                return (rule.LowCodewordDamage, canConvert: false);
        }

        return (0f, false);
    }

    /// <summary>
    /// Tracks the last entity to deal damage to this entity.
    /// Used by death burst/aura to determine direction based on killer's faction.
    /// </summary>
    private void OnDamageChanged(EntityUid uid, LoyaltyHealthComponent comp, DamageChangedEvent args)
    {
        if (args.DamageIncreased && args.Origin.HasValue)
            comp.LastDamageSource = args.Origin;
    }

    private void OnEntityDied(EntityUid uid, LoyaltyHealthComponent lhp, MobStateChangedEvent ev)
    {
        if (ev.NewMobState != MobState.Dead)
            return;

        var ruleQuery = EntityQueryEnumerator<RevolutionaryRuleComponent>();
        if (!ruleQuery.MoveNext(out _, out var rule))
            return;

        var curTime = _timing.CurTime;
        if (curTime - lhp.LastDeathBurstTime < rule.DeathBurstCooldown)
            return;

        lhp.LastDeathBurstTime = curTime;

        // Direction is based on who killed this entity, not what faction the dead entity belongs to.
        var killerIsRev = lhp.LastDamageSource.HasValue
            && (HasComp<RevolutionaryComponent>(lhp.LastDamageSource.Value)
                || HasComp<HeadRevolutionaryComponent>(lhp.LastDamageSource.Value));
        var deadIsRev = HasComp<RevolutionaryComponent>(uid) || HasComp<HeadRevolutionaryComponent>(uid);

        var nearby = new HashSet<Entity<LoyaltyHealthComponent>>();
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, rule.DeathBurstRange, nearby);

        foreach (var (nearUid, nearLhp) in nearby)
        {
            if (nearUid == uid || !_mobState.IsAlive(nearUid))
                continue;

            if (HasComp<RevolutionaryComponent>(nearUid) || HasComp<HeadRevolutionaryComponent>(nearUid))
                continue;

            if (!_interact.InRangeUnobstructed(uid, nearUid, rule.DeathBurstRange + 0.5f))
                continue;

            if (killerIsRev)
            {
                nearLhp.LoyaltyHealth = MathF.Min(nearLhp.LoyaltyHealth + rule.DeathBurstDamage, nearLhp.MaxLoyaltyHealth);
            }
            else
            {
                // if the victim was a rev, their death can convert wavering crew (martyr effect)
                ApplyLoyaltyDamageInternal(nearUid, nearLhp, rule.DeathBurstDamage, canConvert: deadIsRev);
            }
        }
    }
}
