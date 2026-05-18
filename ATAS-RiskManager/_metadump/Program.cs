using System.Reflection;
using System.Runtime.Loader;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var local = Path.Combine(root, "lib", "atas-8x");
var install = @"C:\Program Files (x86)\ATAS Platform";

AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (var dir in new[] { local, install })
    {
        var path = Path.Combine(dir, name.Name + ".dll");
        if (File.Exists(path))
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }

    return null;
};

foreach (var file in new[]
{
    Path.Combine(local, "ATAS.DataFeedsCore.dll"),
    Path.Combine(local, "ATAS.Indicators.dll"),
    Path.Combine(local, "ATAS.Strategies.dll"),
    Path.Combine(local, "OFT.Rendering.dll"),
    Path.Combine(local, "Rendering.GDIPlus.dll"),
    Path.Combine(local, "OFT.Attributes.dll"),
    Path.Combine(install, "Utils.Common.dll"),
})
{
    if (File.Exists(file))
        AssemblyLoadContext.Default.LoadFromAssemblyPath(file);
}

var strategy = AppDomain.CurrentDomain.GetAssemblies()
    .Select(a => a.GetType("ATAS.Strategies.Chart.ChartStrategy", throwOnError: false))
    .First(t => t != null)!;

Console.WriteLine(strategy.FullName);
Console.WriteLine("Constructors:");
foreach (var ctor in strategy.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
    Console.WriteLine($"  {ctor}");

Console.WriteLine("IsActivated members:");
foreach (var member in strategy.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
             .Where(m => m.Name.Contains("Activated", StringComparison.OrdinalIgnoreCase) ||
                         m.Name.Contains("Activate", StringComparison.OrdinalIgnoreCase) ||
                         m.Name.Contains("Trading", StringComparison.OrdinalIgnoreCase) ||
                         m.Name.Contains("Can", StringComparison.OrdinalIgnoreCase))
             .OrderBy(m => m.Name)
             .ThenBy(m => m.MemberType.ToString()))
{
    Console.WriteLine($"  {member.MemberType} {member}");
}

Console.WriteLine("Properties with attributes:");
foreach (var prop in strategy.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
             .Where(p => p.Name.Contains("Activated", StringComparison.OrdinalIgnoreCase) ||
                         p.Name.Contains("Trading", StringComparison.OrdinalIgnoreCase) ||
                         p.Name.Contains("Portfolio", StringComparison.OrdinalIgnoreCase) ||
                         p.Name.Contains("Security", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine($"  {prop.PropertyType.FullName} {prop.Name} CanWrite={prop.CanWrite}");
    foreach (var attr in prop.GetCustomAttributesData())
        Console.WriteLine($"    {attr.AttributeType.FullName}");
}
