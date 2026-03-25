using AdmXmlDb.Core;
using AdmXmlDb.Core.Entities;

namespace AdmXmlDb.Core.Tests;

public class TargetPathBuilderTests
{
    private static IntegrationTask MakeTask(string outputPath = @"C:\Output") => new()
    {
        OutputPath = outputPath,
        TargetDirectoryComponents = new List<TargetDirectoryComponent>()
    };

    // ===== GetOutputFilePath =====

    [Fact]
    public void GetOutputFilePath_NoOptions_ReturnsPrefixPlusFileName()
    {
        var task = MakeTask();
        var result = TargetPathBuilder.GetOutputFilePath("<root/>", task, "data.xml");
        Assert.Equal(Path.Combine(@"C:\Output", "data.xml"), result);
    }

    [Fact]
    public void GetOutputFilePath_WithPrefix_PrefixIsApplied()
    {
        var task = MakeTask();
        task.PrependToFileName = "PROC_";
        var result = TargetPathBuilder.GetOutputFilePath("<root/>", task, "data.xml");
        Assert.Equal(Path.Combine(@"C:\Output", "PROC_data.xml"), result);
    }

    [Fact]
    public void GetOutputFilePath_WithSuffix_SuffixBeforeExtension()
    {
        var task = MakeTask();
        task.FileNameSuffix = "_done";
        var result = TargetPathBuilder.GetOutputFilePath("<root/>", task, "data.xml");
        Assert.Equal(Path.Combine(@"C:\Output", "data_done.xml"), result);
    }

    [Fact]
    public void GetOutputFilePath_WithPrefixAndSuffix_BothApplied()
    {
        var task = MakeTask();
        task.PrependToFileName = "IN_";
        task.FileNameSuffix = "_OUT";
        var result = TargetPathBuilder.GetOutputFilePath("<root/>", task, "report.xml");
        Assert.Equal(Path.Combine(@"C:\Output", "IN_report_OUT.xml"), result);
    }

    [Fact]
    public void GetOutputFilePath_AddDateTimeToFileName_TimestampAppended()
    {
        var task = MakeTask();
        task.AddDateTimeToFileName = true;
        var before = DateTime.Now;
        var result = TargetPathBuilder.GetOutputFilePath("<root/>", task, "data.xml");
        var after = DateTime.Now;

        var fileName = Path.GetFileNameWithoutExtension(result);
        // fileName should be like "data_20260325_143022"
        Assert.StartsWith("data_", fileName);
        Assert.Equal(".xml", Path.GetExtension(result));
        // Timestamp portion length: yyyyMMdd_HHmmss = 15 chars
        Assert.Equal("data_".Length + 15, fileName.Length);
    }

    [Fact]
    public void GetOutputFilePath_AddDateTimeFalse_NoTimestamp()
    {
        var task = MakeTask();
        task.AddDateTimeToFileName = false;
        var result = TargetPathBuilder.GetOutputFilePath("<root/>", task, "data.xml");
        Assert.Equal(Path.Combine(@"C:\Output", "data.xml"), result);
    }

    // ===== BuildTargetDirectory =====

    [Fact]
    public void BuildTargetDirectory_NoComponents_ReturnsNull()
    {
        var task = MakeTask();
        var result = TargetPathBuilder.BuildTargetDirectory("<root/>", task);
        Assert.Null(result);
    }

    [Fact]
    public void BuildTargetDirectory_StaticComponentOnly_ReturnsSegment()
    {
        var task = MakeTask();
        task.TargetDirectoryComponents = new List<TargetDirectoryComponent>
        {
            new() { ComponentType = TargetDirectoryComponentType.Static, Value = "processed", SortOrder = 0 }
        };
        var result = TargetPathBuilder.BuildTargetDirectory("<root/>", task);
        Assert.Equal("processed", result);
    }

    [Fact]
    public void BuildTargetDirectory_WithStaticTargetDir_UsedAsBase()
    {
        var task = MakeTask();
        task.StaticTargetDirectory = @"C:\Archive";
        task.TargetDirectoryComponents = new List<TargetDirectoryComponent>
        {
            new() { ComponentType = TargetDirectoryComponentType.Static, Value = "2026", SortOrder = 0 }
        };
        var result = TargetPathBuilder.BuildTargetDirectory("<root/>", task);
        Assert.Equal(Path.Combine(@"C:\Archive", "2026"), result);
    }

    [Fact]
    public void BuildTargetDirectory_XPathComponent_ExtractsValue()
    {
        var task = MakeTask();
        task.TargetDirectoryComponents = new List<TargetDirectoryComponent>
        {
            new() { ComponentType = TargetDirectoryComponentType.XPath, Value = "//department", SortOrder = 0 }
        };
        var xml = "<root><department>HR</department></root>";
        var result = TargetPathBuilder.BuildTargetDirectory(xml, task);
        Assert.Equal("HR", result);
    }

    [Fact]
    public void BuildTargetDirectory_XPathNotFound_SegmentSkipped()
    {
        var task = MakeTask();
        task.TargetDirectoryComponents = new List<TargetDirectoryComponent>
        {
            new() { ComponentType = TargetDirectoryComponentType.XPath, Value = "//missing", SortOrder = 0 }
        };
        var result = TargetPathBuilder.BuildTargetDirectory("<root/>", task);
        Assert.Null(result);
    }
}
