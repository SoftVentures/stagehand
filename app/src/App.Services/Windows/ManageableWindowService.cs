using App.Core.Stage;
using App.Interop;
using Microsoft.Extensions.Logging;

namespace App.Services.Windows;

/// <summary>
/// Default <see cref="IManageableWindowService"/> implementation: asks the
/// <see cref="IWindowEnumerator"/> for raw snapshots and keeps those the
/// <see cref="IWindowFilter"/> accepts.
/// </summary>
public sealed class ManageableWindowService : IManageableWindowService
{
    private static readonly Action<ILogger, int, int, Exception?> LogEnumerationResult =
        LoggerMessage.Define<int, int>(
            LogLevel.Debug,
            new EventId(2201, nameof(LogEnumerationResult)),
            "Window enumeration: {Filtered} of {Total} snapshots passed IWindowFilter."
        );

    private readonly IWindowEnumerator _enumerator;
    private readonly IWindowFilter _filter;
    private readonly ILogger<ManageableWindowService> _log;

    public ManageableWindowService(
        IWindowEnumerator enumerator,
        IWindowFilter filter,
        ILogger<ManageableWindowService> log
    )
    {
        ArgumentNullException.ThrowIfNull(enumerator);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(log);

        _enumerator = enumerator;
        _filter = filter;
        _log = log;
    }

    /// <inheritdoc />
    public IReadOnlyList<WindowSnapshot> GetManageableWindows()
    {
        IReadOnlyList<WindowSnapshot> snapshots = _enumerator.GetManageableWindows();
        var result = new List<WindowSnapshot>(snapshots.Count);
        foreach (WindowSnapshot snap in snapshots)
        {
            if (_filter.IsManageable(snap))
            {
                result.Add(snap);
            }
        }
        LogEnumerationResult(_log, result.Count, snapshots.Count, null);
        return result;
    }
}
