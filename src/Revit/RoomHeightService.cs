using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Application.Units;

namespace EgRoomTracker.Revit;

/// <summary>
/// Hauteur d'une pièce par lancer de rayons, reprise de <c>GetRoomMinHeightByRaytracing</c> (application TRC) :
/// <list type="bullet">
/// <item>rayons verticaux vers le haut depuis un point à 0,1 pied au-dessus du bas de la pièce, sur une grille de pas
/// <c>heightStepM</c> dans la boîte englobante, aux seuls points situés dans la pièce (<c>IsPointInRoom</c>) ;</item>
/// <item>obstacles : Plafonds, Sols, Toits et Ossature, <b>modèle principal et liens</b> (<c>FindReferencesInRevitLinks</c>) ;</item>
/// <item>impacts à moins de 0,1 m ignorés ; hauteur minimale = plus courte distance, avec catégorie et document d'origine
/// (titre du modèle ou du lien) ; minimum par catégorie conservé ;</item>
/// <item>hauteur renseignée = paramètre <c>ROOM_HEIGHT</c>, lue à part.</item>
/// </list>
/// La vue 3D nécessaire (<c>ReferenceIntersector</c>) est la vue d'export IFC « EXP…IFC » si elle existe, sinon une vue 3D
/// temporaire créée une seule fois (et non une par pièce, comme dans TRC), tous les jeux de travail visibles. Si aucun point
/// de la grille ne tombe dans une petite pièce, le point d'insertion de la pièce est essayé. Les vues restent temporaires : le
/// RVT n'est jamais sauvegardé.
/// </summary>
public sealed class RoomHeightService(RevitExecutionContext context, IUnitConversionService units) : IRoomHeightService
{
    private static readonly BuiltInCategory[] Obstacles =
    [
        BuiltInCategory.OST_Ceilings,
        BuiltInCategory.OST_Floors,
        BuiltInCategory.OST_Roofs,
        BuiltInCategory.OST_StructuralFraming,
    ];

    /// <summary>Hauteur du point de départ des rayons au-dessus du bas de la pièce (pieds, valeur de TRC).</summary>
    private const double StartHeightFeet = 0.1;

    private View3D? _view;
    private ReferenceIntersector? _intersector;

    public RoomHeightMeasurement Measure(long roomElementId, double stepM)
    {
        var document = context.RequireDocument();
        if (document.GetElement(new ElementId(roomElementId)) is not Room room)
        {
            throw new InvalidOperationException($"Pièce {roomElementId} introuvable dans le document.");
        }

        var declared = ReadDeclaredHeight(room);
        var intersector = Intersector(document);
        var accumulator = new HeightHitAccumulator();
        var samples = 0;

        var box = room.get_BoundingBox(_view);
        if (box is not null)
        {
            var step = UnitUtils.ConvertToInternalUnits(stepM, UnitTypeId.Meters);
            var z = box.Min.Z + StartHeightFeet;
            for (var x = box.Min.X; x <= box.Max.X; x += step)
            {
                for (var y = box.Min.Y; y <= box.Max.Y; y += step)
                {
                    samples += Cast(document, room, intersector, new XYZ(x, y, z), accumulator);
                }
            }

            if (samples == 0 && room.Location is LocationPoint location)
            {
                samples += Cast(document, room, intersector, new XYZ(location.Point.X, location.Point.Y, z), accumulator);
            }
        }

        return new RoomHeightMeasurement(declared, accumulator.MinM, accumulator.LimitingCategory, accumulator.LimitingDocument, accumulator.ByCategoryM, samples);
    }

    /// <summary>Lance un rayon vertical depuis <paramref name="origin"/> s'il est dans la pièce ; retourne le nombre de rayons lancés (0 ou 1).</summary>
    private int Cast(Document document, Room room, ReferenceIntersector intersector, XYZ origin, HeightHitAccumulator accumulator)
    {
        if (!room.IsPointInRoom(origin))
        {
            return 0;
        }

        var hits = intersector.Find(origin, XYZ.BasisZ);
        if (hits is null)
        {
            return 1;
        }

        foreach (var hit in hits)
        {
            var distance = units.ToMeters(hit.Proximity);
            if (distance < HeightHitAccumulator.MinHitDistanceM)
            {
                continue;
            }

            var reference = hit.GetReference();
            var element = document.GetElement(reference);
            if (element is RevitLinkInstance link && link.GetLinkDocument() is { } linked && reference.LinkedElementId != ElementId.InvalidElementId)
            {
                element = linked.GetElement(reference.LinkedElementId);
            }

            if (element is null)
            {
                continue;
            }

            accumulator.Add(element.Category?.Name ?? "Sans catégorie", element.Document.Title, distance);
        }

        return 1;
    }

    /// <summary>Hauteur renseignée en mètres (<c>ROOM_HEIGHT</c>) ; null si absente, sans valeur ou nulle (jamais 0).</summary>
    private double? ReadDeclaredHeight(Room room)
    {
        var parameter = room.get_Parameter(BuiltInParameter.ROOM_HEIGHT);
        if (parameter is not { HasValue: true, StorageType: StorageType.Double })
        {
            return null;
        }

        var raw = parameter.AsDouble();
        return double.IsFinite(raw) && raw > 0 ? units.ToMeters(raw) : null;
    }

    private ReferenceIntersector Intersector(Document document)
    {
        if (_intersector is not null)
        {
            return _intersector;
        }

        _view = FindExportView(document) ?? CreateTemporaryView(document);
        _intersector = new ReferenceIntersector(new ElementMulticategoryFilter(Obstacles.ToList()), FindReferenceTarget.Element, _view)
        {
            FindReferencesInRevitLinks = true,
        };
        return _intersector;
    }

    /// <summary>Vue 3D d'export IFC du modèle (nom contenant « EXP » et « IFC »), comme TRC.</summary>
    private static View3D? FindExportView(Document document) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .FirstOrDefault(view => !view.IsTemplate && view.Name.Contains("EXP", StringComparison.Ordinal) && view.Name.Contains("IFC", StringComparison.Ordinal));

    /// <summary>Vue 3D isométrique temporaire, tous les jeux de travail utilisateur visibles (comme <c>CreateTemporary3DView</c> de TRC).</summary>
    private static View3D CreateTemporaryView(Document document)
    {
        var viewType = new FilteredElementCollector(document)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(type => type.ViewFamily == ViewFamily.ThreeDimensional)
            ?? throw new InvalidOperationException("Aucun type de vue 3D dans le document.");

        using var transaction = new Transaction(document, "EG Room Tracker - vue 3D des hauteurs");
        transaction.Start();
        var view = View3D.CreateIsometric(document, viewType.Id);
        view.Name = "EG_RoomTracker_Hauteurs_" + Guid.NewGuid().ToString("N")[..8];
        if (document.IsWorkshared)
        {
            foreach (var workset in new FilteredWorksetCollector(document).OfKind(WorksetKind.UserWorkset))
            {
                try
                {
                    view.SetWorksetVisibility(workset.Id, WorksetVisibility.Visible);
                }
                catch (Exception)
                {
                    // Jeu de travail dont la visibilité ne peut être fixée : conservé tel quel.
                }
            }
        }

        if (transaction.Commit() != TransactionStatus.Committed)
        {
            throw new InvalidOperationException("Vue 3D temporaire non validée.");
        }

        return view;
    }
}
