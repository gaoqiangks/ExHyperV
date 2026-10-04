namespace ExHyperV.Services;

// A named kernel object remains alive while any console (or pending automatic
// connection) holds a handle. No mutex ownership is held across async calls.
internal static class ConnectionPresence
{
    private const string MarkerName = @"Local\ExHyperV-Console-Presence-v2";

    internal static IDisposable Register() => new Mutex(false, MarkerName);

    internal static IDisposable? TryReserveAutomatic()
    {
        var marker = new Mutex(false, MarkerName, out bool first);
        if (first) return marker;
        marker.Dispose();
        return null;
    }
}
