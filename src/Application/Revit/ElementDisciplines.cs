namespace EgRoomTracker.Application.Revit;

/// <summary>
/// Discipline d'un équipement ou d'un terminal d'après sa catégorie Revit (nom de <c>BuiltInCategory</c>) : <c>cvc</c>,
/// <c>plomberie</c>, <c>electricite</c>, sinon <c>autres</c> (équipements spécialisés, protection incendie, toute catégorie
/// ajoutée par <c>elementCategories</c>). Les sections de sortie (TRC) sont rangées par discipline pour alléger les listes.
/// </summary>
public static class ElementDisciplines
{
    public const string Cvc = "cvc";
    public const string Plumbing = "plomberie";
    public const string Electrical = "electricite";
    public const string Other = "autres";

    public static IReadOnlyList<string> All { get; } = [Cvc, Plumbing, Electrical, Other];

    private static readonly Dictionary<string, string> ByCategory = new(StringComparer.Ordinal)
    {
        // CVC
        ["OST_MechanicalEquipment"] = Cvc,
        ["OST_DuctTerminal"] = Cvc,
        ["OST_MechanicalControlDevices"] = Cvc,

        // Plomberie
        ["OST_PlumbingEquipment"] = Plumbing,
        ["OST_PlumbingFixtures"] = Plumbing,
        ["OST_Sprinklers"] = Plumbing,

        // Électricité, courants forts et faibles
        ["OST_ElectricalEquipment"] = Electrical,
        ["OST_ElectricalFixtures"] = Electrical,
        ["OST_LightingFixtures"] = Electrical,
        ["OST_LightingDevices"] = Electrical,
        ["OST_CommunicationDevices"] = Electrical,
        ["OST_DataDevices"] = Electrical,
        ["OST_TelephoneDevices"] = Electrical,
        ["OST_SecurityDevices"] = Electrical,
        ["OST_FireAlarmDevices"] = Electrical,
        ["OST_NurseCallDevices"] = Electrical,
    };

    /// <summary>Discipline d'une catégorie (nom de <c>BuiltInCategory</c>, ex. <c>OST_DuctTerminal</c>) ; <see cref="Other"/> par défaut.</summary>
    public static string Of(string builtInCategoryName) => ByCategory.GetValueOrDefault(builtInCategoryName, Other);
}
