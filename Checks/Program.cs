using GunsawDualSenceSupport;

CheckHidReports();
CheckTriggerThresholds();
CheckRecoil();
Console.WriteLine("HID reports, trigger thresholds and recoil checks passed.");

static void CheckHidReports()
{
    var report = new byte[HidReport.Size];
    var rumble = HidReport.EncodeRumble(0.5f, 1);
    Equal((128, 255), ((int)rumble.Low, (int)rumble.High), "Motor amplitude quantization");

    HidReport.WriteEffects(report, true, rumble, 8, (6, 12, false), true);
    Packet(report, "AE00FF80006400300000 26F00300D0B62D00000C00 21FC03C0FFFF3F00000000", "Combined improved rumble, triggers and USB speaker", (38, 4));

    HidReport.WriteEffects(report, true, (0, 0), null, null, false);
    Packet(report, "0200", "Improved rumble stop", (38, 4));

    HidReport.WriteEffects(report, false, rumble, null, null, false);
    Packet(report, "0300FF80", "Compatible rumble");
    HidReport.WriteEffects(report, false, (0, 0), null, null, false);
    Packet(report, "03", "Compatible rumble stop");

    HidReport.WriteEffects(report, false, null, 1, (0, 0, true), false);
    Packet(report, "0C000000000000000000 02667F1000000000000000 21FC030000000000000000", "Soft detent and minimum levitation resistance");
    HidReport.WriteEffects(report, false, null, 0, (0, 0, false), false);
    Packet(report, "0C000000000000000000 0500000000000000000000 05", "Trigger release without rumble");

    HidReport.WriteEffects(report, false, null, null, (5, 0, false), false);
    Packet(report, "04000000000000000000 21F0030040922400000000", "Single-shot trigger kick");

    HidReport.WriteLightbar(report, false, 12, 34, 56);
    Packet(report, "0004", "Lightbar RGB", (44, 12), (45, 34), (46, 56));
    HidReport.WriteLightbar(report, true);
    Packet(report, "0008", "Lightbar reset");
}

static void Packet(byte[] actual, string prefix, string label, params (int Offset, byte Value)[] fields)
{
    var expected = new byte[47];
    Convert.FromHexString(prefix.Replace(" ", "")).CopyTo(expected, 0);
    foreach (var (offset, value) in fields) expected[offset] = value;
    Equal(Convert.ToHexString(expected), Convert.ToHexString(actual), label);
}

static void CheckTriggerThresholds()
{
    Equal(false, Trigger.FireHeld(0.159f, false, false), "Below unarmed threshold");
    Equal(true, Trigger.FireHeld(0.16f, false, false), "At unarmed threshold");
    Equal(false, Trigger.FireHeld(0.499f, false, true), "Below armed threshold");
    Equal(true, Trigger.FireHeld(0.5f, false, true), "At armed threshold");
    Equal(true, Trigger.FireHeld(0.28f, true, true), "Armed hysteresis");
    Equal(false, Trigger.FireHeld(0.279f, true, true), "Below armed release");

    Equal(true, Trigger.ArmFireDetent(true, 0.499f), "Detent stays armed before shot");
    Equal(false, Trigger.ArmFireDetent(true, 0.5f), "Detent clears at shot threshold");
    Equal(false, Trigger.ArmFireDetent(false, 0.181f), "Detent stays clear during release");
    Equal(true, Trigger.ArmFireDetent(false, 0.18f), "Detent resets independently of firing");
    Equal(1, Trigger.LevitationResistance(0), "Minimum levitation force");
    Equal(2, Trigger.LevitationResistance(9.5f), "Cubic levitation resistance");
    Equal(8, Trigger.LevitationResistance(25), "Maximum levitation force");
}

static void CheckRecoil()
{
    var pistol = new RecoilPulse(5, 1, true, false, false);
    Equal((5, 0), pistol.TriggerAt(0, true, false), "Pistol kick starts at shot");
    Equal((5, 0), pistol.TriggerAt(0.023f, true, true), "Pistol kick before deadline");
    Equal((0, 0), pistol.TriggerAt(0.024f, true, true), "Pistol kick at deadline");
    Equal((0, 0), pistol.TriggerAt(0.01f, false, true), "Pistol kick ends on release");

    var pellets = new RecoilPulse(5, 10, false, false, false);
    Equal((1f, 1f), pellets.At(0.089f), "Pellet count scales both motors and duration");
    Equal((0f, 0f), pellets.At(0.091f), "Maximum rumble expires after 90 ms");
    var rocket = new RecoilPulse(100, 1, false, true, true);
    Equal((1f, 0.85f), rocket.At(0), "Rocket motor balance");
    Equal((7, 0), rocket.TriggerAt(0, true, true), "Launcher trigger force");

    var automatic = new RecoilPulse(5, 1, false, false, false, 0.1f, true);
    Equal((6, 10), automatic.TriggerAt(0.117f, true, true), "Automatic kick follows shot cadence");
    Equal((0, 0), automatic.TriggerAt(0.119f, true, true), "Automatic kick expires between bursts");
    Equal((0, 0), automatic.TriggerAt(0.036f, true, false), "Automatic kick expires sooner when burst cannot continue");
    Equal((0.82f, 0.82f), automatic.At(0.13f, true), "Rumble sustains through a burst");
    Equal((0f, 0f), automatic.At(0.13f), "Rumble expires after a single shot");
    Equal(0f, RecoilPulse.TrackInterval(0.1f, 0.25f, 0.1f), "Late shot starts a new burst");
    Equal(0.12f, RecoilPulse.TrackInterval(0.1f, 0.12f, 0), "First measured burst interval");
    Equal(0.125f, RecoilPulse.TrackInterval(0.1f, 0.145f, 0.12f), "Smoothed burst interval");

    foreach (var fps in new[] { 30, 60, 120 })
    {
        var frame = 0;
        while (pistol.TriggerAt(frame / (float)fps, true, true).Force > 0) frame++;
        Equal((int)Math.Ceiling(0.024 * fps), frame, $"First update after the 24 ms deadline at {fps} FPS");
    }
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
}
