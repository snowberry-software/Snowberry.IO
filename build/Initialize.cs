using System;
using System.Runtime.CompilerServices;

namespace _build;

public static class Initialize
{
    [ModuleInitializer]
    public static void ModuleInitialize() => Environment.SetEnvironmentVariable("NUKE_TELEMETRY_OPTOUT", "1");
}
