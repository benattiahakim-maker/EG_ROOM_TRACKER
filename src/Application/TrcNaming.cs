namespace EgRoomTracker.Application;

/// <summary>
/// Codification des noms de TRC : <c>code_file</c> d'un modèle et nom des vues 3D de pièces. Reprise de l'application
/// de validation (EG_Automation_TRC) : la plateforme retrouve l'image d'une pièce par son <c>bim_id</c> (UniqueId),
/// qui préfixe le nom du fichier.
/// </summary>
public static class TrcNaming
{
    /// <summary>Suffixe de la vue 3D d'une pièce (première position de caméra).</summary>
    public const string RoomImageSuffix = "AdjustedCornerView_1";

    /// <summary>
    /// <c>code_file</c> : nom du RVT sans extension, <c>_</c> remplacés par <c>-</c>, suffixe <c>-détaché</c> retiré
    /// (ex. <c>GUID£64SDF_SOE_V5.rvt</c> donne <c>GUID£64SDF-SOE-V5</c>).
    /// </summary>
    public static string CodeFile(string documentFileName) =>
        Path.GetFileNameWithoutExtension(documentFileName).Replace("_", "-").Replace("-détaché", string.Empty);

    /// <summary>Nom du fichier image d'une pièce : <c>{UniqueId}_{code_file}_{ElementId}_AdjustedCornerView_1.jpg</c>.</summary>
    public static string RoomImageFileName(string uniqueId, string codeFile, long roomElementId) =>
        FormattableString.Invariant($"{uniqueId}_{codeFile}_{roomElementId}_{RoomImageSuffix}.jpg");
}
