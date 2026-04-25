using System.Windows.Media;
using App.Interop;

namespace App.Harness;

/// <summary>
/// View-model wrapper around a <see cref="WindowSnapshot"/> that adds an
/// optional WPF <see cref="ImageSource"/> preview rendered from a
/// <c>PrintWindow</c> capture. Lives in the harness only — the production
/// stage layer renders previews via live DWM thumbnails directly into the
/// sidebar overlay HWND, which doesn't need a managed <see cref="ImageSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Preview"/> is <see langword="null"/> when the
/// <c>PrintWindow</c> capture failed (degenerate window rect, hardware-
/// accelerated surface, security-protected content like Netflix, GDI handle
/// pressure). The XAML template falls back to an empty placeholder.
/// </para>
/// <para>
/// <b>No forwarder properties.</b> The XAML binds directly to
/// <c>Snapshot.Title</c>, <c>Snapshot.ClassName</c> etc. Earlier revisions of
/// this type exposed convenience forwarders with expression-bodied getters
/// (<c>public string Title =&gt; Snapshot.Title;</c>) — those are strictly
/// read-only at the IL level, and the inline-<c>Run.Text</c> bindings inside a
/// <c>TextBlock</c> default to TwoWay in some WPF revisions, which then crashes
/// at template instantiation with <c>InvalidOperationException</c>. Binding
/// through <c>Snapshot.*</c> sidesteps the issue: those are record
/// init-properties, which WPF treats as bindable in OneWay mode.
/// </para>
/// </remarks>
public sealed record WindowListItem(WindowSnapshot Snapshot, ImageSource? Preview);
