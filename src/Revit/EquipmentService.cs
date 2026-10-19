using Autodesk.Revit.DB;
using EgRoomTracker.Application.Units;
using EgRoomTracker.Contracts.Configuration;

namespace EgRoomTracker.Revit;

/// <summary>
/// Équipements du document principal, rattachés à la pièce qui les contient. Catégories par défaut, toutes disciplines :
/// équipements mécaniques (CVC : centrales, ventilo-convecteurs, pompes à chaleur, chaudières, radiateurs…), électriques
/// (tableaux, armoires), de plomberie (ballons, surpresseurs…), équipements spécialisés et protection incendie.
/// Remplaçables par <c>elementCategories.equipment</c> dans <c>settings.json</c>.
/// </summary>
public sealed class EquipmentService(RevitExecutionContext context, IUnitConversionService units)
    : InRoomElementServiceBase(context, units)
{
    public override ElementKind Kind => ElementKind.Equipment;

    protected override IReadOnlyList<BuiltInCategory> Categories { get; } =
    [
        BuiltInCategory.OST_MechanicalEquipment,
        BuiltInCategory.OST_ElectricalEquipment,
        BuiltInCategory.OST_PlumbingEquipment,
        BuiltInCategory.OST_SpecialityEquipment,
        BuiltInCategory.OST_FireProtection,
    ];
}
