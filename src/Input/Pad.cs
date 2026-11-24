namespace GunsawDualSenseSupport;

internal enum Button
{
    Cross = 0, Circle = 1, Square = 2, Triangle = 3, Share = 4, Options = 6,
    L3 = 7, R3 = 8, L1 = 9, R1 = 10, Up = 11, Down = 12, Left = 13, Right = 14, Touch = 20
}

internal struct Pad
{
    internal int Buttons;
    internal float LX, LY, RX, RY, L2, R2, TouchX, TouchY, MoveX, MoveY;
    internal bool Touching, Firing;

    internal bool Has(Button button) => (Buttons & (1 << (int)button)) != 0;

    internal static Pad Decode(byte[] packet) => new()
    {
        Buttons = packet[0] | packet[1] << 8 | packet[2] << 16,
        LX = Axis(packet[3]), LY = Axis(packet[4]), RX = Axis(packet[5]), RY = Axis(packet[6]),
        L2 = packet[7] / 255f, R2 = packet[8] / 255f,
        Touching = packet[9] != 0, TouchX = packet[10] / 255f, TouchY = packet[11] / 255f
    };

    private static float Axis(byte value) => (value - 128) / (value < 128 ? 128f : 127f);
}