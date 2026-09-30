namespace MedievalWorldConquest.Simulation
{
    /// <summary>
    /// How fast an attack marches, which is all a defender can tell about it before it lands: as in Tribal Wars,
    /// the distance and the arrival time give away its slowest unit, and so roughly what it might be. A
    /// scout-speed attack is only scouts; a ram-speed one may bring down the wall; a nobleman-speed one may take
    /// the village. (Or it's a fake: a single slow unit sent to draw the defenders' support away.)
    /// </summary>
    public enum AttackSpeed
    {
        Scout,
        /// <summary>Light cavalry or mounted archers.</summary>
        Cavalry,
        HeavyCavalry,
        /// <summary>Spearmen, axemen or archers.</summary>
        Infantry,
        Swordsman,
        /// <summary>Rams or catapults.</summary>
        Siege,
        Nobleman,
    }

    public static class AttackSpeeds
    {
        /// <summary>The speed of a set of troops: that of its slowest unit.</summary>
        public static AttackSpeed Of(int[] troops)
        {
            switch (World.SlowestUnit(troops) ?? UnitType.Scout)
            {
                case UnitType.Scout: return AttackSpeed.Scout;
                case UnitType.LightCavalry:
                case UnitType.MountedArcher: return AttackSpeed.Cavalry;
                case UnitType.HeavyCavalry: return AttackSpeed.HeavyCavalry;
                case UnitType.Swordsman: return AttackSpeed.Swordsman;
                case UnitType.Ram:
                case UnitType.Catapult: return AttackSpeed.Siege;
                case UnitType.Nobleman: return AttackSpeed.Nobleman;
                default: return AttackSpeed.Infantry;
            }
        }

        /// <summary>The unit whose icon stands for a speed.</summary>
        public static UnitType Icon(AttackSpeed speed) => speed switch
        {
            AttackSpeed.Scout => UnitType.Scout,
            AttackSpeed.Cavalry => UnitType.LightCavalry,
            AttackSpeed.HeavyCavalry => UnitType.HeavyCavalry,
            AttackSpeed.Swordsman => UnitType.Swordsman,
            AttackSpeed.Siege => UnitType.Ram,
            AttackSpeed.Nobleman => UnitType.Nobleman,
            _ => UnitType.Axeman,
        };

        /// <summary>What a speed could mean, in words.</summary>
        public static string Describe(AttackSpeed speed) => speed switch
        {
            AttackSpeed.Scout => "Scout speed: only scouts.",
            AttackSpeed.Cavalry => "Light cavalry speed: a raid, most likely.",
            AttackSpeed.HeavyCavalry => "Heavy cavalry speed.",
            AttackSpeed.Infantry => "Infantry speed (spearmen, axemen or archers).",
            AttackSpeed.Swordsman => "Swordsman speed.",
            AttackSpeed.Siege => "Ram speed: rams or catapults, to bring down the wall or a building. Danger (or a fake).",
            _ => "Nobleman speed: it may take the village. Danger (or a fake).",
        };

        /// <summary>Rams, catapults or noblemen: an attack that can do lasting harm.</summary>
        public static bool IsDangerous(AttackSpeed speed) => speed >= AttackSpeed.Siege;

        /// <summary>Slow enough that it's likely a real attack worth calling for help against (swordsmen or slower).</summary>
        public static bool WorthHelp(AttackSpeed speed) => speed >= AttackSpeed.Swordsman;
    }
}
