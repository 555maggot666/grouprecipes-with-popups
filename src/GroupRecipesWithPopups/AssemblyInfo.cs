using System.Runtime.CompilerServices;

// The harness plugin in tools/Harness drives the real wire format and the real server receive path from
// inside a running game. It is a separate test-only assembly and never part of the Thunderstore package.
[assembly: InternalsVisibleTo("GroupRecipesWithPopups.Harness")]
