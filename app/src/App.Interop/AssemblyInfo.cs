using System.Runtime.InteropServices;

// Apply a restrictive DLL search path to every P/Invoke in this assembly.
// System32 covers every Win32 API we call (user32, dwmapi, shcore, kernel32).
// This avoids DLL-planting and silences CA5392 across the assembly without
// annotating each signature individually.
// <see href="https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.defaultdllimportsearchpathsattribute"/>
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
