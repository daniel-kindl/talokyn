using Talokyn.Core;

if (args.Length == 1 && args[0] == "--version")
{
    Console.WriteLine($"{ProjectInfo.Name} {ProjectInfo.Version} (bootstrap only)");
    return 0;
}

Console.Error.WriteLine("Talokyn is not implemented yet. See docs/design/SPEC.md.");
return 2;
