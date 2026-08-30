using System.IO;
using App.Core.Branding;

namespace App.Services.Snapshot;

/// <summary>
/// Production <see cref="ISnapshotPathProvider"/>: resolves to
/// <c>%LOCALAPPDATA%\{BrandConstants.ProductFolderName}\state\snapshot.json</c>.
/// </summary>
public sealed class AppDataSnapshotPathProvider : ISnapshotPathProvider
{
    /// <summary>Creates the parent directory and resolves the file path.</summary>
    public AppDataSnapshotPathProvider()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData
        );
        var stateFolder = Path.Combine(localAppData, BrandConstants.ProductFolderName, "state");
        Directory.CreateDirectory(stateFolder);
        SnapshotFilePath = Path.Combine(stateFolder, "snapshot.json");
    }

    /// <inheritdoc />
    public string SnapshotFilePath { get; }
}
