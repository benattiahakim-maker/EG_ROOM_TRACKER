using System.Text.Json;
using EgRoomTracker.Logging;

namespace EgRoomTracker.Tests.Logging;

public class JsonConsoleLogServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 3, 250, TimeSpan.Zero);

    [Fact]
    public void Log_WritesOneJsonLinePerEventWithRequiredFields()
    {
        var (log, output) = Create();

        log.Info(LogEventCodes.ExtractionStarted, "startup", "Extraction démarrée.");
        log.Warning(LogEventCodes.AnomaliesDetected, "quality", "2 anomalies.");

        var lines = Lines(output);
        Assert.Equal(2, lines.Length);
        var first = Parse(lines[0]);
        Assert.Equal("2026-10-09T12:00:03.250Z", first.GetProperty("timestampUtc").GetString());
        Assert.Equal("Info", first.GetProperty("level").GetString());
        Assert.Equal(LogEventCodes.ExtractionStarted, first.GetProperty("eventCode").GetString());
        Assert.Equal("wi-test", first.GetProperty("correlationId").GetString());
        Assert.Equal("startup", first.GetProperty("stage").GetString());
        Assert.Equal("Extraction démarrée.", first.GetProperty("message").GetString());
        Assert.False(first.TryGetProperty("exception", out _));
        Assert.Equal("Warning", Parse(lines[1]).GetProperty("level").GetString());
    }

    [Fact]
    public void Log_WritesContextDurationDataAndException()
    {
        var (log, output) = Create();
        Exception exception;
        try
        {
            throw new InvalidOperationException("externe", new IOException("interne"));
        }
        catch (Exception thrown)
        {
            exception = thrown;
        }

        log.Log(new LogEvent(LogLevel.Error, LogEventCodes.ExtractionFailed, "collection", "Échec.")
        {
            DocumentPath = "Projet/A.rvt",
            RoomElementId = "412345",
            Duration = TimeSpan.FromMilliseconds(1234.5),
            Exception = exception,
            Data = new Dictionary<string, object?> { ["errorCode"] = "UNEXPECTED_ERROR", ["rooms"] = 3, ["ids"] = new[] { "1", "2" } },
        });

        var line = Parse(Lines(output).Single());
        Assert.Equal("Error", line.GetProperty("level").GetString());
        Assert.Equal("Projet/A.rvt", line.GetProperty("documentPath").GetString());
        Assert.Equal("412345", line.GetProperty("roomElementId").GetString());
        Assert.Equal(1234.5, line.GetProperty("durationMs").GetDouble());
        var data = line.GetProperty("data");
        Assert.Equal("UNEXPECTED_ERROR", data.GetProperty("errorCode").GetString());
        Assert.Equal(3, data.GetProperty("rooms").GetInt32());
        Assert.Equal(2, data.GetProperty("ids").GetArrayLength());
        var ex = line.GetProperty("exception");
        Assert.Equal("System.InvalidOperationException", ex.GetProperty("type").GetString());
        Assert.Equal("externe", ex.GetProperty("message").GetString());
        Assert.False(string.IsNullOrEmpty(ex.GetProperty("stackTrace").GetString()));
        Assert.Equal("System.IO.IOException", ex.GetProperty("inner").GetProperty("type").GetString());
    }

    [Fact]
    public void Log_NeverWritesSecrets()
    {
        var (log, output) = Create();
        const string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ4In0.c2lnbmF0dXJl";

        log.Log(new LogEvent(LogLevel.Error, "TEST", "export",
            $"GET https://bucket.s3.amazonaws.com/out/result.json?X-Amz-Signature=abc123&X-Amz-Credential=KEY avec Bearer {jwt} et password=hunter2")
        {
            Data = new Dictionary<string, object?>
            {
                ["accessToken"] = "tok-123",
                ["client_secret"] = "s3cr3t",
                ["Authorization"] = "Basic dXNlcjpwYXNz",
                ["nested"] = new Dictionary<string, object?> { ["apiKey"] = "k-999", ["designOption"] = "Variante A" },
                ["url"] = "https://developer.api.autodesk.com/oss/v2/signedresources/abc?region=US&token=zzz",
            },
            Exception = new InvalidOperationException("refresh_token=rt-456 refusé"),
        });

        var text = output.ToString();
        foreach (var secret in new[] { "abc123", "KEY", jwt, "hunter2", "tok-123", "s3cr3t", "dXNlcjpwYXNz", "k-999", "zzz", "rt-456" })
        {
            Assert.DoesNotContain(secret, text);
        }

        var line = Parse(Lines(output).Single());
        Assert.Contains("https://bucket.s3.amazonaws.com/out/result.json?***", line.GetProperty("message").GetString());
        Assert.Equal("Variante A", line.GetProperty("data").GetProperty("nested").GetProperty("designOption").GetString());
        Assert.Equal(SecretRedactor.Mask, line.GetProperty("data").GetProperty("accessToken").GetString());
    }

    [Fact]
    public void Log_LimitsRepeatedRoomEventsAndSummarizesOnFlush()
    {
        var (log, output) = Create(roomEventLimit: 2);

        for (var i = 0; i < 5; i++)
        {
            log.Log(new LogEvent(LogLevel.Warning, "ROOM_READ_WARNING", "collection", "Paramètre manquant.") { RoomElementId = $"{i}" });
        }

        log.Info(LogEventCodes.ExportCompleted, "export", "OK");
        log.Flush();
        log.Flush();

        var lines = Lines(output).Select(Parse).ToList();
        Assert.Equal(2, lines.Count(l => l.GetProperty("eventCode").GetString() == "ROOM_READ_WARNING"));
        var summary = Assert.Single(lines, l => l.GetProperty("eventCode").GetString() == JsonConsoleLogService.SuppressedEventCode);
        Assert.Equal(3, summary.GetProperty("data").GetProperty("ROOM_READ_WARNING").GetInt32());
        Assert.Equal("Warning", summary.GetProperty("level").GetString());
    }

    [Fact]
    public void Flush_FlushesUnderlyingWriterAndDisposeStopsLogging()
    {
        var output = new FlushCountingWriter();
        var log = new JsonConsoleLogService("wi-test", output);

        log.Info("A", "startup", "a");
        log.Flush();
        log.Dispose();
        log.Info("B", "startup", "b");

        Assert.True(output.Flushes >= 1);
        Assert.Single(Lines(output));
    }

    [Fact]
    public void Log_IsThreadSafeAndKeepsLinesWhole()
    {
        var (log, output) = Create();

        Parallel.For(0, 200, i => log.Info("PARALLEL", "test", $"message {i}"));

        var lines = Lines(output);
        Assert.Equal(200, lines.Length);
        Assert.All(lines, l => Parse(l));
    }

    private static (JsonConsoleLogService Log, StringWriter Output) Create(int roomEventLimit = JsonConsoleLogService.DefaultRoomEventLimit)
    {
        var output = new StringWriter();
        return (new JsonConsoleLogService("wi-test", output, new FixedTime(Now), roomEventLimit), output);
    }

    private static string[] Lines(StringWriter output) =>
        output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static JsonElement Parse(string line)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.Clone();
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FlushCountingWriter : StringWriter
    {
        public int Flushes { get; private set; }

        public override void Flush()
        {
            Flushes++;
            base.Flush();
        }
    }
}
