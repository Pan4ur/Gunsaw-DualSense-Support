using System.Net;
using System.Net.Sockets;
using System.Text;

namespace GunsawDualSenceSupport;

internal sealed class EmulatorLink : IDisposable
{
    private const int Port = 27355;
    private const int InputPacketSize = 12;
    private readonly Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private readonly byte[] packet = new byte[64];
    private EndPoint latestSender = new IPEndPoint(IPAddress.Loopback, 0);
    private EndPoint? peer;
    private Pad last;
    private float lastPacket = float.NegativeInfinity, nextSend;

    internal EmulatorLink()
    {
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Loopback, Port));
            socket.Blocking = false;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    internal bool Poll(float now, out Pad pad)
    {
        for (var i = 0; i < 32 && socket.Poll(0, SelectMode.SelectRead); i++)
        {
            try
            {
                if (socket.ReceiveFrom(packet, ref latestSender) != InputPacketSize) continue;
                last = Pad.Decode(packet);
                peer = latestSender;
                lastPacket = now;
            }
            catch (SocketException) { break; }
        }
        bool alive = now - lastPacket < 0.35f;
        pad = alive ? last : default;
        return alive;
    }

    internal void Send(float now, Feedback effects, string state, bool shot = false)
    {
        if (peer == null || (!shot && now < nextSend)) return;
        nextSend = now + 0.04f;
        var line = FormattableString.Invariant($"{effects.Left} {Math.Max(effects.RightKick, effects.RightDetent ? 1 : 0)} {effects.FirePercent} {effects.Low:F2} {effects.High:F2} {effects.Engine:F2} {(effects.Heart ? 1 : 0)} {effects.Locators} {state}");
        try { socket.SendTo(Encoding.UTF8.GetBytes(line), peer); }
        catch (SocketException) { }
    }

    public void Dispose() => socket.Dispose();
}
