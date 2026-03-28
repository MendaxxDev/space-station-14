using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Radiation.Components;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    /// <summary>
    ///     How often (in seconds) to check grid atmospheres for tritium and emit radiation pulses.
    /// </summary>
    private const float TritiumRadiationUpdateInterval = 5.0f;

    /// <summary>
    ///     Cooldown (in seconds) between fire radiation pulses for a single burning tile.
    ///     Matches the TritiumFireRadiationPulse entity lifetime so only one exists at a time per tile.
    /// </summary>
    private const float TritiumFirePulseCooldown = 2.0f;

    private float _tritiumRadiationTimer;

    /// <summary>
    ///     Tracks when a fire radiation pulse was last spawned per tile, to prevent flooding.
    /// </summary>
    private readonly Dictionary<(EntityUid Grid, Vector2i Tile), TimeSpan> _tritiumFirePulseTimes = new();

    /// <summary>
    ///     Minimum moles of tritium on a tile required to emit ambient radiation.
    /// </summary>
    private const float MinTritiumMolesForRadiation = 0.5f;

    /// <summary>
    ///     Intensity scaling factor: radiation intensity (rads/s) per mole of tritium.
    /// </summary>
    private const float TritiumRadiationIntensityFactor = 0.05f;

    /// <summary>
    ///     Maximum ambient radiation intensity that tritium can produce on a single tile (rads/s).
    /// </summary>
    private const float TritiumRadiationMaxIntensity = 3.0f;

    private void ProcessTritiumRadiation(float frameTime)
    {
        _tritiumRadiationTimer += frameTime;
        if (_tritiumRadiationTimer < TritiumRadiationUpdateInterval)
            return;

        _tritiumRadiationTimer -= TritiumRadiationUpdateInterval;

        var query = EntityQueryEnumerator<GridAtmosphereComponent>();
        while (query.MoveNext(out var gridUid, out var atmosphere))
        {
            foreach (var (indices, tile) in atmosphere.Tiles)
            {
                if (tile.Air == null)
                    continue;

                var tritiumMoles = tile.Air.GetMoles(Gas.Tritium);
                if (tritiumMoles < MinTritiumMolesForRadiation)
                    continue;

                // Spawn a short-lived radiation source entity centered on this tile.
                var coords = _mapSystem.ToCenterCoordinates(gridUid, indices);
                var pulse = Spawn("TritiumRadiationPulse", coords);

                // Scale intensity to the amount of tritium present.
                var intensity = Math.Clamp(tritiumMoles * TritiumRadiationIntensityFactor,
                    0.1f,
                    TritiumRadiationMaxIntensity);

                if (TryComp<RadiationSourceComponent>(pulse, out var radSource))
                    radSource.Intensity = intensity;
            }
        }
    }

    /// <summary>
    ///     Spawns a short radiation pulse when tritium is actively burning on a tile.
    ///     Rate-limited to one pulse per tile per <see cref="TritiumFirePulseCooldown"/> seconds.
    /// </summary>
    public void SpawnTritiumFireRadiationPulse(TileAtmosphere tile)
    {
        var key = (tile.GridIndex, tile.GridIndices);
        var now = _gameTiming.CurTime;

        if (_tritiumFirePulseTimes.TryGetValue(key, out var lastTime)
            && (now - lastTime).TotalSeconds < TritiumFirePulseCooldown)
        {
            return;
        }

        _tritiumFirePulseTimes[key] = now;
        var coords = _mapSystem.ToCenterCoordinates(tile.GridIndex, tile.GridIndices);
        Spawn("TritiumFireRadiationPulse", coords);
    }
}