using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BookingCQBLaser.Domain.Enums
{
    public static class PackageTypeExtensions
    {
        public static int GetBaseDurationMinutes(this PackageType package)
        {
            return package switch
            {
                PackageType.S1 => 90,
                PackageType.S2 => 90,
                PackageType.Premium => 120,
                PackageType.Max => 120,
                PackageType.U1 => 90,
                PackageType.U2 => 120,
                PackageType.U3 => 120,
                PackageType.Combat => 120,
                _ => throw new ArgumentOutOfRangeException(nameof(package), package, null)
            };
        }

        public static int GetClientGameDurationMinutes(this PackageType package)
        {
            return package switch
            {
                PackageType.S1 => 50,
                PackageType.S2 => 60,
                PackageType.Premium => 70,
                PackageType.Max => 80,
                PackageType.U1 => 80,
                PackageType.U2 => 90,
                PackageType.U3 => 100,
                PackageType.Combat => 110,
                _ => throw new ArgumentOutOfRangeException(nameof(package), package, null)
            };
        }

        public static int GetPrice(this PackageType package)
        {
            return package switch
            {
                PackageType.S1 => 55,
                PackageType.S2 => 65,
                PackageType.Premium => 85,
                PackageType.Max => 95,
                PackageType.U1 => 55,
                PackageType.U2 => 65,
                PackageType.U3 => 85,
                PackageType.Combat => 95,
                _ => throw new ArgumentOutOfRangeException(nameof(package), package, null)
            };
        }
    }
}
