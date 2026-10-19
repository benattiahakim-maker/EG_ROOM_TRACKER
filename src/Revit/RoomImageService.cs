using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using EgRoomTracker.Application;
using EgRoomTracker.Application.Revit;

namespace EgRoomTracker.Revit;

/// <summary>
/// Vue 3D de chaque pièce (<c>roomImages</c>), reprise de l'application de validation TRC (<c>ImageExporter</c>) : vue
/// isométrique dont la caméra surplombe un coin de la pièce, section box autour de la pièce, murs, sols, toits, plafonds et
/// modèles génériques rendus transparents et en grisé pour montrer l'intérieur, annotations, niveaux et grilles masqués.
/// Les vues sont créées dans une transaction puis exportées avec les réglages de TRC (JPEG sans perte, 600 dpi, ajustées à
/// la page en largeur, <c>imageWidthPx</c> pixels, 8 000 par défaut), recadrées sur leur contenu (<see cref="ImageCropper"/>)
/// et nommées par <see cref="TrcNaming.RoomImageFileName"/>. Les vues restent temporaires : le RVT n'est jamais sauvegardé.
/// <para>
/// L'export d'image de Revit (<c>Document.ExportImage</c>) n'est pas garanti dans le moteur Design Automation : un échec
/// d'une pièce est compté et signalé, il n'interrompt ni les autres pièces ni l'extraction.
/// </para>
/// </summary>
public sealed class RoomImageService(RevitExecutionContext context) : IRoomImageService
{
    // Décalages de TRC, valeurs reproduites telles quelles (ConvertFromInternalUnits appliqué à 3 et 2, d'où des pieds).
    private static readonly double OffsetXY = UnitUtils.ConvertFromInternalUnits(3.0, UnitTypeId.Meters);
    private static readonly double OffsetZ = UnitUtils.ConvertFromInternalUnits(2.0, UnitTypeId.Meters);

    private static readonly BuiltInCategory[] TransparentCategories =
    [
        BuiltInCategory.OST_Walls,
        BuiltInCategory.OST_Floors,
        BuiltInCategory.OST_GenericModel,
        BuiltInCategory.OST_Roofs,
        BuiltInCategory.OST_Ceilings,
    ];

    public ImageExportResult Export(IReadOnlyList<RoomImageRequest> rooms, string imagesDirectory, string codeFile, int widthPx)
    {
        var document = context.RequireDocument();
        Directory.CreateDirectory(imagesDirectory);
        var viewType = new FilteredElementCollector(document)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(type => type.ViewFamily == ViewFamily.ThreeDimensional);
        if (viewType is null)
        {
            return new ImageExportResult(0, rooms.Select(r => r.ElementId).ToList(), "aucun type de vue 3D dans le document");
        }

        var failed = new List<long>();
        var views = CreateViews(document, viewType, rooms, failed);
        var temporary = Directory.CreateTempSubdirectory("egr-img-").FullName;
        var exported = 0;
        try
        {
            foreach (var (room, view) in views)
            {
                try
                {
                    if (ExportView(document, view, temporary, widthPx) is { } produced)
                    {
                        var target = Path.Combine(imagesDirectory, TrcNaming.RoomImageFileName(room.UniqueId, codeFile, room.ElementId));
                        File.Move(produced, target, overwrite: true);
                        Crop(target);
                        exported++;
                    }
                    else
                    {
                        failed.Add(room.ElementId);
                    }
                }
                catch (Exception)
                {
                    failed.Add(room.ElementId);
                }
            }
        }
        finally
        {
            TryDelete(temporary);
        }

        return new ImageExportResult(exported, failed, failed.Count > 0 ? "vue ou export d'image impossible pour certaines pièces" : null);
    }

    /// <summary>Crée les vues 3D en une transaction (validée avant l'export, qui l'exige) ; les pièces sans vue sont ajoutées à <paramref name="failed"/>.</summary>
    private static List<(RoomImageRequest Room, View3D View)> CreateViews(Document document, ViewFamilyType viewType, IReadOnlyList<RoomImageRequest> rooms, List<long> failed)
    {
        var created = new List<(RoomImageRequest, View3D)>();
        var categories = TransparentCategories
            .Select(category => Category.GetCategory(document, category))
            .OfType<Category>()
            .ToList();
        using var transaction = new Transaction(document, "EG Room Tracker - vues 3D des pièces");
        transaction.Start();
        foreach (var request in rooms)
        {
            try
            {
                if (document.GetElement(new ElementId(request.ElementId)) is Room room && CreateView(document, viewType, room, categories) is { } view)
                {
                    created.Add((request, view));
                }
                else
                {
                    failed.Add(request.ElementId);
                }
            }
            catch (Exception)
            {
                failed.Add(request.ElementId);
            }
        }

        if (transaction.Commit() != TransactionStatus.Committed)
        {
            failed.AddRange(created.Select(c => c.Item1.ElementId));
            created.Clear();
        }

        return created;
    }

