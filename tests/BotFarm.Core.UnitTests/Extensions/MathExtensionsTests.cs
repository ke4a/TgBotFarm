using BotFarm.Core.Extensions;

namespace BotFarm.Core.UnitTests.Extensions
{
    [TestFixture]
    public class MathExtensionsTests
    {
        [TestCase(0UL, ExpectedResult = long.MinValue)]
        [TestCase(ulong.MaxValue, ExpectedResult = long.MaxValue)]
        [TestCase(123456789UL, ExpectedResult = long.MinValue + 123456789)]
        public long MapToLong_ShouldReturnExpectedResult(ulong ulongValue)
        {
            return ulongValue.MapToLong();
        }

        [TestCase(long.MinValue, ExpectedResult = 0UL)]
        [TestCase(long.MaxValue, ExpectedResult = ulong.MaxValue)]
        [TestCase(long.MinValue + 123456789, ExpectedResult = 123456789UL)]
        public ulong MapToUlong_ShouldReturnExpectedResult(long longValue)
        {
            return longValue.MapToUlong();
        }
    }
}
