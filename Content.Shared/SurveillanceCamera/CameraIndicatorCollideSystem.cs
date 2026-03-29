using Content.Shared.SurveillanceCamera.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

namespace Content.Shared.SurveillanceCamera;

/// <summary>
/// Makes cameras show as "in use" when a <see cref="CameraIndicatorColliderComponent"/> entity is nearby.
/// Uses physics collision with camera fixtures, similar to <see cref="Content.Shared.Light.EntitySystems.LightCollideSystem"/>.
/// </summary>
public sealed class CameraIndicatorCollideSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    private EntityQuery<CameraIndicatorOnCollideComponent> _indicatorQuery;
    private EntityQuery<SurveillanceCameraComponent> _cameraQuery;

    public override void Initialize()
    {
        base.Initialize();

        _indicatorQuery = GetEntityQuery<CameraIndicatorOnCollideComponent>();
        _cameraQuery = GetEntityQuery<SurveillanceCameraComponent>();

        SubscribeLocalEvent<CameraIndicatorColliderComponent, PreventCollideEvent>(OnPreventCollide);
        SubscribeLocalEvent<CameraIndicatorColliderComponent, StartCollideEvent>(OnStart);
        SubscribeLocalEvent<CameraIndicatorColliderComponent, EndCollideEvent>(OnEnd);
        SubscribeLocalEvent<CameraIndicatorColliderComponent, ComponentShutdown>(OnColliderShutdown);
    }

    private void OnColliderShutdown(Entity<CameraIndicatorColliderComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent.Owner))
            return;

        var contacts = _physics.GetContacts(ent.Owner);

        while (contacts.MoveNext(out var contact))
        {
            if (!contact.IsTouching)
                continue;

            var other = contact.OtherEnt(ent.Owner);

            if (_indicatorQuery.HasComp(other))
            {
                _physics.RegenerateContacts(other);
            }
        }
    }

    private void OnPreventCollide(Entity<CameraIndicatorColliderComponent> ent, ref PreventCollideEvent args)
    {
        if (!_indicatorQuery.HasComp(args.OtherEntity))
        {
            args.Cancelled = true;
        }
    }

    private void OnStart(Entity<CameraIndicatorColliderComponent> ent, ref StartCollideEvent args)
    {
        if (args.OurFixtureId != ent.Comp.FixtureId)
            return;

        if (!_indicatorQuery.TryGetComponent(args.OtherEntity, out var indicator))
            return;

        indicator.ActiveColliders++;
        UpdateVisuals(args.OtherEntity);
    }

    private void OnEnd(Entity<CameraIndicatorColliderComponent> ent, ref EndCollideEvent args)
    {
        if (args.OurFixtureId != ent.Comp.FixtureId)
            return;

        if (!_indicatorQuery.TryGetComponent(args.OtherEntity, out var indicator))
            return;

        indicator.ActiveColliders = Math.Max(0, indicator.ActiveColliders - 1);
        UpdateVisuals(args.OtherEntity);
    }

    private void UpdateVisuals(EntityUid uid)
    {
        var key = SurveillanceCameraVisuals.Disabled;

        if (_cameraQuery.TryGetComponent(uid, out var camera) && camera.Active)
            key = SurveillanceCameraVisuals.Active;

        if (camera != null && (camera.ActiveViewers.Count > 0 || camera.ActiveMonitors.Count > 0))
            key = SurveillanceCameraVisuals.InUse;

        if (_indicatorQuery.TryGetComponent(uid, out var indicator) && indicator.ActiveColliders > 0)
            key = SurveillanceCameraVisuals.InUse;

        _appearance.SetData(uid, SurveillanceCameraVisualsKey.Key, key);
    }
}
