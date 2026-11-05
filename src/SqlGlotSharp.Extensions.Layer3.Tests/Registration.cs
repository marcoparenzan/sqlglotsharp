using System.Runtime.CompilerServices;

namespace SqlGlotSharp.Extensions.Layer3.Tests;

internal static class Registration
{
    // The host registers the extension once at startup; the tests do the same when the assembly loads.
    [ModuleInitializer]
    internal static void Init() => Layer3.Register();
}
