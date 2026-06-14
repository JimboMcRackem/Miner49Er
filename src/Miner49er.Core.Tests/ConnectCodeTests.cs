using System.Linq;
using Miner49er.Core.Net;
using Xunit;

public class ConnectCodeTests
{
    [Theory]
    [InlineData(192, 168, 1, 5, 27649)]
    [InlineData(203, 0, 113, 7, 27649)]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(255, 255, 255, 255, 65535)]
    [InlineData(10, 0, 0, 1, 1)]
    public void Round_trips_ip_and_port(byte a, byte b, byte c, byte d, int port)
    {
        var code = ConnectCode.Encode(new[] { a, b, c, d }, (ushort)port);
        Assert.True(ConnectCode.TryDecode(code, out var ip, out var back));
        Assert.Equal(new byte[] { a, b, c, d }, ip);
        Assert.Equal((ushort)port, back);
    }

    [Fact]
    public void Code_is_eleven_chars()
    {
        var code = ConnectCode.Encode(new byte[] { 203, 0, 113, 7 }, 27649);
        Assert.Equal(11, code.Length);
    }

    [Fact]
    public void Decode_is_case_insensitive_and_ignores_spaces_and_dashes()
    {
        var code = ConnectCode.Encode(new byte[] { 203, 0, 113, 7 }, 27649);
        var noisy = "  " + string.Join("-", code.ToLowerInvariant().Select(c => c.ToString())) + " ";
        Assert.True(ConnectCode.TryDecode(noisy, out var ip, out var port));
        Assert.Equal(new byte[] { 203, 0, 113, 7 }, ip);
        Assert.Equal((ushort)27649, port);
    }

    [Fact]
    public void Rejects_a_single_character_typo()
    {
        var code = ConnectCode.Encode(new byte[] { 203, 0, 113, 7 }, 27649).ToCharArray();
        code[0] = code[0] == '0' ? '1' : '0';   // flip a data char; checksum char untouched
        Assert.False(ConnectCode.TryDecode(new string(code), out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("SHORT")]
    [InlineData("WAYTOOLONG12345")]
    [InlineData("!!!!!!!!!!!")]
    public void Rejects_malformed_input(string bad)
    {
        Assert.False(ConnectCode.TryDecode(bad, out _, out _));
    }

    [Fact]
    public void Rejects_null()
    {
        Assert.False(ConnectCode.TryDecode(null!, out _, out _));
    }
}
