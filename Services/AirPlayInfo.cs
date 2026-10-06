namespace AirGlass.Services;

/// <summary>Identity constants of the AirPlay receiver.</summary>
public static class AirPlayInfo
{
    /// <summary>Base TCP/UDP port. UxPlay uses this port and the next two (7000-7002).</summary>
    public const int AirPlayPort = 7000;

    /// <summary>Default name shown in the iOS "Screen Mirroring" list (the PC name).</summary>
    public static string ReceiverName { get; } = BuildReceiverName();

    private static string BuildReceiverName()
    {
        var host = Environment.MachineName;
        return string.IsNullOrWhiteSpace(host) ? "AirGlass" : host;
    }
}
