using SharpTS.Sdk.Tasks;
using Xunit;

namespace SharpTS.Tests.SdkTests;

/// <summary>
/// Tests for ReadTsConfigTask MSBuild task.
/// </summary>
public class ReadTsConfigTaskTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MockBuildEngine _buildEngine;

    public ReadTsConfigTaskTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SharpTS_Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _buildEngine = new MockBuildEngine();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string CreateTsConfig(string content)
    {
        var path = Path.Combine(_tempDir, "tsconfig.json");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Execute_MissingFile_ReturnsTrue_WithDefaults()
    {
        var task = new ReadTsConfigTask
        {
            BuildEngine = _buildEngine,
            TsConfigPath = Path.Combine(_tempDir, "nonexistent.json")
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.False(task.PreserveConstEnums);
        Assert.False(task.ExperimentalDecorators);
        Assert.False(task.Decorators);
        Assert.False(task.EmitDecoratorMetadata);
        Assert.Equal(string.Empty, task.EntryFile);
    }

    [Fact]
    public void Execute_EmptyConfig_ReturnsTrue_WithDefaults()
    {
        var path = CreateTsConfig("{}");

        var task = new ReadTsConfigTask
        {
            BuildEngine = _buildEngine,
            TsConfigPath = path
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.False(task.PreserveConstEnums);
        Assert.False(task.ExperimentalDecorators);
        Assert.False(task.Decorators);
        Assert.False(task.EmitDecoratorMetadata);
        Assert.Equal(string.Empty, task.EntryFile);
    }

    [Fact]
    public void Execute_PreserveConstEnums_True_SetsProperty()
    {
        var path = CreateTsConfig("""
            {
                "compilerOptions": {
                    "preserveConstEnums": true
                }
            }
            """);

        var task = new ReadTsConfigTask
        {
            BuildEngine = _buildEngine,
            TsConfigPath = path
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.True(task.PreserveConstEnums);
    }

    [Fact]
    public void Execute_PreserveConstEnums_False_SetsPropertyFalse()
    {
        var path = CreateTsConfig("""
            {
                "compilerOptions": {
                    "preserveConstEnums": false
                }
            }
            """);

        var task = new ReadTsConfigTask
        {
            BuildEngine = _buildEngine,
            TsConfigPath = path
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.False(task.PreserveConstEnums);
    }

    [Fact]
    public void Execute_ExperimentalDecorators_True_SetsProperty()
    {
        var path = CreateTsConfig("""
            {
                "compilerOptions": {
                    "experimentalDecorators": true
                }
            }
            """);

        var task = new ReadTsConfigTask
        {
            BuildEngine = _buildEngine,
            TsConfigPath = path
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.True(task.ExperimentalDecorators);
    }

    [Fact]
    public void Execute_Decorators_True_SetsProperty()
    {
        var path = CreateTsConfig("""
            {
                "compilerOptions": {
                    "decorators": true
                }
            }
            """);

        var task = new ReadTsConfigTask
        {
            BuildEngine = _buildEngine,
            TsConfigPath = path
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.True(task.Decorators);
    }

    [Fact]
    public void Execute_EmitDecoratorMetadata_True_SetsProperty()
    {
        var path = CreateTsConfig("""
            {
                "compilerOptions": {
                    "emitDecoratorMetadata": true
                }
            }
            """);

        var task = new ReadTsConfigTask
        {
            BuildEngine = _buildEngine,
            TsConfigPath = path
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.True(task.EmitDecoratorMetadata);
    }

    [Fact]
    public void Execute_AllCompilerOptions_SetsAllProperties()
    {
        var path = CreateTsConfig("""
            {
                "compilerOptions": {
                    "preserveConstEnums": true,
                    "experimentalDecorators": true,
                    "decorators": true,
                    "emitDecoratorMetadata": true,
                    "rootDir": "./src",
                    "outDir": "./dist"
                }
            }
            """);

        var task = new ReadTsConfigTask
        {
            BuildEngine = _buildEngine,
            TsConfigPath = path
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.True(task.PreserveConstEnums);
        Assert.True(task.ExperimentalDecorators);
        Assert.True(task.Decorators);
        Assert.True(task.EmitDecoratorMetadata);
        Assert.Equal("./src", task.RootDir);
        Assert.Equal("./dist", task.OutDir);
    }

    [Fact]
    public void Execute_FilesArray_SetsEntryFile()
    {
        var path = CreateTsConfig("""
            {
                "files": ["src/main.ts", "src/other.ts"]
            }
            """);

        var task = new ReadTsConfigTask
        {
            BuildEngine = _buildEngine,
            TsConfigPath = path
        };

        var result = task.Execute();

        Assert.True(result);
        // Entry file should be relative to tsconfig.json directory
        Assert.EndsWith("src/main.ts", task.EntryFile.Replace("\\", "/"));
    }

    [Fact]
    public void Execute_EmptyFilesArray_LeavesEntryFileEmpty()
    {
        var path = CreateTsConfig("""
            {
                "files": []
            }
            """);

        var task = new ReadTsConfigTask
        {
            BuildEngine = _buildEngine,
            TsConfigPath = path
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.Equal(string.Empty, task.EntryFile);
    }

    [Fact]
    public void Execute_MalformedJson_ReturnsFalse_LogsError()
    {
        var path = CreateTsConfig("{ invalid json }");

        var task = new ReadTsConfigTask
        {
            BuildEngine = _buildEngine,
            TsConfigPath = path
        };

        var result = task.Execute();

        Assert.False(result);
        Assert.Single(_buildEngine.Errors);
        Assert.Contains("parse", _buildEngine.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_JsonWithComments_Succeeds()
    {
        var path = CreateTsConfig("""
            {
                // This is a comment
                "compilerOptions": {
                    "preserveConstEnums": true /* inline comment */
                }
            }
            """);

        var task = new ReadTsConfigTask
        {
            BuildEngine = _buildEngine,
            TsConfigPath = path
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.True(task.PreserveConstEnums);
    }

    [Fact]
    public void Execute_JsonWithTrailingCommas_Succeeds()
    {
        var path = CreateTsConfig("""
            {
                "compilerOptions": {
                    "preserveConstEnums": true,
                },
            }
            """);

        var task = new ReadTsConfigTask
        {
            BuildEngine = _buildEngine,
            TsConfigPath = path
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.True(task.PreserveConstEnums);
    }

    [Fact]
    public void Execute_NoCompilerOptions_ReturnsDefaults()
    {
        var path = CreateTsConfig("""
            {
                "include": ["src/**/*"],
                "exclude": ["node_modules"]
            }
            """);

        var task = new ReadTsConfigTask
        {
            BuildEngine = _buildEngine,
            TsConfigPath = path
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.False(task.PreserveConstEnums);
        Assert.False(task.ExperimentalDecorators);
        Assert.False(task.Decorators);
        Assert.False(task.EmitDecoratorMetadata);
    }

    [Fact]
    public void Execute_UnknownOptions_IgnoresThem()
    {
        var path = CreateTsConfig("""
            {
                "compilerOptions": {
                    "target": "ES2020",
                    "module": "ESNext",
                    "strict": true,
                    "preserveConstEnums": true
                }
            }
            """);

        var task = new ReadTsConfigTask
        {
            BuildEngine = _buildEngine,
            TsConfigPath = path
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.True(task.PreserveConstEnums);
    }

    [Fact]
    public void Execute_RootDir_SetsProperty()
    {
        var path = CreateTsConfig("""
            {
                "compilerOptions": {
                    "rootDir": "./src"
                }
            }
            """);

        var task = new ReadTsConfigTask
        {
            BuildEngine = _buildEngine,
            TsConfigPath = path
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.Equal("./src", task.RootDir);
    }

    [Fact]
    public void Execute_OutDir_SetsProperty()
    {
        var path = CreateTsConfig("""
            {
                "compilerOptions": {
                    "outDir": "./dist"
                }
            }
            """);

        var task = new ReadTsConfigTask
        {
            BuildEngine = _buildEngine,
            TsConfigPath = path
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.Equal("./dist", task.OutDir);
    }
}
