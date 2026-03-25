using AdmXmlDb.Core;

namespace AdmXmlDb.Core.Tests;

public class InputValidatorTests
{
    // ===== IsValidXPath =====

    [Theory]
    [InlineData("//item")]
    [InlineData("/root/child")]
    [InlineData("//element[@attr='val']")]
    [InlineData(".")]
    [InlineData("//department/name")]
    public void IsValidXPath_ValidExpressions_ReturnsTrue(string xpath)
    {
        Assert.True(InputValidator.IsValidXPath(xpath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsValidXPath_NullOrWhitespace_ReturnsFalse(string? xpath)
    {
        Assert.False(InputValidator.IsValidXPath(xpath!));
    }

    [Theory]
    [InlineData("///invalid")]
    [InlineData("[@unclosed")]
    [InlineData("//item[")]
    public void IsValidXPath_InvalidExpressions_ReturnsFalse(string xpath)
    {
        Assert.False(InputValidator.IsValidXPath(xpath));
    }

    // ===== ContainsPathTraversal =====

    [Theory]
    [InlineData("../secret")]
    [InlineData("C:/output/../secret")]
    [InlineData("..")]
    [InlineData("folder/..")]
    [InlineData("C:\\output\\..\\secret")]
    public void ContainsPathTraversal_TraversalPatterns_ReturnsTrue(string path)
    {
        Assert.True(InputValidator.ContainsPathTraversal(path));
    }

    [Theory]
    [InlineData(@"C:\Output\data")]
    [InlineData(@"\\server\share\folder")]
    [InlineData("/var/data/output")]
    [InlineData("relative/path/folder")]
    public void ContainsPathTraversal_SafePaths_ReturnsFalse(string path)
    {
        Assert.False(InputValidator.ContainsPathTraversal(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ContainsPathTraversal_NullOrWhitespace_ReturnsFalse(string? path)
    {
        Assert.False(InputValidator.ContainsPathTraversal(path!));
    }
}
