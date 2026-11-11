
using System.Runtime.InteropServices;

namespace Internal;

internal static class WinTimer
{
    
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeganPeriod(uint uMilliseconds);
    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint uMilliseconds);

    public static void Begin()
    {
        if(OperatingSystem.IsWindows()) TimeBeganPeriod(1);
    }
    public static void End()
    {
        if(OperatingSystem.IsWindows()) TimeEndPeriod(1);
    }

}