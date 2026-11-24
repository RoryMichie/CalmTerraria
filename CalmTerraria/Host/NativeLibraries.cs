using System.Reflection;
using System.Runtime.InteropServices;
using Terraria;
using Terraria.ModLoader;

namespace CalmTerraria.Host;

/// <summary>Gets SRUI's native libraries out of the .tmod and in front
/// of the loader. A mod is loaded from a stream, so nothing of it is on
/// disk and the operating system cannot find a DLL beside it: each
/// library is written to a folder under the save path, loaded from
/// there by full path, and handed to SRUI's P/Invokes by name through
/// an import resolver on each SRUI assembly.</summary>
internal static class NativeLibraries
{
    /// <summary>In load order: cosmos imports phonon by name, and
    /// Windows satisfies that import from a module already in the
    /// process.</summary>
    private static readonly string[] Names = { "prism", "phonon", "cosmos" };

    /// <summary>Process-wide, deliberately: a native library outlives
    /// the mod that loaded it across a mod reload, and its file stays
    /// locked until the game exits.</summary>
    private static readonly Dictionary<string, IntPtr> Handles = new();

    public static void Install(Mod mod)
    {
        var folder = Path.Combine(Main.SavePath, "CalmTerraria", "native");
        Directory.CreateDirectory(folder);
        lock (Handles)
        {
            foreach (var name in Names)
            {
                if (Handles.ContainsKey(name))
                    continue;
                var path = Path.Combine(folder, name + ".dll");
                Extract(mod, $"native/{name}.dll", path);
                Handles[name] = NativeLibrary.Load(path);
            }
        }
        Resolve(typeof(Srui.SruiApp).Assembly);
        Resolve(typeof(Srui.Audio.SoundManager).Assembly);
    }

    private static void Extract(Mod mod, string resource, string path)
    {
        var bytes = mod.GetFileBytes(resource)
            ?? throw new FileNotFoundException($"{resource} is not in the mod file.");
        // A copy already there is either current or held open by an
        // earlier load in this process; only a different size is worth
        // a write, and that one may fail loudly.
        if (File.Exists(path) && new FileInfo(path).Length == bytes.Length)
            return;
        File.WriteAllBytes(path, bytes);
    }

    private static void Resolve(Assembly assembly)
    {
        try
        {
            NativeLibrary.SetDllImportResolver(assembly, static (name, _, _) =>
            {
                lock (Handles)
                    return Handles.TryGetValue(name, out var handle) ? handle : IntPtr.Zero;
            });
        }
        catch (InvalidOperationException)
        {
            // This assembly instance already has its resolver.
        }
    }
}
