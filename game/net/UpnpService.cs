using System;
using System.Net;
using System.Threading.Tasks;
using Godot;

namespace Miner49er;

/// <summary>Which step of the UPnP handshake failed, so the lobby can tell the
/// player whether it's a router setting (fixable) or CGNAT (not fixable).</summary>
public enum UpnpFailure { None, NoGateway, MappingRefused, NoPublicIPv4, Error }

/// <summary>Best-effort UPnP port mapping for internet hosting. Discovery and
/// deletion block, so they run on background threads; the result is reported via
/// a callback the caller marshals back to the main thread. ENet is UDP, so the
/// mapping uses the UDP protocol.</summary>
public sealed class UpnpService
{
	private Upnp? _upnp;   // discovered gateway instance, kept for clean deletion
	private int _port;
	private bool _mapped;

	// Runs discovery + mapping on a background thread. onComplete(success, publicIp,
	// reason) fires on that thread; the caller marshals to the main thread. On success
	// reason is None; on failure it names the step that failed for diagnostics.
	public void Open(int port, Action<bool, string, UpnpFailure> onComplete)
	{
		_port = port;
		Task.Run(() =>
		{
			try
			{
				var upnp = new Upnp();
				if (upnp.Discover() != (int)Upnp.UpnpResult.Success)
				{
					// No IGD answered the SSDP discovery — UPnP is off or unsupported.
					onComplete(false, "", UpnpFailure.NoGateway);
					return;
				}
				var gateway = upnp.GetGateway();
				if (gateway is null || !gateway.IsValidGateway())
				{
					onComplete(false, "", UpnpFailure.NoGateway);
					return;
				}
				if (upnp.AddPortMapping(port, port, "Miner49er", "UDP", 0) != (int)Upnp.UpnpResult.Success)
				{
					// Gateway found but it declined the mapping (UPnP restricted, or a
					// stale/conflicting mapping already holds the port).
					onComplete(false, "", UpnpFailure.MappingRefused);
					return;
				}
				string ext = upnp.QueryExternalAddress();
				if (!IPAddress.TryParse(ext, out var parsed)
					|| parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
				{
					// Mapping succeeded but there's no routable public IPv4 (CGNAT or
					// IPv6-only). The mapping is on our own NAT, not the real edge.
					onComplete(false, "", UpnpFailure.NoPublicIPv4);
					return;
				}
				_upnp = upnp;
				_mapped = true;
				onComplete(true, ext, UpnpFailure.None);
			}
			catch
			{
				onComplete(false, "", UpnpFailure.Error);
			}
		});
	}

	// Removes the mapping on a background thread, reusing the discovered gateway.
	public void Release()
	{
		if (!_mapped || _upnp is null) return;
		var upnp = _upnp;
		int port = _port;
		_mapped = false;
		_upnp = null;
		Task.Run(() => { try { upnp.DeletePortMapping(port, "UDP"); } catch { /* best effort */ } });
	}
}
