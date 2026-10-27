using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using DockPad.Surfaces;

namespace DockPad.Services.StreamDock;

public sealed record DriverLoadResult(IControlSurfaceDriver? Driver, string? Error);

public static class SurfaceDriverLoader
{
    private static readonly ConcurrentDictionary<string, Lazy<DriverLoadResult>> Drivers =
        new(StringComparer.OrdinalIgnoreCase);

    public static string PluginsFolder { get; } = Path.Combine(AppContext.BaseDirectory, "plugins");

    /// <summary>
    /// Chaque chemin de pilote garde son résultat pour toute la durée du processus, y compris en cas d'échec.
    /// </summary>
    public static DriverLoadResult Load(string name, string pluginsFolder)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                return Failure(name, "The driver name must not be empty.");
            if (string.IsNullOrWhiteSpace(pluginsFolder))
                return Failure(name, "The plugins folder must not be empty.");

            var assemblyPath = Path.GetFullPath(Path.Combine(pluginsFolder, name, $"DockPad.{name}.dll"));
            // GetOrAdd peut appeler plusieurs factories ; seule la Lazy retenue construit le pilote.
            var result = Drivers.GetOrAdd(assemblyPath, path =>
                new Lazy<DriverLoadResult>(() => LoadOnce(name, path), LazyThreadSafetyMode.ExecutionAndPublication));
            return result.Value;
        }
        catch (Exception ex)
        {
            return Failure(name, $"Failed to load driver '{name}' ({ex.GetBaseException().GetType().Name}).", ex);
        }
    }

    private static DriverLoadResult LoadOnce(string name, string assemblyPath)
    {
        try
        {
            var driverFolder = Path.GetDirectoryName(assemblyPath);
            if (Directory.Exists(driverFolder) == false)
                return Failure(name, $"Driver folder not found: '{driverFolder}'.");
            if (File.Exists(assemblyPath) == false)
                return Failure(name, $"Driver assembly not found: '{assemblyPath}'.");

            var context = new SurfaceDriverLoadContext(assemblyPath);
            var assembly = context.LoadFromAssemblyPath(assemblyPath);
            var driverType = assembly.GetTypes()
                .Where(type => type.IsVisible && type.IsAbstract == false && type.ContainsGenericParameters == false)
                .Where(type => typeof(IControlSurfaceDriver).IsAssignableFrom(type))
                .Where(type => type.GetConstructor(Type.EmptyTypes) != null)
                .FirstOrDefault();
            if (driverType == null)
                return Failure(name, $"No public control surface driver with a parameterless constructor found in '{assemblyPath}'.");

            var driver = Activator.CreateInstance(driverType) as IControlSurfaceDriver;
            if (driver == null)
                return Failure(name, $"Could not create control surface driver '{driverType.FullName}'.");

            return new DriverLoadResult(driver, null);
        }
        catch (Exception ex)
        {
            return Failure(name, $"Failed to load driver '{name}' ({ex.GetBaseException().GetType().Name}).", ex);
        }
    }

    private static DriverLoadResult Failure(string name, string message, Exception? exception = null)
    {
        try
        {
            LogService.Warn(exception ?? new InvalidOperationException(message), $"Impossible de charger le pilote de surface « {name} ».");
        }
        catch (Exception)
        {
            // Un journal indisponible ne doit pas faire remonter l'échec du pilote à l'hôte.
        }
        return new DriverLoadResult(null, message);
    }
}

/// <summary>
/// Les assemblies de l'hôte sont partagées ; les autres dépendances sont résolues à côté du pilote.
/// </summary>
internal sealed class SurfaceDriverLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    internal SurfaceDriverLoadContext(string assemblyPath) : base(name: assemblyPath, isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(assemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var contract = typeof(IControlSurfaceDriver).Assembly;
        if (string.Equals(assemblyName.Name, contract.GetName().Name, StringComparison.OrdinalIgnoreCase))
            return contract;

        var shared = Default.Assemblies
            .Where(assembly => string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        if (shared != null)
            return shared;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path == null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path == null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
