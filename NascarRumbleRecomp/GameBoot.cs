using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using RecompOne.Runtime.Memory;

namespace FullRumble;

/// <summary>
/// Boots the recompiled game. Kept in its own class with a BCL-only signature so the host's
/// command-line paths never JIT this method and therefore never load the RecompOne runtime or
/// its native libraries. The game library (NascarRumbleRecompLib.dll) is produced from the
/// player's own disc and loaded by reflection, so the host ships and runs without it.
/// </summary>
internal static class GameBoot
{
    private const string LibraryFile = "NascarRumbleRecompLib.dll";
    private const string EntryPoint = "NascarRumble.Lib.LibEntry";
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    public static int Run(string cuePath)
    {
        string libraryPath = Path.Combine(AppContext.BaseDirectory, LibraryFile);
        if (!File.Exists(libraryPath))
        {
            Console.Error.WriteLine(
                "The game library is not built yet. Build it from a disc you own with:");
            Console.Error.WriteLine("    python tools/build_lib.py");
            return 1;
        }

        // Creating the memory loads RecompOne.Runtime into the default context first, so the
        // library resolves its IMemory against the same assembly the host already loaded.
        var memory = new PSMemory();
        nint console = GetConsoleWindow();
        try
        {
            Assembly library = AssemblyLoadContext.Default.LoadFromAssemblyPath(libraryPath);
            Type entry = library.GetType(EntryPoint, throwOnError: true)!;
            MethodInfo run = entry.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(EntryPoint, "Run");

            // The game opens its own window; drop the console so a launch shows only the game.
            if (console != 0)
                ShowWindow(console, SW_HIDE);

            run.Invoke(null, new object?[] { memory, cuePath });
            return 0;
        }
        catch (Exception error)
        {
            if (console != 0)
                ShowWindow(console, SW_SHOW);
            Console.Error.WriteLine("The game encountered an error and was closed safely.");
            Console.Error.WriteLine(Describe(error));
            return 1;
        }
        finally
        {
            try
            {
                RecompOne.Runtime.Runtime.Shutdown();
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"Warning while closing the runtime: {error.Message}");
            }
        }
    }

    private static string Describe(Exception error) =>
        error is TargetInvocationException { InnerException: { } inner } ? inner.ToString() : error.ToString();
}

