using System;

namespace Miner49er.Core.Net;

/// <summary>Encodes a host endpoint (IPv4 + port) as a short, typo-resistant
/// share code. Crockford base32 over a 6-byte payload (4 IP + 2 port) gives 10
/// data chars; a trailing Crockford mod-37 check char rejects mistyped codes.
/// Pure C# (no Godot/sockets) so it is fully unit-testable.</summary>
public static class ConnectCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";   // 32 symbols (no I L O U)
    private const string CheckAlphabet = Alphabet + "*~$=U";              // 37 symbols (mod-37 check)

    public static string Encode(byte[] ipv4, ushort port)
    {
        if (ipv4 is null || ipv4.Length != 4)
            throw new ArgumentException("ipv4 must be 4 bytes", nameof(ipv4));

        var payload = new byte[6];
        payload[0] = ipv4[0]; payload[1] = ipv4[1]; payload[2] = ipv4[2]; payload[3] = ipv4[3];
        payload[4] = (byte)(port >> 8); payload[5] = (byte)(port & 0xFF);

        ulong v = 0;
        foreach (var b in payload) v = (v << 8) | b;   // 48-bit value

        var chars = new char[11];
        for (int i = 9; i >= 0; i--) { chars[i] = Alphabet[(int)(v & 31)]; v >>= 5; }
        chars[10] = CheckAlphabet[Checksum(payload)];
        return new string(chars);
    }

    public static bool TryDecode(string code, out byte[] ipv4, out ushort port)
    {
        ipv4 = Array.Empty<byte>();
        port = 0;
        if (code is null) return false;

        // Normalize: uppercase, drop spaces/dashes, map Crockford-equivalent glyphs.
        var sb = new System.Text.StringBuilder(code.Length);
        foreach (var raw in code)
        {
            char c = char.ToUpperInvariant(raw);
            if (c == ' ' || c == '-') continue;
            if (c == 'I' || c == 'L') c = '1';
            else if (c == 'O') c = '0';
            sb.Append(c);
        }
        var norm = sb.ToString();
        if (norm.Length != 11) return false;

        ulong v = 0;
        for (int i = 0; i < 10; i++)
        {
            int idx = Alphabet.IndexOf(norm[i]);
            if (idx < 0) return false;
            v = (v << 5) | (ulong)idx;
        }

        var payload = new byte[6];
        payload[0] = (byte)((v >> 40) & 0xFF);
        payload[1] = (byte)((v >> 32) & 0xFF);
        payload[2] = (byte)((v >> 24) & 0xFF);
        payload[3] = (byte)((v >> 16) & 0xFF);
        payload[4] = (byte)((v >> 8) & 0xFF);
        payload[5] = (byte)(v & 0xFF);

        int provided = CheckAlphabet.IndexOf(norm[10]);
        if (provided < 0 || provided != Checksum(payload)) return false;

        ipv4 = new byte[] { payload[0], payload[1], payload[2], payload[3] };
        port = (ushort)((payload[4] << 8) | payload[5]);
        return true;
    }

    private static int Checksum(byte[] payload)
    {
        int mod = 0;
        foreach (var b in payload) mod = (mod * 256 + b) % 37;
        return mod;
    }
}
