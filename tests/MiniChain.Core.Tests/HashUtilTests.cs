using System.Text;

namespace MiniChain.Core.Tests;

public class HashUtilTests
{
    [Theory]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    public void Sha256Hex_matches_known_test_vectors(string input, string expected)
    {
        Assert.Equal(expected, HashUtil.Sha256Hex(input));
        Assert.Equal(expected, HashUtil.Sha256Hex(Encoding.UTF8.GetBytes(input)));
    }

    [Fact]
    public void A_one_character_change_changes_the_whole_hash()
    {
        var a = HashUtil.Sha256Hex("hello world");
        var b = HashUtil.Sha256Hex("hello worle");

        Assert.NotEqual(a, b);
        Assert.Equal(64, a.Length);
        Assert.Equal(a, a.ToLowerInvariant());
    }
}
