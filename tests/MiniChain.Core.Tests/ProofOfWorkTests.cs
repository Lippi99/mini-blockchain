namespace MiniChain.Core.Tests;

public class ProofOfWorkTests
{
    [Theory]
    [InlineData("0000ab", 4, true)]
    [InlineData("0000ab", 5, false)]
    [InlineData("000fab", 3, true)]
    [InlineData("100000", 1, false)]
    [InlineData("abc", 0, true)]
    [InlineData("00", 3, false)]
    [InlineData("000", -1, false)]
    public void MeetsDifficulty_counts_leading_zero_hex_digits(string hash, int difficulty, bool expected)
    {
        Assert.Equal(expected, ProofOfWork.MeetsDifficulty(hash, difficulty));
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x0F, 0xFF }, 3, true)]
    [InlineData(new byte[] { 0x00, 0x0F, 0xFF }, 4, false)]
    [InlineData(new byte[] { 0x00, 0x00, 0x0A }, 5, true)]
    [InlineData(new byte[] { 0x10, 0x00, 0x00 }, 1, false)]
    [InlineData(new byte[] { 0x0F, 0x00, 0x00 }, 1, true)]
    [InlineData(new byte[] { 0x00, 0x00, 0x00 }, 7, false)]
    public void MeetsDifficulty_on_bytes_checks_four_bits_per_hex_digit(byte[] hash, int difficulty, bool expected)
    {
        Assert.Equal(expected, ProofOfWork.MeetsDifficulty(hash, difficulty));
    }

    [Fact]
    public void The_byte_check_always_agrees_with_the_hex_check()
    {
        var hash = new byte[32];
        for (var first = 0; first < 256; first++)
        {
            for (var second = 0; second < 256; second++)
            {
                hash[0] = (byte)first;
                hash[1] = (byte)second;
                var hex = Convert.ToHexStringLower(hash);
                for (var difficulty = 0; difficulty <= 6; difficulty++)
                {
                    if (ProofOfWork.MeetsDifficulty(hex, difficulty) != ProofOfWork.MeetsDifficulty(hash, difficulty))
                        Assert.Fail($"Checks disagree for {hex[..6]}... at difficulty {difficulty}.");
                }
            }
        }
    }
}
