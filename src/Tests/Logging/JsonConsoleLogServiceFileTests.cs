using System.Text.Json;
using EgRoomTracker.Logging;

namespace EgRoomTracker.Tests.Logging;

/// <summary>Copie du journal dans <c>log.jsonl</c> (fichier annexe du ZIP de sortie).</summary>
public sealed class JsonConsoleLogServiceFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "egrt-log-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void File_ContainsSameLinesAsConsole_AsAscii_WithoutBom()
    {
        var path = Path.Combine(_root, "result", "log.jsonl");
        var console = new StringWriter();
        using (var log = new JsonConsoleLogService("wi-test", console, filePath: path))
        {
            log.Info(LogEventCodes.ExtractionStarted, "startup", "Pièce « Réserve » démarrée.");
            log.Warning(LogEventCodes.AnomaliesDetected, "quality", "2 anomalies.");
        }

        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.All(bytes, b => Assert.True(b < 128));
        var fileLines = File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var consoleLines = console.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(consoleLines, fileLines);
        Assert.Equal(2, fileLines.Length);
        foreach (var line in fileLines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.Equal("wi-test", doc.RootElement.GetProperty("correlationId").GetString());
        }
    }

    [Fact]
    public void File_IsWrittenBeforeDispose_AndKeepsSuppressionSummaryAfterFlush()
    {
        var path = Path.Combine(_root, "log.jsonl");
        using var log = new JsonConsoleLogService("wi-test", new StringWriter(), roomEventLimit: 1, filePath: path);
        for (var i = 0; i < 3; i++)
        {
            log.Log(new LogEvent(LogLevel.Warning, "ROOM_READ_WARNING", "collection", "x") { RoomElementId = $"{i}" });
        }

        log.Flush();

        // Lisible sans fermer le service : le flush final précède le zip.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var lines = new StreamReader(stream).ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1, lines.Count(l => l.Contains("\"ROOM_READ_WARNING\"") && !l.Contains(JsonConsoleLogService.SuppressedEventCode)));
        var summary = Assert.Single(lines, l => l.Contains(JsonConsoleLogService.SuppressedEventCode));
        using var doc = JsonDocument.Parse(summary);
        Assert.Equal(2, doc.RootElement.GetProperty("data").GetProperty("ROOM_READ_WARNING").GetInt32());
    }

    [Fact]
    public void File_NeverContainsSecrets()
    {
        var path = Path.Combine(_root, "log.jsonl");
        using (var log = new JsonConsoleLogService("wi-test", new StringWriter(), filePath: path))
        {
            log.Log(new LogEvent(LogLevel.Error, "TEST", "export", "GET https://bucket.s3.amazonaws.com/out/result.json?X-Amz-Signature=abc123 Bearer tok-777")
            {
                Data = new Dictionary<string, object?> { ["accessToken"] = "tok-123", ["client_secret"] = "s3cr3t" },
                Exception = new InvalidOperationException("refresh_token=rt-456 refusé"),
            });
        }

        var text = File.ReadAllText(path);
        foreach (var secret in new[] { "abc123", "tok-777", "tok-123", "s3cr3t", "rt-456" })
        {
            Assert.DoesNotContain(secret, text);
        }
    }

    [Fact]
    public void File_Failure_NeverThrows_WarnsOnConsoleOnce_AndConsoleKeepsWorking()
    {
        // Le chemin du fichier est un dossier existant : l'ouverture échoue.
        Directory.CreateDirectory(_root);
        var console = new StringWriter();
        var log = new JsonConsoleLogService("wi-test", console, filePath: _root);

        log.Info(LogEventCodes.ExtractionStarted, "startup", "un");
        log.Info(LogEventCodes.ExportCompleted, "export", "deux");
        log.Flush();
        log.Dispose();

        var lines = console.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.Equal(1, lines.Count(l => l.GetProperty("eventCode").GetString() == JsonConsoleLogService.FileUnavailableEventCode));
        Assert.Contains(lines, l => l.GetProperty("message").GetString() == "un");
        Assert.Contains(lines, l => l.GetProperty("message").GetString() == "deux");
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public void WithoutFilePath_NoFileIsCreated()
    {
        using (var log = new JsonConsoleLogService("wi-test", new StringWriter()))
        {
            log.Info(LogEventCodes.ExtractionStarted, "startup", "un");
        }

        Assert.False(Directory.Exists(_root));
    }
}