    private static View3D? CreateView(Document document, ViewFamilyType viewType, Room room, IReadOnlyList<Category> categories)
    {
        var box = room.get_BoundingBox(null);
        if (box is null)
        {
            return null;
        }

        var center = (box.Min + box.Max) / 2;
        var enlarged = new BoundingBoxXYZ
        {
            Min = new XYZ(box.Min.X - OffsetXY, box.Min.Y - OffsetXY, box.Min.Z - OffsetZ),
            Max = new XYZ(box.Max.X + OffsetXY, box.Max.Y + OffsetXY, box.Max.Z - OffsetZ),
        };
        var corner = new XYZ(enlarged.Min.X, enlarged.Min.Y, enlarged.Max.Z);
        var camera = corner + ((corner - center).Normalize() * OffsetXY) + new XYZ(0, 0, OffsetZ);
        var direction = (center - camera).Normalize();

        var view = View3D.CreateIsometric(document, viewType.Id);
        view.Name = $"Room_{room.Id.Value}_{TrcNaming.RoomImageSuffix}";
        view.AreAnnotationCategoriesHidden = true;
        Hide(document, view, BuiltInCategory.OST_Levels);
        Hide(document, view, BuiltInCategory.OST_Grids);
        view.SetOrientation(new ViewOrientation3D(camera, UpDirection(direction), direction));
        view.SetSectionBox(enlarged);
        view.CropBoxActive = false;
        view.CropBoxVisible = false;
        view.CropBox = enlarged;
        view.DisplayStyle = DisplayStyle.Textures;

        foreach (var category in categories.Where(c => view.CanCategoryBeHidden(c.Id)))
        {
            ApplyOverrides(view, category, 100);
            foreach (Category subCategory in category.SubCategories)
            {
                if (subCategory is not null && view.CanCategoryBeHidden(subCategory.Id))
                {
                    ApplyOverrides(view, subCategory, 60);
                }
            }
        }

        return view;
    }

    private static void Hide(Document document, View3D view, BuiltInCategory category)
    {
        if (Category.GetCategory(document, category) is { } found && view.CanCategoryBeHidden(found.Id))
        {
            view.SetCategoryHidden(found.Id, true);
        }
    }

    /// <summary>Haut de la vue, orthogonal à la direction (l'axe Y si la vue est presque verticale).</summary>
    private static XYZ UpDirection(XYZ direction)
    {
        var up = Math.Abs(direction.DotProduct(XYZ.BasisZ)) > 0.9 ? XYZ.BasisY : XYZ.BasisZ;
        var right = up.CrossProduct(direction).Normalize();
        return direction.CrossProduct(right).Normalize();
    }

    /// <summary>Surcharge de la catégorie : grisé, demi-teinte, lignes fines, transparence (en pourcentage).</summary>
    private static void ApplyOverrides(View3D view, Category category, int transparency)
    {
        var grey = new Color(128, 128, 128);
        var settings = new OverrideGraphicSettings();
        Try(() => settings.SetProjectionLineWeight(1));
        Try(() => settings.SetCutLineWeight(1));
        Try(() => settings.SetSurfaceTransparency(transparency));
        Try(() => settings.SetSurfaceBackgroundPatternColor(grey));
        Try(() => settings.SetSurfaceForegroundPatternColor(grey));
        Try(() => settings.SetCutForegroundPatternColor(grey));
        Try(() => settings.SetCutBackgroundPatternColor(grey));
        Try(() => settings.SetHalftone(true));
        Try(() => view.SetCategoryOverrides(category.Id, settings));
    }

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            // Réglage non applicable à cette catégorie : ignoré, comme dans TRC.
        }
    }

    /// <summary>Exporte la vue en JPEG dans le dossier temporaire ; renvoie le fichier produit (Revit préfixe le nom) ou null.</summary>
    private static string? ExportView(Document document, View3D view, string temporaryDirectory, int widthPx)
    {
        var options = new ImageExportOptions
        {
            ExportRange = ExportRange.SetOfViews,
            FilePath = Path.Combine(temporaryDirectory, "img"),
            HLRandWFViewsFileType = ImageFileType.JPEGLossless,
            ShadowViewsFileType = ImageFileType.JPEGLossless,
            ImageResolution = ImageResolution.DPI_600,
            ZoomType = ZoomFitType.FitToPage,
            FitDirection = FitDirectionType.Horizontal,
            PixelSize = widthPx,
        };
        options.SetViewsAndSheets(new List<ElementId> { view.Id });
        document.ExportImage(options);

        return Directory.EnumerateFiles(temporaryDirectory)
            .Where(file => Path.GetExtension(file).StartsWith(".jp", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(file).Contains(view.Name, StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>Recadre l'image ; en cas d'échec (ou si System.Drawing est indisponible), elle est conservée telle quelle, comme dans TRC.</summary>
    private static void Crop(string path)
    {
        try
        {
            ImageCropper.CropWhitespace(path);
        }
        catch (Exception)
        {
            // Image non recadrée.
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception)
        {
            // Dossier temporaire : nettoyé avec le WorkItem.
        }
    }
}
