using System;

namespace CodexSwitch.Sensors;

internal static class CpuRegisters
{
    public static double? Amd(uint register)
    {
        if (register is 0 or uint.MaxValue) return null;
        var celsius = (register >> 21) * 0.125 - ((register & (1u << 19)) != 0 ? 49 : 0);
        return double.IsFinite(celsius) && celsius is >= -50 and <= 150 ? celsius : null;
    }
    public static double? Intel(ulong target, ulong status)
    {
        var maximum = (target >> 16) & 0xff;
        if (maximum is < 50 or > 125 || (status & (1ul << 31)) == 0) return null;
        var celsius = (double)maximum - ((status >> 16) & 0x7f);
        return celsius is >= -50 and <= 150 ? celsius : null;
    }
    public static int Family(int eax) => ((eax >> 8) & 15) == 15 ? 15 + ((eax >> 20) & 255) : (eax >> 8) & 15;
}
