using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class DecoratorAttributeTests
{
    [Fact]
    public void Obsolete_Attribute_AppliedCorrectly()
    {
        var source = """
            @DotNetType("System.Type")
            declare class Type {
                static GetType(typeName: string): Type;
                IsDefined(attributeType: Type, inherit: boolean): boolean;
            }

            @DotNetType("System.ObsoleteAttribute")
            declare class ObsoleteAttribute {}

            @Obsolete("This is old")
            class OldClass {}

            console.log("Starting test");

            let oldClassType = Type.GetType("OldClass");
            if (oldClassType == null) {
                console.log("OldClass not found");
            } else {
                console.log("OldClass found");
                let obsType = Type.GetType("System.ObsoleteAttribute");
                if (obsType == null) {
                    console.log("System.ObsoleteAttribute not found");
                } else {
                    console.log("System.ObsoleteAttribute found");
                    let isDefined = oldClassType.IsDefined(obsType, false);
                    console.log("IsDefined: " + (isDefined ? "true" : "false"));
                }
            }
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Contains("IsDefined: true", output);
    }

    [Fact]
    public void Deprecated_Attribute_AppliedCorrectly()
    {
        var source = """
            @DotNetType("System.Type")
            declare class Type {
                static GetType(typeName: string): Type;
                IsDefined(attributeType: Type, inherit: boolean): boolean;
            }

            @DotNetType("System.ObsoleteAttribute")
            declare class ObsoleteAttribute {}

            @deprecated("Do not use")
            class DeprecatedClass {}

            console.log("Starting test (deprecated)");

            let t = Type.GetType("DeprecatedClass");
            if (t == null) {
                console.log("DeprecatedClass not found");
            } else {
                let isDefined = t.IsDefined(Type.GetType("System.ObsoleteAttribute"), false);
                console.log("IsDefined: " + (isDefined ? "true" : "false"));
            }
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Contains("IsDefined: true", output);
    }

    [Fact]
    public void Obsolete_Method_Attribute_AppliedCorrectly()
    {
        // Use C# reflection directly since TypeScript-based reflection has overload resolution issues
        var source = """
            class MyClass {
                @Obsolete("Use newMethod instead")
                oldMethod(): void {}

                newMethod(): void {}
            }

            console.log("MyClass defined");
            """;

        var (assembly, output) = TestHarness.CompileAndRun(source, DecoratorMode.Legacy);
        Assert.Contains("MyClass defined", output);

        // Verify using C# reflection
        var myClassType = assembly.GetType("MyClass");
        Assert.NotNull(myClassType);

        var oldMethod = myClassType!.GetMethod("oldMethod");
        var newMethod = myClassType.GetMethod("newMethod");
        Assert.NotNull(oldMethod);
        Assert.NotNull(newMethod);

        Assert.True(Attribute.IsDefined(oldMethod!, typeof(ObsoleteAttribute)), "oldMethod should have [Obsolete]");
        Assert.False(Attribute.IsDefined(newMethod!, typeof(ObsoleteAttribute)), "newMethod should not have [Obsolete]");
    }

    [Fact]
    public void Obsolete_Field_Attribute_AppliedCorrectly()
    {
        var source = """
            class MyClass {
                @Obsolete("Use newField instead")
                oldField: string = "old";

                newField: string = "new";
            }

            console.log("MyClass with fields defined");
            """;

        var (assembly, output) = TestHarness.CompileAndRun(source, DecoratorMode.Legacy);
        Assert.Contains("MyClass with fields defined", output);

        // Verify using C# reflection
        var myClassType = assembly.GetType("MyClass");
        Assert.NotNull(myClassType);

        // Instance fields are compiled as backing fields with __ prefix and PascalCase
        var oldField = myClassType!.GetField("__OldField", BindingFlags.Instance | BindingFlags.NonPublic);
        var newField = myClassType.GetField("__NewField", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(oldField);
        Assert.NotNull(newField);

        Assert.True(Attribute.IsDefined(oldField!, typeof(ObsoleteAttribute)), "oldField should have [Obsolete]");
        Assert.False(Attribute.IsDefined(newField!, typeof(ObsoleteAttribute)), "newField should not have [Obsolete]");
    }

    [Fact]
    public void Obsolete_StaticField_Attribute_AppliedCorrectly()
    {
        var source = """
            class MyClass {
                @Obsolete("Use newStaticField instead")
                static oldStaticField: string = "old";

                static newStaticField: string = "new";
            }

            console.log("MyClass with static fields defined");
            """;

        var (assembly, output) = TestHarness.CompileAndRun(source, DecoratorMode.Legacy);
        Assert.Contains("MyClass with static fields defined", output);

        // Verify using C# reflection
        var myClassType = assembly.GetType("MyClass");
        Assert.NotNull(myClassType);

        // Static fields keep their original names
        var oldField = myClassType!.GetField("oldStaticField", BindingFlags.Static | BindingFlags.Public);
        var newField = myClassType.GetField("newStaticField", BindingFlags.Static | BindingFlags.Public);
        Assert.NotNull(oldField);
        Assert.NotNull(newField);

        Assert.True(Attribute.IsDefined(oldField!, typeof(ObsoleteAttribute)), "oldStaticField should have [Obsolete]");
        Assert.False(Attribute.IsDefined(newField!, typeof(ObsoleteAttribute)), "newStaticField should not have [Obsolete]");
    }

    [Fact]
    public void Obsolete_Accessor_Attribute_AppliedCorrectly()
    {
        var source = """
            class MyClass {
                private _oldValue: string = "old";
                private _newValue: string = "new";

                @Obsolete("Use newValue instead")
                get oldValue(): string {
                    return this._oldValue;
                }

                get newValue(): string {
                    return this._newValue;
                }
            }

            console.log("MyClass with accessors defined");
            """;

        var (assembly, output) = TestHarness.CompileAndRun(source, DecoratorMode.Legacy);
        Assert.Contains("MyClass with accessors defined", output);

        // Verify using C# reflection
        var myClassType = assembly.GetType("MyClass");
        Assert.NotNull(myClassType);

        // Accessors are compiled as get_PascalName methods
        var oldGetter = myClassType!.GetMethod("get_OldValue");
        var newGetter = myClassType.GetMethod("get_NewValue");
        Assert.NotNull(oldGetter);
        Assert.NotNull(newGetter);

        Assert.True(Attribute.IsDefined(oldGetter!, typeof(ObsoleteAttribute)), "get_OldValue should have [Obsolete]");
        Assert.False(Attribute.IsDefined(newGetter!, typeof(ObsoleteAttribute)), "get_NewValue should not have [Obsolete]");
    }
}
