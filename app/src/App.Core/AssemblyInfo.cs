using System.Runtime.CompilerServices;

// Plan 02+ tests cover internal helpers in App.Core (domain invariants, layout
// engine internals). Only the test assembly is granted access.
[assembly: InternalsVisibleTo("App.Tests")]

// App.Services consumes internal helpers that belong semantically to the Core
// domain but require IO/logging wiring to be useful (e.g. WindowClassExclusions
// invoked from WindowFilter in the Services layer). Keeping the helpers
// internal-to-Core preserves the "Core owns the policy, Services owns the
// wiring" split without exposing them to consumers outside the app.
[assembly: InternalsVisibleTo("App.Services")]
