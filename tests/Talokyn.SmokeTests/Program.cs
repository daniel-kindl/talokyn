using Talokyn.Core;

if (ProjectInfo.Name != "Talokyn")
{
    Console.Error.WriteLine("FAIL: wrong application name.");
    return 1;
}

if (ProjectInfo.RuntimeImplemented)
{
    Console.Error.WriteLine("FAIL: bootstrap must not claim a working runtime.");
    return 1;
}

Console.WriteLine("PASS: bootstrap identity and status.");
return 0;
