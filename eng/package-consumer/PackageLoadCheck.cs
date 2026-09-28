using System.Reflection;

/// <summary>
/// Loads every Tenantry assembly in the output and all of its types, so a dependency that a package needs
/// but does not bring (or brings at a version its code cannot use) fails here rather than in an application.
/// </summary>
internal static class PackageLoadCheck
{
    public static void Run()
    {
        var paths = Directory.GetFiles(AppContext.BaseDirectory, "Tenantry.*.dll");
        if (paths.Length == 0)
        {
            throw new InvalidOperationException($"No Tenantry assemblies in {AppContext.BaseDirectory}");
        }

        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            var assembly = Assembly.LoadFrom(path);
            try
            {
                var types = assembly.GetTypes();
                var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                Console.WriteLine($"{assembly.GetName().Name} {version}: {types.Length} types loaded");
            }
            catch (ReflectionTypeLoadException e)
            {
                foreach (var loaderException in e.LoaderExceptions.OfType<Exception>().Select(x => x.Message).Distinct())
                {
                    Console.Error.WriteLine($"{assembly.GetName().Name}: {loaderException}");
                }

                throw;
            }
        }
    }
}
