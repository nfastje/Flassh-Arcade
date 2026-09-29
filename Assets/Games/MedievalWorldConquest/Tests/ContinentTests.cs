using MedievalWorldConquest.Simulation;
using NUnit.Framework;

namespace MedievalWorldConquest.Tests
{
    /// <summary>Continent numbers, as on a Tribal Wars map: 10 × 10 blocks of 25 × 25 fields.</summary>
    public class ContinentTests
    {
        [Test]
        public void ContinentsAreNumberedUpAndAcrossTheMap()
        {
            Assert.AreEqual("K00", World.ContinentName(0, 0));
            Assert.AreEqual("K00", World.ContinentName(24, 24));
            Assert.AreEqual("K01", World.ContinentName(25, 0), "across the map: the units digit");
            Assert.AreEqual("K10", World.ContinentName(0, 25), "up the map: the tens digit");
            Assert.AreEqual("K55", World.ContinentName(World.MapSize / 2, World.MapSize / 2), "the middle, where a world begins");
            Assert.AreEqual("K99", World.ContinentName(World.MapSize - 1, World.MapSize - 1));
        }
    }
}
