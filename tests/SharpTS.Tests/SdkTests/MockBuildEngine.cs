using Microsoft.Build.Framework;

namespace SharpTS.Tests.SdkTests;

/// <summary>
/// Mock implementation of IBuildEngine for testing MSBuild tasks.
/// </summary>
public class MockBuildEngine : IBuildEngine
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Messages { get; } = new();

    public bool ContinueOnError => false;
    public int LineNumberOfTaskNode => 0;
    public int ColumnNumberOfTaskNode => 0;
    public string ProjectFileOfTaskNode => "test.csproj";

    public bool BuildProjectFile(string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs)
    {
        return true;
    }

    public void LogCustomEvent(CustomBuildEventArgs e)
    {
        Messages.Add(e.Message ?? string.Empty);
    }

    public void LogErrorEvent(BuildErrorEventArgs e)
    {
        Errors.Add(e.Message ?? string.Empty);
    }

    public void LogMessageEvent(BuildMessageEventArgs e)
    {
        Messages.Add(e.Message ?? string.Empty);
    }

    public void LogWarningEvent(BuildWarningEventArgs e)
    {
        Warnings.Add(e.Message ?? string.Empty);
    }
}
