using UnityEngine;
using UnityEngine.SceneManagement;

namespace GunsawDualSenceSupport;

internal sealed class Effects
{
    private MotorRumble motorRumble;
    private BodyScript? lastBody;
    private float lastHealth, hitAt = -10, shotAt = -10, heartAt = -10;
    private float hit, autoInterval;
    private RecoilPulse shot;
    private AudioClip? locator;
    private int locatorCount;
    private WeaponPreset? cachedWeapon;
    private RocketProjectile? rocket;
    private GrenadeScript? grenade;
    private WeaponScript? shotWeapon;
    internal int ShotFrame { get; private set; } = -1;
    private bool detentArmed = true;

    internal (int Force, int Hertz, float Low, float High) Shot(WeaponScript source, bool held)
    {
        var weapon = source.stats;
        var now = Time.unscaledTime;
        var nominal = weapon.fireDelay / Mathf.Max(0.01f, Time.timeScale);
        autoInterval = held && weapon.fullAuto && shotWeapon == source && cachedWeapon == weapon
            ? RecoilPulse.TrackInterval(nominal, now - shotAt, autoInterval)
            : 0;
        shotWeapon = source;
        var launcher = weapon.ammoType == 4;
        if (cachedWeapon != weapon)
        {
            cachedWeapon = weapon;
            var projectile = weapon.shootType == 1 ? weapon.tracerLine : null;
            rocket = projectile != null ? projectile.GetComponentInChildren<RocketProjectile>(true) : null;
            grenade = projectile != null && rocket == null ? projectile.GetComponentInChildren<GrenadeScript>(true) : null;
        }
        float damage = rocket != null ? rocket.damage : grenade != null ? grenade.damage : weapon.damage;
        var interval = autoInterval > 0 ? autoInterval : nominal;
        shot = new RecoilPulse(damage, weapon.bulletAmount, weapon.slot == 0, launcher, rocket != null, interval, weapon.fullAuto);
        shotAt = now;
        ShotFrame = Time.frameCount;
        var (force, hertz) = shot.TriggerAt(0, held, source.ammo > 0);
        var (low, high) = shot.At(0);
        return (force, hertz, low, high);
    }

    internal void Locate(AudioClip clip)
    {
        locator = clip;
        locatorCount++;
    }

    internal static bool HasFireWeapon(BodyScript? body) =>
        body != null && body.isAlive && !body.unarmed && body.weapon != null && body.weapon.stats != null;

    internal Feedback Tick(
        PlayerScript? player,
        float now,
        Speaker? speaker,
        bool enabled,
        bool triggerHeld,
        float speakerGain,
        float triggerTravel)
    {
        var body = player != null ? player.bodyScript : null;
        bool hasFireWeapon = enabled && HasFireWeapon(body);
        detentArmed = !hasFireWeapon || Trigger.ArmFireDetent(detentArmed, triggerTravel);
        if (!enabled || !triggerHeld) autoInterval = 0;
        var output = new Feedback { Left = 1, Locators = locatorCount };
        AudioSource? engineSource = null;
        if (body != null && enabled)
        {
            if (body != lastBody)
            {
                lastHealth = body.health;
                hitAt = heartAt = -10;
                if (ShotFrame != Time.frameCount) shotAt = -10;
            }
            if (lastHealth - body.health > 0.05f)
            {
                hitAt = now;
                hit = Mathf.Clamp((lastHealth - body.health) / Mathf.Max(1, body.maxHealth) * 2.4f, 0.08f, 1f);
            }
            lastHealth = body.health;

            var impact = hit * Mathf.Clamp01(1 - (now - hitAt) / 0.22f);
            float shotElapsed = now - shotAt;
            var weapon = body.weapon;
            bool canContinue = weapon != null && weapon == shotWeapon && weapon.ammo > 0 && !weapon.isReloading;
            var kick = shot.At(shotElapsed, hasFireWeapon && triggerHeld && canContinue);
            output.ShotRumble = kick != default;
            if (body.isAlive)
            {
                var hp = body.health / Mathf.Max(1, body.maxHealth);
                if (hp < 0.3f)
                {
                    if (now - heartAt >= Mathf.Lerp(0.58f, 0.93f, hp / 0.3f))
                    {
                        heartAt = now;
                        speaker?.Heartbeat();
                    }
                    float t = now - heartAt;
                    output.Heart = t < 0.065f || (t > 0.15f && t < 0.21f);
                }
                if (hasFireWeapon)
                {
                    output.FirePercent = Trigger.FirePercent;
                    output.RightDetent = detentArmed;

                    if (weapon == shotWeapon)
                    {
                        (output.RightKick, output.RightHz) =
                            shot.TriggerAt(
                                shotElapsed,
                                triggerHeld,
                                canContinue
                            );
                    }
                }

                var vehicle = body.curVehicle;
                if (vehicle != null && !vehicle.engineDisabled && vehicle.engineSound != null && vehicle.mainPart != null && vehicle.mainPart.rb != null)
                {
                    var speed = vehicle.mainPart.rb.velocity.magnitude;
                    if (speed > 0.3f && vehicle.engineSound.isPlaying)
                    {
                        engineSource = vehicle.engineSound;
                        output.Engine = Mathf.Clamp01(speed / 10f) * Mathf.Clamp01(engineSource.volume);
                    }
                }
            }

            var golf = body.curVehicle != null && (body.curVehicle.vehicleName?.IndexOf("kart", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                   body.curVehicle.vehicleName?.IndexOf("golf", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                   SceneManager.GetActiveScene().name.IndexOf("golf", StringComparison.OrdinalIgnoreCase) >= 0);
            var motor = motorRumble.Update(golf ? output.Engine * 0.4f : 0, now,
                speaker?.HasBluetoothRoute == true && speakerGain > 0);
            output.Low = Mathf.Max(Mathf.Max(impact, kick.Low), Mathf.Max(motor, output.Heart ? 0.34f : 0));
            output.High = Mathf.Max(impact * 0.55f, kick.High);
        }
        else if (body != null)
        {
            lastHealth = body.health;
            shotAt = hitAt = heartAt = -10;
        }
        if (body == null || !enabled) motorRumble = default;

        lastBody = body;
        if (locator != null) speaker?.Locator(locator);
        locator = null;
        speaker?.Tick(now, engineSource, output.Engine, speakerGain);
        return output;
    }
}
