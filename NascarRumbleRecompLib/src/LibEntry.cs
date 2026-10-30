using RecompOne.Runtime.Memory;

namespace NascarRumble.Lib;

/// <summary>Single entry point the prebuilt host invokes by reflection after loading this library.</summary>
public static class LibEntry
{
    public static void Run(IMemory memory, string cuePath)
    {
        if (Environment.GetEnvironmentVariable("RUMBLE_NATIVE_TRACE") == "1")
        {
            RecompOne.Runtime.Log.BiosOn = true;
            RecompOne.Runtime.Log.CdOn = true;
            RecompOne.Runtime.Log.DmaOn = true;
            RecompOne.Runtime.Log.GpuOn = true;
            RecompOne.Runtime.Log.MdecOn = true;
            RecompOne.Runtime.Log.SdkOn = true;
            RecompOne.Runtime.Log.SpuOn = true;
        }

        NascarRumble.Host.Launcher.PrepareStartupDisplay();
        NascarRumble.Host.Launcher.RequestFonts();
        RecompOne.Runtime.Runtime.PreBoot = NascarRumble.Host.Launcher.Run;
        Recompiled.Entry.Run(memory, cuePath);
    }

    public static void Shutdown() => RecompOne.Runtime.Runtime.Shutdown();
}
