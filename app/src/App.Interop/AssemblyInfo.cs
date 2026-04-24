using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Apply a restrictive DLL search path to every P/Invoke in this assembly.
// System32 covers every Win32 API we call (user32, dwmapi, shcore, kernel32).
// This avoids DLL-planting and silences CA5392 across the assembly without
// annotating each signature individually.
// <see href="https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.defaultdllimportsearchpathsattribute"/>
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

// Plan 02 tests cover internal types (NativeMethods.*, safe-handle release logic,
// wrapper implementations). The test assembly is the only additional friend.
[assembly: InternalsVisibleTo("App.Tests")]

// Plan 02 §SidebarOverlay needs GetDpiForWindow / GetMonitorInfo / GetDpiForMonitor
// directly from App.Shell.Overlay.PerMonitorDpiHelpers + OverlayMonitor. Exposing
// a narrow public API for these would duplicate the seam for no benefit; the
// alternative (redeclaring the same P/Invokes in App.Shell) would violate the
// "all P/Invoke lives in App.Interop" convention from app/CLAUDE.md.
[assembly: InternalsVisibleTo("App.Shell")]

// Plan 02 §S9 harness calls GetGuiResources (and a few other diagnostic
// helpers) directly for the GDI/user-object soak test. Exposing a narrow
// public API for a test-only surface would bloat the App.Interop contract;
// granting the harness internals access keeps the diagnostic plumbing
// invisible to production consumers.
[assembly: InternalsVisibleTo("App.Harness")]

// Castle DynamicProxy (used by NSubstitute) needs to see internal interfaces
// (e.g. INativeWindowApi, INativeDwmApi) to generate proxies at runtime.
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
