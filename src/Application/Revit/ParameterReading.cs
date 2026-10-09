namespace EgRoomTracker.Application.Revit;

/// <summary>
/// Lecture brute d'un paramètre texte, indépendante de Revit (section 3, représentation des valeurs) :
/// paramètre absent, présent sans valeur, ou valeur (éventuellement chaîne vide).
/// </summary>
public readonly record struct ParameterReading
{
    private ParameterReading(ParameterState state, string? value)
    {
        State = state;
        Value = value;
    }

    public ParameterState State { get; }

    /// <summary>Valeur lue lorsque <see cref="State"/> = <see cref="ParameterState.HasValue"/> ; chaîne vide conservée.</summary>
    public string? Value { get; }

    public static ParameterReading Absent { get; } = new(ParameterState.Absent, null);

    public static ParameterReading NoValue { get; } = new(ParameterState.NoValue, null);

    public static ParameterReading Of(string value) => new(ParameterState.HasValue, value);
}

public enum ParameterState
{
    /// <summary>Paramètre absent de l'élément : valeur null et indicateur MISSING_PARAMETER.</summary>
    Absent,

    /// <summary>Paramètre présent sans valeur : null.</summary>
    NoValue,

    /// <summary>Valeur renseignée, y compris chaîne vide.</summary>
    HasValue,
}
