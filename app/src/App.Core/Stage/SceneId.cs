namespace App.Core.Stage;

/// <summary>
/// Stable identity for a <see cref="Scene"/>. New scenes get a fresh GUID
/// via <see cref="New"/>; restored scenes (from a snapshot) preserve their
/// pre-crash identity.
/// </summary>
/// <param name="Value">The underlying GUID. Use <see cref="New"/> to mint one.</param>
public readonly record struct SceneId(Guid Value)
{
    /// <summary>Mints a fresh, random scene id.</summary>
    public static SceneId New() => new(Guid.NewGuid());

    /// <inheritdoc />
    public override string ToString() => Value.ToString("N");
}
