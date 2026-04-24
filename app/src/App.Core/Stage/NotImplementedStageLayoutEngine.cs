using System.Collections.Immutable;

namespace App.Core.Stage;

/// <summary>
/// Placeholder <see cref="IStageLayoutEngine"/>. Every member throws
/// <see cref="NotImplementedException"/> until Plan 02 implements the layout
/// algorithm.
/// </summary>
public sealed class NotImplementedStageLayoutEngine : IStageLayoutEngine
{
    /// <inheritdoc />
    public ImmutableDictionary<IntPtr, App.Interop.Rect> ComputeThumbnailLayout(
        App.Interop.Rect sidebarRect,
        IReadOnlyList<App.Interop.WindowSnapshot> windows
    ) => throw new NotImplementedException("Implemented in Plan 02.");
}
