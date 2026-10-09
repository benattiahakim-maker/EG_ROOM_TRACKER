using EgRoomTracker.Application;

namespace EgRoomTracker.Tests.Application;

/// <summary>Contexte du WorkItem Design Automation : DAS_WORKITEM_ID et &lt;id&gt;_job.das (APS DA Field Guide).</summary>
public sealed class WorkItemContextTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("egr-wi-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Func<string, string?> Env(string? id) => name => name == WorkItemContext.WorkItemIdVariable ? id : null;

    private static readonly Guid FixedGuid = Guid.Parse("0f3c2a9e-5b1d-4c08-a1b2-c3d4e5f60718");

    [Fact]
    public void NoVariable_FallsBackToRunGuid_AndNullEngine()
    {
        var context = WorkItemContext.Read(Env(null), _dir);

        Assert.Equal(WorkItemContext.Empty, context);
        Assert.Equal("run-0f3c2a9e5b1d4c08a1b2c3d4e5f60718", context.CorrelationId(() => FixedGuid));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void BlankOrUnsafeVariable_IsIgnored(string id) =>
        Assert.Equal(WorkItemContext.Empty, WorkItemContext.Read(Env(id), _dir));

    [Fact]
    public void Variable_GivesCorrelationId_WithoutJobFile()
    {
        var context = WorkItemContext.Read(Env(" d74527d72351488d9f6058f5ab5012bf "), _dir);

        Assert.Equal("d74527d72351488d9f6058f5ab5012bf", context.WorkItemId);
        Assert.Null(context.EngineId);
        Assert.Equal("d74527d72351488d9f6058f5ab5012bf", context.CorrelationId(() => FixedGuid));
    }

    [Theory]
    [InlineData("_job.das")]
    [InlineData("_Job.das")]
    public void JobFile_GivesEngineId(string suffix)
    {
        File.WriteAllText(Path.Combine(_dir, "wi42" + suffix),
            """{"Id":"wi42","ActivityId":"owner.Activity+prod","EngineId":"Autodesk.Revit+2026","Apps":[],"Quotas":{}}""");

        var context = WorkItemContext.Read(Env("wi42"), _dir);

        Assert.Equal(new WorkItemContext("wi42", "Autodesk.Revit+2026"), context);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"EngineId":null}""")]
    [InlineData("""{"EngineId":42}""")]
    [InlineData("""{"EngineId":"  "}""")]
    [InlineData("""{"Id":"wi42"}""")]
    public void UnreadableOrMissingEngine_KeepsWorkItemId_AndNullEngine(string content)
    {
        File.WriteAllText(Path.Combine(_dir, "wi42_job.das"), content);

        var context = WorkItemContext.Read(Env("wi42"), _dir);

        Assert.Equal(new WorkItemContext("wi42", null), context);
    }

    [Fact]
    public void FailingEnvironment_GivesEmptyContext() =>
        Assert.Equal(WorkItemContext.Empty, WorkItemContext.Read(_ => throw new System.Security.SecurityException(), _dir));
}
