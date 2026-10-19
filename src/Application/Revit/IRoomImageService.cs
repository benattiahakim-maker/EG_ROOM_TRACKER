namespace EgRoomTracker.Application.Revit;

/// <summary>
/// Export d'une vue 3D par pièce (images JPEG de <c>Images/</c> dans la sortie ZIP), nommées selon <see cref="TrcNaming"/>.
/// Appelé par <see cref="RoomExtractionService"/> quand <c>roomImages</c> est actif, document ouvert. Ne modifie jamais
/// le RVT source (les vues sont temporaires, rien n'est sauvegardé).
/// </summary>
public interface IRoomImageService
{
    /// <param name="rooms">Pièces à photographier (placées uniquement).</param>
    /// <param name="imagesDirectory">Dossier de sortie, créé au besoin.</param>
    /// <param name="codeFile">Code du fichier, cf. <see cref="TrcNaming.CodeFile"/>.</param>
    /// <param name="widthPx">Largeur des images en pixels.</param>
    ImageExportResult Export(IReadOnlyList<RoomImageRequest> rooms, string imagesDirectory, string codeFile, int widthPx);
}

/// <summary>Pièce à photographier : ElementId et UniqueId (préfixe du nom de fichier).</summary>
public sealed record RoomImageRequest(long ElementId, string UniqueId);

/// <summary>Images écrites et pièces sans image (vue ou export impossible).</summary>
public sealed record ImageExportResult(int Exported, IReadOnlyList<long> FailedRoomIds, string? Error = null);
