namespace SharpTS.Cli;

/// <summary>Publishes the generated guest through the supported .NET NativeAOT pipeline.</summary>
public sealed record NativeAotPublishOptions(string OutputPath, string RuntimeIdentifier);
