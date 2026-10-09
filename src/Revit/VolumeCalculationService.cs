using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.Exceptions;
using EgRoomTracker.Application;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;

namespace EgRoomTracker.Revit;

/// <summary>
/// État du calcul des volumes.
/// <list type="bullet">
/// <item><c>preserve</c> (EGR-09) : lecture du réglage <c>AreaVolumeSettings.GetAreaVolumeSettings(document).ComputeVolumes</c>,
/// sans transaction ni modification.</item>
/// <item><c>compute</c> (EGR-16) : si le réglage est déjà actif, lecture directe sans transaction. Sinon, transaction
/// temporaire sur la copie de travail : <c>ComputeVolumes = true</c>, <c>Document.Regenerate()</c>, lecture de
/// <c>Room.Volume</c> de toutes les pièces AVANT l'annulation (<c>Transaction.RollBack()</c> rejette toutes les
/// modifications), puis annulation. Le RVT n'est jamais sauvegardé (fermeture sans sauvegarde).</item>
/// </list>
/// Échec d'activation (transaction refusée, réglage non modifiable) : état <see cref="VolumeComputationState.ComputeFailure"/>,
/// volumes non disponibles et anomalie document VOLUME_COMPUTE_FAILED. Échec de régénération
/// (<c>RegenerationFailedException</c>, document corrompu dont la lecture est illégale) : erreur globale DOCUMENT_CORRUPTED.
/// </summary>
public sealed class VolumeCalculationService(RevitExecutionContext context) : IVolumeCalculationService
{
    internal const string TransactionName = "EG Room Tracker - calcul temporaire des volumes";

    public VolumeComputationState Prepare(VolumeMode mode)
    {
        var document = context.RequireDocument();
        var documentSetting = AreaVolumeSettings.GetAreaVolumeSettings(document).ComputeVolumes;
        if (mode == VolumeMode.Preserve || documentSetting)
        {
            return new VolumeComputationState(mode, documentSetting) { DocumentComputeVolumes = documentSetting };
        }

        return ComputeTemporarily(document, mode);
    }

    private static VolumeComputationState ComputeTemporarily(Document document, VolumeMode mode)
    {
        var failed = new VolumeComputationState(mode, false) { DocumentComputeVolumes = false };
        using var transaction = new Transaction(document, TransactionName);
        Dictionary<long, double> volumes;
        try
        {
            if (transaction.Start() != TransactionStatus.Started)
            {
                return failed with { ComputeFailure = "Transaction temporaire non démarrée." };
            }

            // Annulation volontaire : effacer silencieusement les échecs postés (options réinitialisées à chaque Start).
            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions().SetClearAfterRollback(true));
            AreaVolumeSettings.GetAreaVolumeSettings(document).ComputeVolumes = true;
            document.Regenerate();
            volumes = ReadVolumes(document);
        }
        catch (RegenerationFailedException ex)
        {
            RollBack(transaction);
            throw new ExtractionFailedException(ErrorCodes.DocumentCorrupted, Stages.Document,
                "Échec de régénération pendant le calcul temporaire des volumes (volumeMode compute).", ex);
        }
        catch (Exception ex) when (ex is not ExtractionFailedException)
        {
            var rolledBack = RollBack(transaction);
            return failed with
            {
                ComputeFailure = $"Activation du calcul des volumes impossible ({ex.GetType().Name}).",
                TransactionRolledBack = rolledBack,
            };
        }

        return new VolumeComputationState(mode, true)
        {
            DocumentComputeVolumes = false,
            ComputeApplied = true,
            TransactionRolledBack = RollBack(transaction),
            ComputedVolumes = volumes,
        };
    }

    /// <summary>Volumes bruts de toutes les pièces ; une pièce illisible est omise (volume non disponible).</summary>
    private static Dictionary<long, double> ReadVolumes(Document document)
    {
        var volumes = new Dictionary<long, double>();
        using var collector = new FilteredElementCollector(document).OfCategory(RoomCollectionService.RoomCategory).WhereElementIsNotElementType();
        foreach (var element in collector)
        {
            if (element is not Room room)
            {
                continue;
            }

            try
            {
                volumes[room.Id.Value] = room.Volume;
            }
            catch (Exception)
            {
                // Lecture isolée par pièce : la pièce sera exportée en notAvailable.
            }
        }

        return volumes;
    }

    /// <summary>Annule la transaction si elle est démarrée ; null si aucune transaction n'était démarrée.</summary>
    private static bool? RollBack(Transaction transaction)
    {
        if (transaction.GetStatus() != TransactionStatus.Started)
        {
            return null;
        }

        try
        {
            return transaction.RollBack() == TransactionStatus.RolledBack;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
