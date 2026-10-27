using SharpTS.Cli;
using Xunit;

namespace SharpTS.Tests.CliTests;

public class CliSigningParserTests
{
    [Fact]
    public void SigningIsOptInAndUsesExplicitDefaults()
    {
        var parser = new CommandLineParser();
        Assert.Null(Assert.IsType<ParsedCommand.Compile>(parser.Parse(["-c", "hello.ts"])).CompileOptions.Signing);
        var command = Assert.IsType<ParsedCommand.Compile>(parser.Parse(["-c", "hello.ts", "-t", "exe", "--sign", "test.pfx"]));
        Assert.Equal("test.pfx", command.CompileOptions.Signing!.Certificate);
        Assert.Equal("sha256", command.CompileOptions.Signing.DigestAlgorithm);
        Assert.Equal(AuthenticodeSigningOptions.DefaultTimestampUrl, command.CompileOptions.Signing.TimestampUrl);
    }

    [Fact]
    public void ExplicitSigningOptionsAreForwardedWithoutExposingPassword()
    {
        var command = Assert.IsType<ParsedCommand.Compile>(new CommandLineParser().Parse([
            "-c", "hello.ts", "-t", "exe", "--sign", "test.pfx", "--sign-pass", "fixture-password",
            "--sign-digest", "SHA384", "--sign-timestamp", "none"]));
        Assert.Equal("fixture-password", command.CompileOptions.Signing!.Password);
        Assert.Equal("sha384", command.CompileOptions.Signing.DigestAlgorithm);
        Assert.Null(command.CompileOptions.Signing.TimestampUrl);
        Assert.DoesNotContain("fixture-password", command.CompileOptions.ToString());
    }

    [Theory]
    [InlineData("--sign")]
    [InlineData("--sign-pass")]
    [InlineData("--sign-timestamp")]
    [InlineData("--sign-digest")]
    public void SigningFlagsRequireValues(string flag)
    {
        var result = new CommandLineParser().Parse(["-c", "hello.ts", "-t", "exe", flag]);
        Assert.Equal(64, Assert.IsType<ParsedCommand.Error>(result).ExitCode);
    }

    [Theory]
    [InlineData("--sign-pass", "value")]
    [InlineData("--sign-timestamp", "https://example.test/tsa")]
    [InlineData("--sign-digest", "sha256")]
    public void SigningDetailsRequireAnExplicitCertificate(string flag, string value)
    {
        Assert.IsType<ParsedCommand.Error>(new CommandLineParser().Parse(["-c", "hello.ts", "-t", "exe", flag, value]));
    }

    [Fact]
    public void SigningRejectsDllAndNoOutputModesAndInvalidAlgorithms()
    {
        var parser = new CommandLineParser();
        Assert.IsType<ParsedCommand.Error>(parser.Parse(["-c", "hello.ts", "--sign", "test.pfx"]));
        foreach (var mode in new[] { "--noEmit", "--emitDeclarationOnly", "--showConfig" })
            Assert.IsType<ParsedCommand.Error>(parser.Parse(["-c", "hello.ts", "-t", "exe", "--sign", "test.pfx", mode]));
        Assert.IsType<ParsedCommand.Error>(parser.Parse(["-c", "hello.ts", "-t", "exe", "--sign", "test.pfx", "--sign-digest", "sha1"]));
        Assert.IsType<ParsedCommand.Error>(parser.Parse(["-c", "hello.ts", "-t", "exe", "--sign", "test.pfx", "--sign-timestamp", "file:///tmp/tsa"]));
    }

    [Fact]
    public void PasswordEnvironmentFallbackHasLowerPriorityThanExplicitOption()
    {
        const string key = "SHARPTS_SIGN_PASS";
        var previous = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "environment-fixture");
            var parser = new CommandLineParser();
            var fallback = Assert.IsType<ParsedCommand.Compile>(parser.Parse(["-c", "hello.ts", "-t", "exe", "--sign", "test.pfx"]));
            Assert.Equal("environment-fixture", fallback.CompileOptions.Signing!.Password);
            var explicitValue = Assert.IsType<ParsedCommand.Compile>(parser.Parse(["-c", "hello.ts", "-t", "exe", "--sign", "test.pfx", "--sign-pass", "explicit-fixture"]));
            Assert.Equal("explicit-fixture", explicitValue.CompileOptions.Signing!.Password);
        }
        finally { Environment.SetEnvironmentVariable(key, previous); }
    }

    [Theory]
    [InlineData("--sign", "--verify")]
    [InlineData("--sign-timestamp", "https://name:password@example.test/tsa")]
    [InlineData("--sign-digest", "invalid")]
    public void InvalidSigningValuesAreUsageErrors(string option, string value)
    {
        string[] args = option == "--sign"
            ? ["-c", "hello.ts", "-t", "exe", option, value]
            : ["-c", "hello.ts", "-t", "exe", "--sign", "test.pfx", option, value];
        Assert.Equal(64, Assert.IsType<ParsedCommand.Error>(new CommandLineParser().Parse(args)).ExitCode);
    }

    [Fact]
    public void NativeProjectModeCannotBeCombinedWithSigning()
    {
        foreach (var target in new[] { "dll", "exe" })
            Assert.IsType<ParsedCommand.Error>(new CommandLineParser().Parse([
                "-c", "hello.ts", "-t", target, "--emit-native-project", "native",
                "--runtime", SharpTS.Compilation.NativeAotProjectOptions.HostRuntimeIdentifier!, "--sign", "test.pfx"]));
    }

    [Fact]
    public void FilePasswordEnvironmentDoesNotApplyToCertificateStoreSelection()
    {
        const string key = "SHARPTS_SIGN_PASS";
        var previous = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "file-password-fixture");
            string certificate = "sha1:" + new string('A', 40);
            var parser = new CommandLineParser();
            var command = Assert.IsType<ParsedCommand.Compile>(parser.Parse(["-c", "hello.ts", "-t", "exe", "--sign", certificate]));
            Assert.Null(command.CompileOptions.Signing!.Password);
            Assert.IsType<ParsedCommand.Error>(parser.Parse(["-c", "hello.ts", "-t", "exe", "--sign", certificate, "--sign-pass", "explicit"]));
            var empty = Assert.IsType<ParsedCommand.Compile>(parser.Parse(["-c", "hello.ts", "-t", "exe", "--sign", "test.pfx", "--sign-pass", ""]));
            Assert.Equal("", empty.CompileOptions.Signing!.Password);
            Assert.Null(Assert.IsType<ParsedCommand.Compile>(parser.Parse(["-c", "hello.ts"])).CompileOptions.Signing);
        }
        finally { Environment.SetEnvironmentVariable(key, previous); }
    }
}
