using Autodesk.Revit.DB;
using EgRoomTracker.Application.Units;
using EgRoomTracker.Contracts.Configuration;

namespace EgRoomTracker.Revit;

/// <summary>
/// Terminaux du document principal (points d'extrémité des réseaux), rattachés à la pièce qui les contient. Catégories par
/// défaut, toutes disciplines : CVC (terminaux d'air : bouches, diffuseurs, grilles ; dispositifs de commande mécanique),
/// plomberie (appareils sanitaires, sprinklers) et électricité courants forts et faibles (appareils électriques : prises,
/// interrupteurs ; luminaires et dispositifs d'éclairage ; communication, données, téléphone, sécurité, alarme incendie,
/// appel malade). Remplaçables par <c>elementCategories.terminals</c> dans <c>settings.json</c>.
/// </summary>
public sealed class TerminalService(RevitExecutionContext context, IUnitConversionService units)
    : InRoomElementServiceBase(context, units)
{
    public override ElementKind Kind => ElementKind.Terminals;

    protected override IReadOnlyList<BuiltInCategory> Categories { get; } =
    [
        BuiltInCategory.OST_DuctTerminal,
        BuiltInCategory.OST_MechanicalControlDevices,
        BuiltInCategory.OST_PlumbingFixtures,
        BuiltInCategory.OST_Sprinklers,
        BuiltInCategory.OST_ElectricalFixtures,
        BuiltInCategory.OST_LightingFixtures,
        BuiltInCategory.OST_LightingDevices,
        BuiltInCategory.OST_CommunicationDevices,
        BuiltInCategory.OST_DataDevices,
        BuiltInCategory.OST_TelephoneDevices,
        BuiltInCategory.OST_SecurityDevices,
        BuiltInCategory.OST_FireAlarmDevices,
        BuiltInCategory.OST_NurseCallDevices,
    ];
}
