using System;

namespace MedievalWorldConquest.Simulation
{
    public enum TerrainType
    {
        Grass,
        /// <summary>A field of woods: scenery on the empty land only (no effect on play, and not on the minimap).</summary>
        Forest,
        /// <summary>A hill: scenery, like woods.</summary>
        Hills,
        Water,
    }

    /// <summary>
    /// The world's landscape, worked out from the world seed rather than saved: the same seed always gives the same
    /// map. Uses its own noise (not Unity's) so it's plain C# and identical everywhere it's computed.
    /// </summary>
    public static class Terrain
    {
        /// <summary>Fields this close to the map center are always open grass, so the player never starts in a lake.</summary>
        public const double StartClearingRadius = 4;
        /// <summary>The chance of any field being a pond.</summary>
        public const double PondChance = 0.005;

        /// <summary>The chance of any (other) field being woods, or a hill: single fields of scenery, scattered.</summary>
        public const double WoodsChance = 0.06, HillsChance = 0.03;

        public static TerrainType At(int seed, int x, int y)
        {
            if (x < 0 || y < 0 || x >= World.MapSize || y >= World.MapSize) return TerrainType.Water;
            double c = World.MapSize / 2.0;
            if ((x - c) * (x - c) + (y - c) * (y - c) < StartClearingRadius * StartClearingRadius) return TerrainType.Grass;

            // Water is only the odd pond, a single field, as on Tribal Wars' maps: about one field in two hundred.
            if (Hash(seed ^ 0x6A09E667, x, y) < PondChance) return TerrainType.Water;
            // The rest is open plains, with a field of woods or a hill here and there to break it up (scenery only:
            // villages may stand anywhere but water, and the minimap shows them all as plains).
            double scenery = Hash(seed ^ 0x3C6EF372, x, y);
            if (scenery < WoodsChance) return TerrainType.Forest;
            if (scenery < WoodsChance + HillsChance) return TerrainType.Hills;
            return TerrainType.Grass;
        }

        /// <summary>A repeatable pseudo-random number in 0..1 for a grid point (or any pair of numbers).</summary>
        internal static double Hash(int seed, int x, int y)
        {
            unchecked
            {
                uint h = (uint)seed * 374761393u + (uint)x * 668265263u + (uint)y * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (double)0x1000000;
            }
        }
    }
}
