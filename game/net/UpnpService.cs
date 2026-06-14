using System;
using System.Net;
using System.Threading.Tasks;
using Godot;

namespace Miner49er;

/// <summary>Best-effort UPnP port mapping for internet hosting. Discovery and
/// deletion block, so they run on background threads; the result is reported via
/// a callback the caller marshals back to the main thread. ENet is UDP, so the
/// mapping uses the UDP protocol.</summary>
public sealed class UpnpService
{
	private Upnp? _upnp;   // discovered gateway instance, kept for clean deletion
	private int _port;
	private bool _mapped;

	// Runs discovery + mapping on a background thread. onComplete(success, publicIp)
	// fires on that thread; the caller marshals to the main thread.
	public void Open(int port, Action<bool, string> onComplete)
	{
		_port = port;
		Task.Run(() =>
		{
			try
			{
				var upnp = new Upnp();
				if (upnp.Discover() != (int)Upnp.UpnpResult.Success)
				{
					onComplete(false, "");
					return;
				}
				var gateway = upnp.GetGateway();
				if (gateway is null || !gateway.IsValidGateway())
				{
					onComplete(false, "");
					return;
				}
				int add = upnp.AddPortMapping(port, port, "Miner49er", "UDP", 0);
				string ext = upnp.QueryExternalAddress();
				bool ok = add == (int)Upnp.UpnpResult.Success
				          && IPAddress.TryParse(ext, out var parsed)
				          && parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
				if (ok) { _upnp = upnp; _mapped = true; }
				onComplete(ok, ok ? ext : "");
			}
			catch
			{
				onComplete(false, "");
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
