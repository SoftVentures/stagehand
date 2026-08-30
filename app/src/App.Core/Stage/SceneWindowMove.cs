using App.Interop;

namespace App.Core.Stage;

/// <summary>
/// One "move this window from rect A to rect B" instruction within a
/// <see cref="SceneSwapPlan"/>.
/// </summary>
/// <param name="Identity">Window identity (HWND lookup-key valid for this swap).</param>
/// <param name="From">Source rectangle in physical pixels (informational; not used by the OS).</param>
/// <param name="To">Destination rectangle in physical pixels.</param>
public sealed record SceneWindowMove(WindowIdentity Identity, Rect From, Rect To);
