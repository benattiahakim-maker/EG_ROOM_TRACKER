using Autodesk.Revit.DB;
using EgRoomTracker.Application;
using EgRoomTracker.Application.Revit;
using EgRoomTracker.Contracts.Codes;
using EgRoomTracker.Contracts.Configuration;
using EgRoomTracker.Contracts.Results;
using EgRoomTracker.Logging;
using RevitExceptions = Autodesk.Revit.Exceptions;

namespace EgRoomTracker.Revit;

/// <summary>
/// Accès au document en environnement Automation (EGR-06).
/// D1 : ouverture explicite du RVT principal (chemin résolu par la configuration) ; maquette collaborative détachée
/// du central en conservant les worksets, tous les worksets ouverts ; propriétaire : ce service, qui ferme sans
/// sauvegarde. D2 : voir <see cref="OpenFailurePolicy"/>. Liens : LINK_MISSING pour chaque lien Revit introuvable ;
/// les pièces des liens ne sont pas parcourues.
/// </summary>
public sealed class RevitDocumentService(RevitExecutionContext context, ILogService log) : IRevitDocumentService
{
    public DocumentDescriptor Open(ExtractionSettings settings, string inputDirectory)
    {
        var (fullPath, relativePath) = MainModelLocation.Resolve(inputDirectory, settings.MainModelPath);
        var fileInfo = ReadFileInfo(fullPath);
        var versionCheck = RevitVersionCompatibility.Check(fileInfo.Format, fileInfo.IsSavedInLaterVersion, context.Application.VersionNumber);
        if (versionCheck.UpgradedInMemory)
        {
            log.Log(new LogEvent(LogLevel.Info, "DOCUMENT_UPGRADE_IN_MEMORY", Stages.Document,
                $"RVT {versionCheck.FileYear} mis à niveau en mémoire vers Revit {context.Application.VersionNumber} ; aucune sauvegarde.")
            {
                DocumentPath = relativePath,
            });
        }

        var policy = new OpenFailurePolicy();
        var document = ReuseProvidedDocument(fullPath) ?? OpenExplicitly(fullPath, fileInfo.IsWorkshared, policy);

        var anomalies = new List<AnomalyDto>();
        if (DocumentAnomalies.RevitWarningsOnOpen(policy.Warnings) is { } warnings)
        {
            anomalies.Add(warnings);
        }

        anomalies.AddRange(FindMissingLinks(document));
        return new DocumentDescriptor(
            new SourceInfo(Path.GetFileName(fullPath), relativePath, document.IsWorkshared || fileInfo.IsWorkshared),
            anomalies);
    }

    public void Close()
    {
        var document = context.Document;
        var owned = context.OwnsDocument;
        context.Detach();
        if (document is not null && owned && document.IsValidObject)
        {
            // Jamais de sauvegarde du RVT source.
            document.Close(false);
        }
    }

    private static BasicFileInfo ReadFileInfo(string fullPath)
    {
        try
        {
            return BasicFileInfo.Extract(fullPath);
        }
        catch (RevitExceptions.CorruptModelException ex)
        {
            throw new ExtractionFailedException(ErrorCodes.DocumentCorrupted, Stages.Document, "Informations du fichier RVT illisibles (corruption).", ex);
        }
        catch (RevitExceptions.ApplicationException ex)
        {
            throw new ExtractionFailedException(ErrorCodes.DocumentOpenFailed, Stages.Document, "Informations du fichier RVT illisibles.", ex);
        }
    }

    /// <summary>Réutilise le document fourni par RevitCoreConsole s'il correspond au RVT principal (non possédé).</summary>
    private Document? ReuseProvidedDocument(string fullPath)
    {
        var provided = context.ProvidedDocument;
        if (provided is null || !provided.IsValidObject)
        {
            return null;
        }

        if (!string.Equals(Path.GetFullPath(provided.PathName), fullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ExtractionFailedException(ErrorCodes.DocumentOpenFailed, Stages.Document,
                "Le document ouvert par l'Activity ne correspond pas au RVT principal (stratégie D1 : Activity sans /i).");
        }

        context.Attach(provided, owned: false);
        return provided;
    }

    private Document OpenExplicitly(string fullPath, bool isWorkshared, OpenFailurePolicy policy)
    {
        var options = new OpenOptions { Audit = false };
        if (isWorkshared)
        {
            options.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
            options.SetOpenWorksetsConfiguration(new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));
        }

        var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(fullPath);
        Document? document = null;
        context.Application.FailuresProcessing += policy.OnFailuresProcessing;
        try
        {
            document = context.Application.OpenDocumentFile(modelPath, options);
        }
        catch (RevitExceptions.CorruptModelException ex)
        {
            throw new ExtractionFailedException(ErrorCodes.DocumentCorrupted, Stages.Document, "Document corrompu.", ex);
        }
        catch (RevitExceptions.ApplicationException ex) when (policy.BlockingFailures.Count > 0)
        {
            throw new ExtractionFailedException(ErrorCodes.DocumentCorrupted, Stages.Document, BlockingMessage(policy), ex);
        }
        catch (RevitExceptions.ApplicationException ex)
        {
            throw new ExtractionFailedException(ErrorCodes.DocumentOpenFailed, Stages.Document, "Échec d'ouverture du document.", ex);
        }
        finally
        {
            context.Application.FailuresProcessing -= policy.OnFailuresProcessing;
        }

        if (document is null)
        {
            throw new ExtractionFailedException(ErrorCodes.DocumentOpenFailed, Stages.Document, "Échec d'ouverture du document.");
        }

        if (policy.BlockingFailures.Count > 0)
        {
            document.Close(false);
            throw new ExtractionFailedException(ErrorCodes.DocumentCorrupted, Stages.Document, BlockingMessage(policy));
        }

        context.Attach(document, owned: true);
        return document;
    }

    private static string BlockingMessage(OpenFailurePolicy policy) =>
        $"Erreur bloquante à l'ouverture ({policy.BlockingFailures.Count}) : {policy.BlockingFailures[0]}";

    /// <summary>Liens Revit introuvables (absents du ZIP ou chemin non résolu).</summary>
    private static IEnumerable<AnomalyDto> FindMissingLinks(Document document)
    {
        using var collector = new FilteredElementCollector(document).OfClass(typeof(RevitLinkType));
        foreach (var linkType in collector.Cast<RevitLinkType>().OrderBy(t => t.Id.Value))
        {
            if (linkType.IsNestedLink || linkType.GetLinkedFileStatus() != LinkedFileStatus.NotFound)
            {
                continue;
            }

            string? path = null;
            var reference = linkType.GetExternalFileReference();
            if (reference is not null)
            {
                path = ModelPathUtils.ConvertModelPathToUserVisiblePath(reference.GetPath());
            }

            yield return DocumentAnomalies.LinkMissing(linkType.Name, path);
        }
    }
}
