// Declaration of System.Runtime.CompilerServices.IsExternalInit.
// .NET Framework doesn't ship this type; the C# compiler requires it to be
// resolvable when emitting init-only setters (record struct, record class,
// or any property with `init`). Declaring it in source makes record-struct
// value objects work on net48 without adding a NuGet polyfill package.

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
