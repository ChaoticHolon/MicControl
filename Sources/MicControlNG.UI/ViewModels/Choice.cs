namespace MicControlNG.ViewModels;

/// <summary>An item for a ComboBox: the stored value plus user-facing text.</summary>
public sealed record Choice(object? Value, string Name, string? Description = null)
{
    public override string ToString() => Name;
}
