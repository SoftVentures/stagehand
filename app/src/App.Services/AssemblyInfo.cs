using System.Runtime.CompilerServices;

// Plan 02+ tests cover internal helpers in App.Services (settings migrator,
// atomic-write paths). Only the test assembly is granted access.
[assembly: InternalsVisibleTo("App.Tests")]
