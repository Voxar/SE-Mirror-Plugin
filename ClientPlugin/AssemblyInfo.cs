// IgnoresAccessChecksTo directives consumed by Pulsar's source compiler at
// compile time. Pulsar's PublicizedAssemblies.InspectSource() does a Roslyn
// syntax-tree scan for these attributes and modifies the reference assemblies'
// metadata to make internal members appear public — so the compiled IL never
// attempts an internal access in the first place. This is what
// CameraLCD-Remastered relies on and what makes Krafs.Publicizer's runtime
// attribute redundant under Pulsar's source compiler.
//
// Krafs.Publicizer (when `dotnet build` is run locally) also emits assembly-
// level IgnoresAccessChecksTo for the same targets — these source lines are
// duplicates from Krafs's perspective. Roslyn accepts duplicate
// [assembly: IgnoresAccessChecksTo("...")] declarations with AllowMultiple,
// so both build paths produce a valid assembly.

using System.Runtime.CompilerServices;

[assembly: IgnoresAccessChecksTo("Sandbox.Game")]
[assembly: IgnoresAccessChecksTo("SpaceEngineers.Game")]
[assembly: IgnoresAccessChecksTo("VRage.Render")]
[assembly: IgnoresAccessChecksTo("VRage.Render11")]
