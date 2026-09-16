using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests that ensure compiled DLLs remain standalone (no SharpTS.dll dependency).
/// </summary>
public class StandaloneDllTests
{
    // Allowlist of files that contain intentional SharpTS late-binding patterns.
    // These use graceful fallback: emit tries emitted types first, falls back to
    // interpreter types via Type.GetType() if available. This allows compiled code
    // to work standalone while maintaining compatibility when running with SharpTS.dll.
    //
    // Note: These patterns do NOT create hard dependencies - they're runtime lookups
    // that return null if SharpTS.dll is absent. The compiled code works regardless.
    private static readonly HashSet<string> LateBindingAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "Compilation/RuntimeEmitter.ReflectionHelpers.cs", // the canonical EmitReflectionHelper/EmitReflectionCall(/Void)/EmitReflectionCreateInstance helpers + RuntimeTypesLateBoundName const — #1128
        "Compilation/ConstantFolder.cs",              // SharpTSUndefined detection
        "Compilation/PropertyDescriptorStore.cs",     // SharpTSPropertyDescriptor/Object fallback
        "Compilation/RuntimeEmitter.Worker.cs",       // TypedArray/Worker/Atomics interpreter fallback
        "Compilation/RuntimeEmitter.Proxy.cs",        // Proxy CreateProxy via EmitReflectionCreateInstance(SharpTSProxy) (requires SharpTS.dll at runtime)
        "Compilation/RuntimeEmitter.Json.Proxy.cs",   // JSON.stringify Proxy materialization via TrapOwnKeys/TrapGet (requires SharpTS.dll at runtime)
        "Compilation/RuntimeEmitter.Json.ParseReviver.cs", // JSON.parse reviver Proxy trap dispatch (TrapOwnKeys/TrapGet/TrapSet/TrapDeleteProperty)
        // RuntimeEmitter.Intl.cs / .Date.cs / .AbortController.cs — pruned: migrated to the
        // RuntimeEmitter.ReflectionHelpers.cs canonical helpers (#1128); no inline literals remain.
        "Compilation/RuntimeEmitter.ProcessHelpers.cs",      // ProcessEventEmitterCall and ProcessEmitExit fallback (ProcessBuiltIns target via EmitReflectionCall)
        // "Compilation/RuntimeEmitter.Net.cs" — now uses emitted $NetServer/$NetSocket directly (no reflection)
        "Compilation/RuntimeEmitter.ChildProcessHelpers.cs", // child_process.fork bridges to the interpreter's ForkForCompiledLoop (requires SharpTS.dll co-located; suppressed by --standalone) — #1017
        // ZlibHelpers.cs / DnsPromises.cs — pruned: now pure IL, no SharpTS late binding
        "Compilation/RuntimeEmitter.ClusterHelpers.cs", // cluster bridges to ClusterCompiledBridge — workers run interpreted (requires SharpTS.dll co-located; suppressed by --standalone) — #1171
        "Compilation/RuntimeEmitter.VmHelpers.cs",             // vm module delegation to interpreter via reflection
        "Compilation/RuntimeEmitter.SourceExecution.cs",       // sharpts:execution host bridge (requires the managed SharpTS runtime closure; suppressed by --standalone)
        "Compilation/RuntimeEmitter.DnsResolver.cs",           // dns.Resolver factory via RuntimeTypes
        "Compilation/RuntimeEmitter.Dns.cs",                   // setDefaultResultOrder best-effort sync to RuntimeTypes.DnsConfig (graceful no-op when SharpTS absent; standalone lookup ordering uses the emitted static) — #1072
        "Compilation/ILEmitter.Calls.ExternalInterop.cs",      // @DotNetType delegate shim + event subscription via DotNetDelegateShim/DotNetEventBinder
        "Compilation/CallHandlers/GlobalFunctionHandler.cs",   // eval() indirect dispatch via EvalBridge (graceful throw when SharpTS absent)
    };

    /// <summary>
    /// Scans Compilation/ source files to ensure no direct typeof() references to
    /// SharpTS types that would embed assembly references in emitted IL.
    ///
    /// WRONG: typeof(RuntimeTypes).GetMethod(...) - embeds SharpTS.dll reference
    /// RIGHT: EmitReflectionHelper(typeBuilder, "SomeMethod", argCount) or
    ///        EmitReflectionCall/EmitReflectionCreateInstance
    ///        (Compilation/RuntimeEmitter.ReflectionHelpers.cs) - emit a
    ///        Type.GetType("SharpTS.Compilation.RuntimeTypes, SharpTS") runtime lookup
    /// </summary>
    [Fact]
    public void CompilationFiles_ShouldNotUseTypeofForEmittedIL()
    {
        var repoRoot = RepoPaths.FindRepoRoot();
        var compilationDir = Path.Combine(repoRoot, "src", "SharpTS", "Compilation");
        var violations = new List<string>();

        // Unqualified typeof() references to SharpTS types that must NOT appear in
        // emitted IL — a typeof() embeds a hard metadata token to the SharpTS assembly.
        // (The string-based Type.GetType("…, SharpTS") late-binding form is covered
        // separately by CompilationFiles_ShouldNotIntroduceNewSharpTsLateBindingOutsideAllowlist.)
        var forbiddenPatterns = new[]
        {
            "typeof(RuntimeTypes)",
            "typeof(PropertyDescriptorStore)",
            "typeof(ObjectBuiltIns)",
            "typeof(SharpTSArray)",
            "typeof(SharpTSObject)",
        };

        // General case the explicit list above can't enumerate: any fully-qualified
        // typeof(SharpTS.<...>) (e.g. typeof(SharpTS.Runtime.Types.SharpTSArray))
        // likewise embeds an assembly token into emitted IL.
        var qualifiedSharpTsTypeof = new Regex(@"typeof\(\s*SharpTS\.", RegexOptions.Compiled);

        foreach (var file in Directory.GetFiles(compilationDir, "*.cs", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);

            // Skip the RuntimeTypes files themselves - they define the types, not emit IL referencing them
            if (fileName.StartsWith("RuntimeTypes."))
                continue;

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();

                // Skip comments
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("/*"))
                    continue;

                if (forbiddenPatterns.Any(line.Contains) || qualifiedSharpTsTypeof.IsMatch(line))
                {
                    violations.Add($"{fileName}:{i + 1}: {trimmed}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Found {violations.Count} typeof() references that create SharpTS.dll dependencies in emitted IL.\n" +
            $"Use a Type.GetType(\"…, SharpTS\") reflection helper instead — EmitReflectionHelper / EmitReflectionCall(/Void) / EmitReflectionCreateInstance in Compilation/RuntimeEmitter.ReflectionHelpers.cs.\n\n" +
            string.Join("\n", violations.Take(20)));
    }

    [Fact]
    public void CompilationFiles_ShouldNotIntroduceNewSharpTsLateBindingOutsideAllowlist()
    {
        var repoRoot = RepoPaths.FindRepoRoot();
        var compilationDir = Path.Combine(repoRoot, "src", "SharpTS", "Compilation");
        var violations = new List<string>();

        // Any of these patterns indicates runtime coupling to SharpTS via late binding.
        var pattern = new Regex(
            "Type\\.GetType\\(\"SharpTS\\.|\"[^\"]+,\\s*SharpTS\"",
            RegexOptions.Compiled);

        foreach (var file in Directory.GetFiles(compilationDir, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Path.Combine(repoRoot, "src", "SharpTS"), file).Replace('\\', '/');
            if (LateBindingAllowlist.Contains(relative))
                continue;

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
                    continue;

                if (pattern.IsMatch(lines[i]))
                {
                    violations.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Found SharpTS late-binding patterns outside the current allowlist.\n" +
            "Either migrate those call sites to emitted runtime types or explicitly add a temporary allowlist entry.\n\n" +
            string.Join("\n", violations.Take(50)));
    }

    [Fact]
    public void CompiledDll_ShouldNotReferenceSharpTsAssembly()
    {
        var source = """
            const obj = { a: 1 };
            console.log(obj.a);
            """;

        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            var refs = GetAssemblyReferences(dllPath);
            Assert.DoesNotContain(refs, r => r == "SharpTS");
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_JsonParse_ShouldExecuteWithoutSharpTsDll()
    {
        var source = """
            const parsed: any[] = JSON.parse(
                '[{"id":1,"label":"a"},{"\\u0069d":2,"label":"b"}]');
            console.log(parsed[0].id, parsed[1].id, parsed[1].label);
            """;

        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            Assert.DoesNotContain(GetAssemblyReferences(dllPath), r => r == "SharpTS");
            Assert.Equal("1 2 b\n", ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_ShapedJsonRoundTrip_ShouldExecuteWithoutSharpTsDll()
    {
        var source = """
            const payload: {
                items: { id: number; label: string; active: boolean }[]
            } = {
                items: [{ id: 1, label: "a", active: true }]
            };
            const json: string = JSON.stringify(payload);
            const parsed: any = JSON.parse(json);
            console.log(json);
            console.log(parsed.items[0].id, parsed.items[0].label,
                parsed.items[0].active);
            """;

        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            Assert.DoesNotContain(
                GetAssemblyReferences(dllPath), r => r == "SharpTS");
            Assert.Equal(
                "{\"items\":[{\"id\":1,\"label\":\"a\",\"active\":true}]}\n" +
                "1 a true\n",
                ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// Broader sibling of <see cref="CompiledDll_ShouldNotReferenceSharpTsAssembly"/>: the trivial
    /// program there only exercises one code path. This compiles a battery of diverse language
    /// features and asserts each output's emitted metadata carries no SharpTS assembly reference —
    /// catching a hard dependency introduced by an indirect mechanism the source-level lints can't
    /// model. None of these features touch the soft-dependency surface (eval/Proxy/Intl/vm/dns/
    /// Worker/@DotNetType), so every output must be fully standalone.
    /// </summary>
    [Theory]
    [MemberData(nameof(StandaloneFeatureBattery))]
    public void CompiledDll_FeatureBattery_ShouldNotReferenceSharpTsAssembly(string feature, string source)
    {
        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            var refs = GetAssemblyReferences(dllPath);
            Assert.True(
                refs.All(r => r != "SharpTS"),
                $"Feature '{feature}' emitted a SharpTS assembly reference: {string.Join(", ", refs)}");
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    public static IEnumerable<object[]> StandaloneFeatureBattery()
    {
        yield return ["classes", """
            abstract class Animal { #id = 1; constructor(public name: string){} abstract speak(): string; getId(){ return this.#id; } }
            class Dog extends Animal { speak(){ return this.name + " woof"; } }
            const d = new Dog("rex"); console.log(d.speak(), d.getId());
            """];
        yield return ["async", """
            async function f(x: number): Promise<number> { return x * 2; }
            async function main(){ const a = await f(21); const b = await Promise.all([f(1), f(2)]); console.log(a, b[0], b[1]); }
            main();
            """];
        yield return ["generators", """
            function* gen(n: number){ for (let i=0;i<n;i++) yield i*i; }
            let s = 0; for (const v of gen(5)) s += v; console.log(s);
            """];
        yield return ["regex", """
            const re = /(\d+)-(\d+)/g; const str = "12-34 56-78";
            let m; let total = 0;
            while ((m = re.exec(str)) !== null) { total += parseInt(m[1]) + parseInt(m[2]); }
            console.log(total, "abc123".replace(/\d/g, "#"));
            """];
        yield return ["mapset", """
            const m = new Map<string, number>(); m.set("a",1).set("b",2);
            const s = new Set([1,1,2,3]); const wm = new WeakMap(); const k = {};
            wm.set(k, 99);
            console.log(m.get("a")! + m.get("b")!, s.size, wm.get(k));
            """];
        yield return ["destructure", """
            const [a, ...rest] = [1,2,3,4]; const {x, y=10} = {x: 5} as any;
            const tpl = `sum=${a + x + y + rest.length}`; console.log(tpl);
            """];
        yield return ["errors", """
            class MyErr extends Error { constructor(m: string){ super(m); this.name = "MyErr"; } }
            try { throw new MyErr("boom"); } catch(e){ if (e instanceof MyErr) console.log(e.name, e.message); }
            finally { console.log("done"); }
            """];
        yield return ["enums-ns", """
            enum Color { Red, Green = 5, Blue }
            namespace Geo { export function area(r: number){ return 3 * r * r; } }
            console.log(Color.Blue, Color[5], Geo.area(2));
            """];
    }

    [Fact]
    public void Isolated_HttpCreateServer_ShouldExecuteWithoutSharpTsDll()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as http from "http";
                const server = http.createServer((req: any, res: any) => { res.end("ok"); });
                console.log(typeof server);
                server.close();
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("object\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// vm is a soft dependency: a normal --compile co-locates SharpTS.dll, but a --standalone
    /// build suppresses the copy and the vm path must throw a clear "not supported" error at
    /// runtime instead of silently degrading (cf. the eval/tls standalone lesson). The test
    /// helper never co-locates SharpTS.dll, so running the isolated DLL exercises exactly the
    /// --standalone path.
    /// </summary>
    [Fact]
    public void Isolated_Vm_ThrowsClearly_WhenSharpTsRuntimeAbsent()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { runInNewContext } from 'vm';
                try {
                    runInNewContext('1 + 1');
                } catch (error) {
                    console.log('caught: ' + error.message);
                }
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            // Catch inside the probe so this assertion does not depend on Windows
            // unhandled-exception reporting finishing before the process deadline.
            // A named function import defers the missing-runtime lookup to the call.
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.StartsWith("caught: ", output);
            Assert.Contains("vm module is not supported in standalone", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// A vm program records the "vm module" soft-dependency reason so the CLI co-locates
    /// SharpTS.dll next to non-standalone output.
    /// </summary>
    [Fact]
    public void Vm_RecordsSharpTsRuntimeDependency()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_vm_dep_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var entryPath = Path.Combine(tempDir, "main.ts");
            File.WriteAllText(entryPath, """
                import * as vm from 'vm';
                console.log(vm.runInNewContext('1 + 1'));
                """);

            var resolver = new ModuleResolver(entryPath);
            var entryModule = resolver.LoadModule(entryPath);
            var modules = resolver.GetModulesInOrder(entryModule);
            var typeMap = TestHarness.CheckModulesOrThrow(new TypeChecker(), modules, resolver);
            var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(modules.SelectMany(m => m.Statements).ToList());

            var compiler = new ILCompiler("vm_dep_test");
            compiler.CompileModules(modules, resolver, typeMap, deadCodeInfo);

            Assert.Contains("vm module", compiler.RequiredSharpTSRuntimeReasons);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// cluster is a soft dependency (#1171): compiled cluster late-binds into
    /// ClusterCompiledBridge (workers run interpreted), so a normal --compile co-locates
    /// SharpTS.dll while --standalone must throw a clear error rather than silently
    /// degrade (the tls lesson, #1033).
    /// </summary>
    [Fact]
    public void Isolated_Cluster_ThrowsClearly_WhenSharpTsRuntimeAbsent()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { fork } from 'cluster';
                try {
                    fork();
                } catch (error) {
                    console.log('caught: ' + error.message);
                }
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            // A namespace import would read cluster state before entering the try block.
            // Invoke the named function inside it and require a normal process exit.
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.StartsWith("caught: ", output);
            Assert.Contains("cluster requires the SharpTS runtime", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// A cluster program records the "cluster" soft-dependency reason so the CLI
    /// co-locates SharpTS.dll next to non-standalone output (#1171).
    /// </summary>
    [Fact]
    public void Cluster_RecordsSharpTsRuntimeDependency()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_cluster_dep_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var entryPath = Path.Combine(tempDir, "main.ts");
            File.WriteAllText(entryPath, """
                import * as cluster from 'cluster';
                if (cluster.isPrimary) {
                    cluster.fork();
                }
                """);

            var resolver = new ModuleResolver(entryPath);
            var entryModule = resolver.LoadModule(entryPath);
            var modules = resolver.GetModulesInOrder(entryModule);
            var typeMap = TestHarness.CheckModulesOrThrow(new TypeChecker(), modules, resolver);
            var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(modules.SelectMany(m => m.Statements).ToList());

            var compiler = new ILCompiler("cluster_dep_test");
            compiler.CompileModules(modules, resolver, typeMap, deadCodeInfo);

            Assert.Contains("cluster", compiler.RequiredSharpTSRuntimeReasons);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_BroadcastChannel_ShouldExecuteWithoutSharpTsDll()
    {
        var source = """
            const a = new BroadcastChannel('iso');
            const b = new BroadcastChannel('iso');
            b.on('message', (e: any) => { console.log('got:', e.data); });
            a.postMessage('hello');
            a.close();
            b.close();
            """;

        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("got: hello\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_Atomics_ShouldExecuteWithoutSharpTsDll()
    {
        var source = """
            const view = new Int32Array(1);
            Atomics.store(view, 0, 123);
            console.log(Atomics.load(view, 0));
            """;

        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("123\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_DataView_ShouldExecuteWithoutSharpTsDll()
    {
        var source = """
            const ab = new ArrayBuffer(8);
            const dv = new DataView(ab);
            dv.setUint32(0, 0xffffffff);
            console.log(dv.getUint32(0));
            """;

        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("4294967295\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_CryptoSignVerify_ShouldExecuteWithoutSharpTsDll()
    {
        // Use a valid RSA private key (same as in CryptoSignVerifyTests)
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from "crypto";
                const privateKey = `-----BEGIN PRIVATE KEY-----
                MIIEvAIBADANBgkqhkiG9w0BAQEFAASCBKYwggSiAgEAAoIBAQDQ9wlSXsD5OUJ3
                vIhuDuW1J6YN5dvbxAxqZRe4Cm2BXbY1l+tC//x9iZKrkKBbtsBjl3U2WXpwpcWf
                rpqdMrQmxeb1gVHwHUrOamdO5+SG8Zy0ojKWxPUIkahHA5wRacsApLetHAS5N5US
                vlzymEhs/jfhGpcl21fAH72kJuxoRF19tNWACSSb+DpeOHXWHm/PqenFI1ZShK+X
                qkxeFBN8hHUUhhTxdd+LH7n9ImhhDxb6y9Nlio5lZrfHzPLtZ/EHTIyaxV80wHZ0
                H4FER0kybl5Jso0VYBKMEZkWrOto9LlN/EmXmCdkJIZhGjVR/noH1v2jC/xDZ5n9
                8khHsi/lAgMBAAECggEAEC9SFYMpRyRcNZHwrzWQLRvJDMKE6NyiaYsy7xo/qQlt
                F3GQ0zuofsCtD4TAJtpcxFnyxibgCOGOEPQhHZPTyD0DyngdtI9QP/SV09K6LImC
                LatyZ6MRp3xAoF9zMxYSlxYq88l7xCy96xm7cT7CPU7jXRgGJPR8M3FB6vjozpp5
                GYfByegTEzyBiZ689H9Q2syhU+F9MJnQzZsyJLZ1RsLcKOnqCifhWWiDMjqt+EKU
                WQc72dxaveekhp/ISNzo0iCMGCxDi9i2ModAg9wURB7aB7MTIiChzHEQ6hmSeXy0
                feiTLTsvG7e99uSiV/GBV2FtTQ+kqpaUQtZ8uezggQKBgQDvjp7UtrjdNJBQmRPP
                fbGg8uwXoKpVkeZt1F1u0TPJx6UPYqwMQcHGJCVaHhUQ8sI8Sppf+ySc3onR9ix0
                Oai7CGR3hM/Nhy8bf+0h8qUbuSuItlrF+J7lwsLymlMQkN/X3w/bnudN7sLSjTsu
                oKB8rbLI2q62lalBVEAHtBIozQKBgQDfTt3A9GP/0r/UexfPiaJmqTl6KSwWos/Q
                A6dBNhmugwmdLtJru+mVZ0mgFgfiepQJG1W38ynL4/GNv6g3QYFlP/9fFfDxfvRN
                rYRTDtRnKNBXxE712pkInzPAWYcDpEcDMVdLLqLppxDslWeJlr71cJiSdp1WG7n3
                WxVTUSyDeQKBgEsnIwz4heZfpyah32UouaEUlJyU+tr9epzaErXBS83xpAa/ndn6
                hx/yFwW+ij1W6zie7u9Nip7r8bC82hVcQWLrrxkPwWFpF445A9uyk7muzcmF69RP
                uwm5oA8b+xMnYBIJGKB9qXL5hIUpaXenTLHQjFYWxNji+sZT+AJyq3/BAoGAN3qG
                iVuuRG59jjKOtdcB6/N6/iigdXc5nfpqYT8pnjub9dseF/n1jFK+7fDLQK8nfCO4
                Zh0ZczhMWOUWy7OQjDEcJulylOzvkSTczS3QA1kWedehrl8CyiuTVeRoMLVtlxN5
                FoqdmuMQx1ZPBNXY122D2k9xw2TcDOIqKCrwnjECgYAv24vgAfSQRZLd3MrK41El
                xGWRu1CAYmdC2UXV92cJpGAB4irVxs+E7u9qTu1zqZA5ZzbRzz1uxgnhKenC62uh
                cWwcgaiz9LOlCil1gb3bazz0V6HiWrmi++soWhPPNMYSgE002KFHq58G2e1nwBmU
                pYlA6+GlII/hM4c3iRjCyQ==
                -----END PRIVATE KEY-----`;
                const sign = crypto.createSign("sha256");
                sign.update("hello");
                const sig = sign.sign(privateKey, "hex");
                console.log(typeof sig);
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 20000);
            Assert.Equal("string\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_FsBasic_ShouldExecuteWithoutSharpTsDll()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from "fs";
                console.log(fs.existsSync("missing_file_for_guard.txt"));
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("false\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// Phase 23 guardrail: Verifies TypedArray operations work standalone.
    /// </summary>
    [Fact]
    public void Isolated_TypedArray_ShouldExecuteWithoutSharpTsDll()
    {
        var source = """
            const i8 = new Int8Array(4);
            i8[0] = 127;
            i8[1] = -128;
            const f32 = new Float32Array(2);
            f32[0] = 3.14;
            console.log(i8[0], i8[1], i8.length, f32[0].toFixed(2));
            """;

        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("127 -128 4 3.14\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_ForAwaitOfSyncIterable_ShouldExecuteWithoutSharpTsDll()
    {
        var source = """
            async function main() {
                const values: any[] = [];
                for await (const x of [1, Promise.resolve(2)]) values.push(x);
                for await (const x of "ok") values.push(x);
                console.log(JSON.stringify(values));
            }
            main();
            """;

        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            Assert.DoesNotContain(GetAssemblyReferences(dllPath), r => r == "SharpTS");
            Assert.Equal("[1,2,\"o\",\"k\"]\n", ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// #1289: yield* over emitted typed arrays and Buffers must use only emitted runtime
    /// helpers and continue to execute without a SharpTS.dll dependency.
    /// </summary>
    [Fact]
    public void Isolated_YieldStarTypedArrayAndBuffer_ShouldExecuteWithoutSharpTsDll()
    {
        var source = """
            function* values() {
                yield* new Uint8Array([1, 2]);
                yield* Buffer.from([3, 4]);
            }
            console.log(JSON.stringify([...values()]));
            """;

        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            Assert.DoesNotContain(GetAssemblyReferences(dllPath), r => r == "SharpTS");
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("[1,2,3,4]\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// Phase 23 guardrail: Verifies SharedArrayBuffer works standalone.
    /// </summary>
    [Fact]
    public void Isolated_SharedArrayBuffer_ShouldExecuteWithoutSharpTsDll()
    {
        var source = """
            const sab = new SharedArrayBuffer(16);
            const view = new Int32Array(sab);
            view[0] = 42;
            console.log(view[0], sab.byteLength);
            """;

        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("42 16\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// Phase 23 guardrail: Verifies util module works standalone.
    /// </summary>
    [Fact]
    public void Isolated_UtilModule_ShouldExecuteWithoutSharpTsDll()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from "util";
                console.log(util.format("Hello %s", "World"));
                console.log(util.types.isDate(new Date()));
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("Hello World\ntrue\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// Phase 23 guardrail: Verifies path module works standalone.
    /// </summary>
    [Fact]
    public void Isolated_PathModule_ShouldExecuteWithoutSharpTsDll()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from "path";
                const p = path.join("a", "b", "c");
                console.log(path.basename(p));
                console.log(path.extname("file.txt"));
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("c\n.txt\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// #925 guardrail: an exported async function / generator used as an imported value pads omitted
    /// optional args with the `undefined` sentinel (via the $PadUndefined attribute, which is defined
    /// in the OUTPUT assembly) — so this must keep running with no SharpTS.dll on disk.
    /// </summary>
    [Fact]
    public void Isolated_OptionalArgUndefinedPadding_ShouldExecuteWithoutSharpTsDll()
    {
        var files = new Dictionary<string, string>
        {
            ["helper.ts"] = """
                export async function h(x?: any): Promise<string> { return typeof x; }
                export function* gen(x?: any): Generator<string> { yield typeof x; }
                """,
            ["main.ts"] = """
                import { h, gen } from "./helper";
                async function main() {
                    console.log(await h());
                    console.log(gen().next().value);
                }
                main();
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("undefined\nundefined\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// Phase 23 guardrail: Verifies crypto hash operations work standalone.
    /// </summary>
    [Fact]
    public void Isolated_CryptoHash_ShouldExecuteWithoutSharpTsDll()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from "crypto";
                const hash = crypto.createHash("sha256");
                hash.update("hello");
                const digest = hash.digest("hex");
                console.log(digest.substring(0, 8));
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            // SHA256 of "hello" starts with "2cf24dba"
            Assert.Equal("2cf24dba\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void ArrayBufferSlicesViewsCloningAndBufferInteropRunStandalone()
    {
        const string source = """
            const buffer = new ArrayBuffer(16);
            const bytes = new Uint8Array(buffer);
            bytes[0] = 42;
            const view = new DataView(buffer, 4, 8);
            view.setUint32(0, 0x12345678, false);
            console.log(buffer.byteLength, view.byteOffset, view.getUint32(0, false));
            const slice = buffer.slice(0, 8);
            bytes[0] = 99;
            console.log(new Uint8Array(slice)[0], slice.byteLength);
            const dynamic: any = buffer;
            const bound = dynamic.slice;
            console.log(bound(4, 8).byteLength, dynamic.slice(-4).byteLength);
            const isView = ArrayBuffer.isView;
            console.log(isView(view), isView(bytes), isView(buffer), isView(null));
            console.log(bytes.buffer === buffer, view.buffer === buffer, buffer instanceof ArrayBuffer);
            const cloned = structuredClone(buffer);
            bytes[0] = 7;
            console.log(new Uint8Array(cloned)[0], new Uint8Array(buffer)[0]);
            try { new ArrayBuffer(-1); } catch (error) { console.log('invalid length'); }
            const nodeBuffer = Buffer.from(buffer);
            bytes[0] = 11;
            console.log(nodeBuffer[0]);
            """;
        var errors = TestHarness.CompileAndVerifyOnly(source);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
            Assert.False(File.Exists(Path.Combine(tempDir, "SharpTS.dll")));
            Assert.Equal("16 4 305419896\n42 8\n4 4\ntrue true false false\ntrue true true\n99 7\ninvalid length\n11\n",
                ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void BufferNumericEncodingModuleAndTypedArrayOperationsRunStandalone()
    {
        const string source = """
            import { Buffer, atob, btoa, isUtf8, isAscii, transcode } from 'buffer';
            const bytes = Buffer.from('hello');
            console.log(bytes.toString('base64'));
            console.log(Buffer.concat([bytes, Buffer.from('!')]).toString());
            const numbers = Buffer.alloc(32);
            numbers.writeUInt32LE(0x12345678, 0);
            numbers.writeIntBE(-42, 4, 3);
            numbers.writeDoubleBE(1.5, 8);
            numbers.writeBigInt64LE(-123n, 16);
            console.log(numbers.readUInt32LE(0), numbers.readIntBE(4, 3), numbers.readDoubleBE(8), numbers.readBigInt64LE(16));
            console.log(atob(btoa('abc')), isUtf8(bytes), isAscii(bytes));
            console.log(transcode(bytes, 'utf8', 'utf16le').length);
            console.log(Buffer.from([1, 2, 3, 4]).swap16().toString('hex'));
            const view = new Uint16Array([0x1234, 0x5678]);
            const copy = Buffer.copyBytesFrom(view, 1, 1);
            view[1] = 0;
            console.log(copy.length, copy.readUInt16LE(0));
            console.log(Buffer.from(new Uint8Array([1, 2, 3])).toString('hex'));
            function* items() { yield* Buffer.from([4, 5]); yield* new Uint8Array([6]); }
            console.log([...items()].join(','));
            """;
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        var errors = TestHarness.CompileModulesAndVerifyOnly(files, "main.ts");
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.False(File.Exists(Path.Combine(tempDir, "SharpTS.dll")));
            Assert.Equal("aGVsbG8=\nhello!\n305419896 -42 1.5 -123n\nabc true true\n10\n02010403\n2 22136\n010203\n4,5,6\n",
                ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void NodeCryptoAndWebCryptoIntegrationVerifyAndRunStandalone()
    {
        const string source = """
            import * as crypto from 'crypto';
            console.log(crypto.createHash('sha256').update('abc').digest('hex').length);
            async function main() {
                const digest = await crypto.subtle.digest('SHA-256', Buffer.from('abc'));
                console.log(Buffer.from(digest).toString('hex'));
                console.log(crypto.randomUUID().length);
            }
            main();
            """;
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        var errors = TestHarness.CompileModulesAndVerifyOnly(files, "main.ts");
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.False(File.Exists(Path.Combine(tempDir, "SharpTS.dll")));
            Assert.Equal("64\nba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad\n36\n",
                ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void WebCryptoKeyLifecycleAndPromiseRejectionRunStandalone()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                console.log(crypto.webcrypto === crypto.webcrypto, crypto.subtle === crypto.webcrypto.subtle);
                const bytes = Buffer.alloc(8);
                console.log(crypto.getRandomValues(bytes) === bytes);
                async function main() {
                    const subtle = crypto.subtle;
                    const key = await subtle.generateKey({ name: 'HMAC', hash: 'SHA-256', length: 128 }, true, ['sign', 'verify']);
                    const raw = await subtle.exportKey('raw', key);
                    console.log(Buffer.from(raw).length);
                    const imported = await subtle.importKey('raw', raw, { name: 'HMAC', hash: 'SHA-256' }, true, ['sign', 'verify']);
                    const signature = await subtle.sign('HMAC', imported, Buffer.from('data'));
                    console.log(await subtle.verify('HMAC', imported, signature, Buffer.from('data')),
                        await subtle.verify('HMAC', imported, signature, Buffer.from('other')));
                    const base = await subtle.importKey('raw', Buffer.from('password'), 'PBKDF2', false, ['deriveBits', 'deriveKey']);
                    const algorithm = { name: 'PBKDF2', salt: Buffer.from('salt'), iterations: 2, hash: 'SHA-256' };
                    const bits = await subtle.deriveBits(algorithm, base, 128);
                    console.log(Buffer.from(bits).toString('hex') === crypto.pbkdf2Sync('password', 'salt', 2, 16, 'sha256').toString('hex'));
                    const aes = await subtle.deriveKey(algorithm, base, { name: 'AES-GCM', length: 128 }, true, ['encrypt', 'decrypt']);
                    const options = { name: 'AES-GCM', iv: Buffer.alloc(12) };
                    const encrypted = await subtle.encrypt(options, aes, Buffer.from('webcrypto'));
                    console.log(Buffer.from(await subtle.decrypt(options, aes, encrypted)).toString('utf8'));
                    try { await subtle.digest('unsupported', Buffer.from('data')); }
                    catch (error) { console.log('rejected'); }
                }
                main();
                """
        };
        var errors = TestHarness.CompileModulesAndVerifyOnly(files, "main.ts");
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.False(File.Exists(Path.Combine(tempDir, "SharpTS.dll")));
            Assert.Equal("true true\ntrue\n16\ntrue false\ntrue\nwebcrypto\nrejected\n",
                ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void HttpAcceptRequestResponseAndCloseRunStandalone()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { createServer } from 'http';
                const factory = createServer;
                const server = factory((req: any, res: any) => {
                    console.log(req.method + ' ' + req.url);
                    let bytes = 0;
                    req.on('data', (chunk: any) => { bytes += chunk.length; });
                    req.on('end', () => {
                        console.log('complete=' + req.complete);
                        console.log('bytes=' + bytes);
                        res.setHeader('X-Test', 'value');
                        res.writeHead(201);
                        res.write('part-');
                        res.end(Buffer.from('body'));
                    });
                });
                server.listen(0, '127.0.0.1', async () => {
                    try {
                        const response = await fetch('http://127.0.0.1:' + server.address().port + '/probe', {
                            method: 'POST', body: 'payload'
                        });
                        console.log('status=' + response.status);
                        console.log('header=' + response.headers.get('x-test'));
                        console.log('body=' + await response.text());
                    } finally {
                        server.close(() => console.log('closed'));
                    }
                });
                """
        };
        var errors = TestHarness.CompileModulesAndVerifyOnly(files, "main.ts");
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000);
            Assert.Contains("POST /probe\n", output);
            Assert.Contains("complete=true\n", output);
            Assert.Contains("bytes=7\n", output);
            Assert.Contains("status=201\n", output);
            Assert.Contains("header=value\n", output);
            Assert.Contains("body=part-body\n", output);
            Assert.EndsWith("closed\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// Phase 23 guardrail: Scans compiled DLL for forbidden SharpTS late-binding strings.
    /// These strings should NOT appear in standalone output as they indicate runtime dependency.
    /// </summary>
    [Fact]
    public void CompiledDll_ShouldNotContainForbiddenSharpTsStrings()
    {
        var source = """
            // Test various features that historically had SharpTS dependencies
            const arr = [1, 2, 3];
            const obj = { a: 1, b: 2 };
            const buf = new ArrayBuffer(8);
            const view = new Int32Array(buf);
            view[0] = 42;
            console.log(arr.length, Object.keys(obj).length, view[0]);
            """;

        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            // Read the DLL as binary and scan for forbidden strings
            var dllBytes = File.ReadAllBytes(dllPath);
            var dllContent = System.Text.Encoding.UTF8.GetString(dllBytes);

            // Patterns that indicate SharpTS runtime coupling
            var forbiddenPatterns = new[]
            {
                "SharpTS.Runtime.Types.SharpTSArray, SharpTS",
                "SharpTS.Runtime.Types.SharpTSObject, SharpTS",
                "SharpTS.Runtime.Types.SharpTSBuffer, SharpTS",
                "SharpTS.Runtime.Types.SharpTSArrayBuffer, SharpTS",
                "SharpTS.Runtime.Types.SharpTSInt32Array, SharpTS",
                "SharpTS.Runtime.Types.SharpTSUndefined, SharpTS",
                "SharpTS.Compilation.PropertyDescriptorStore, SharpTS",
                "SharpTS.Runtime.BuiltIns.ObjectBuiltIns, SharpTS",
            };

            var foundPatterns = forbiddenPatterns
                .Where(p => dllContent.Contains(p))
                .ToList();

            Assert.True(
                foundPatterns.Count == 0,
                $"Compiled DLL contains forbidden SharpTS late-binding strings:\n" +
                string.Join("\n", foundPatterns.Select(p => $"  - {p}")));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// Phase 23 guardrail: Comprehensive test of core standalone features.
    /// Covers objects, arrays, typed arrays, string operations, and more.
    /// </summary>
    [Fact]
    public void Isolated_Comprehensive_ShouldExecuteWithoutSharpTsDll()
    {
        var source = """
            // Object operations
            const obj = { a: 1, b: 2 };
            const keys = Object.keys(obj);
            const values = Object.values(obj);

            // Array operations
            const arr = [1, 2, 3, 4, 5];
            const mapped = arr.map(x => x * 2);
            const filtered = arr.filter(x => x > 2);

            // TypedArray operations
            const ta = new Int32Array(3);
            ta[0] = 10; ta[1] = 20; ta[2] = 30;

            // String operations
            const str = "hello world";
            const upper = str.toUpperCase();

            // Math operations
            const max = Math.max(1, 5, 3);

            // Date operations
            const d = new Date(0);
            const year = d.getFullYear();

            console.log(keys.length, values.length);
            console.log(mapped[0], filtered.length);
            console.log(ta.length, ta[1]);
            console.log(upper.substring(0, 5));
            console.log(max, year);
            """;

        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(5, lines.Length);
            Assert.Equal("2 2", lines[0]);
            Assert.Equal("2 3", lines[1]);
            Assert.Equal("3 20", lines[2]);
            Assert.Equal("HELLO", lines[3]);
            // Year could be 1969 or 1970 depending on timezone
            Assert.StartsWith("5 19", lines[4]);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_AssertModule_ShouldExecuteWithoutSharpTsDll()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { ok, strictEqual, deepStrictEqual } from 'assert';
                ok(true);
                strictEqual(42, 42);
                deepStrictEqual([1, 2], [1, 2]);
                console.log('assertions passed');
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("assertions passed\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_BufferModule_ShouldExecuteWithoutSharpTsDll()
    {
        var source = """
            const buf = Buffer.from('hello');
            console.log(buf.toString());
            console.log(buf.length);
            """;

        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("hello\n5\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    public static IEnumerable<object[]> ChildProcessMetadataPrograms
    {
        get
        {
            yield return new object[]
            {
                """
                import { execSync, spawnSync, execFileSync } from 'child_process';
                console.log(execSync('echo sync').trim());
                const sorted = spawnSync('sort', [], { input: 'banana\napple\n' });
                console.log(sorted.status, sorted.stdout.trim().replace(/\r?\n/g, ','));
                const file = process.platform === 'win32' ? 'cmd.exe' : '/bin/echo';
                const args = process.platform === 'win32' ? ['/c', 'echo', 'direct'] : ['direct'];
                console.log(execFileSync(file, args).trim());
                """,
                "sync\n0 apple,banana\ndirect\n"
            };
            yield return new object[]
            {
                """
                import { exec, execFile } from 'child_process';
                exec('echo callback', (error: any, output: any, stderr: any) => {
                    console.log(error === null, output.trim(), stderr.length);
                    const file = process.platform === 'win32' ? 'cmd.exe' : '/bin/echo';
                    const args = process.platform === 'win32' ? ['/c', 'echo', 'file'] : ['file'];
                    execFile(file, args, (failure: any, text: any) => console.log(failure === null, text.trim()));
                });
                """,
                "true callback 0\ntrue file\n"
            };
            yield return new object[]
            {
                """
                import { spawn } from 'child_process';
                const child = spawn('sort', []);
                let text = '';
                child.stdout.on('data', (chunk: any) => { text += chunk.toString(); });
                child.on('close', (code: any) => console.log(code, text.trim().replace(/\r?\n/g, ',')));
                child.stdin.write('pear\n'); child.stdin.end('apple\n');
                """,
                "0 apple,pear\n"
            };
            yield return new object[]
            {
                """
                import { spawn } from 'child_process';
                const child = spawn('echo ignored', [], { shell: true, stdio: 'ignore' });
                console.log(child.stdin === null, child.stdout === null, child.stderr === null);
                child.on('close', (code: any) => console.log('close', code));
                """,
                "true true true\nclose 0\n"
            };
            yield return new object[]
            {
                """
                import { spawn } from 'child_process';
                const child = spawn('sharpts_metadata_missing_executable_1599', []);
                child.on('error', (error: any) => console.log(error.code, error.path));
                """,
                "ENOENT sharpts_metadata_missing_executable_1599\n"
            };
            yield return new object[]
            {
                """
                import { exec } from 'child_process';
                exec('echo bytes', { encoding: 'buffer' }, (error: any, output: any) => {
                    console.log(error === null, Buffer.isBuffer(output), output.toString().trim());
                });
                """,
                "true true bytes\n"
            };
            yield return new object[]
            {
                """
                import { exec } from 'child_process';
                exec('echo aaaaaaaaaaaaaaaa', { maxBuffer: 3 }, (error: any, output: any) => {
                    console.log(error.code, output.length <= 3);
                });
                """,
                "ERR_CHILD_PROCESS_STDIO_MAXBUFFER true\n"
            };
            yield return new object[]
            {
                """
                import { fork } from 'child_process';
                try { fork('./metadata-child.ts'); }
                catch (error: any) { console.log(error.message.includes('child_process.fork requires the SharpTS runtime')); }
                """,
                "true\n"
            };
        }
    }

    [Theory]
    [MemberData(nameof(ChildProcessMetadataPrograms))]
    public void Isolated_ChildProcessMetadata_PreservesExecutionStreamsErrorsAndDependencyContract(string source, string expected)
    {
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.DoesNotContain(GetAssemblyReferences(dllPath), name => name == "SharpTS");
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally { CleanupTempDir(tempDir); }
    }

    public static IEnumerable<object[]> HostPrimitiveMetadataPrograms =>
    [
        new object[]
        {
            """
            import {performance as clock} from 'node:perf_hooks';
            const first=clock.now();const second=clock.now();
            console.log(Number.isFinite(first),first>=0,second>=first,Number.isFinite(clock.timeOrigin));
            """,
            "true true true true\n", "main.ts"
        },
        new object[]
        {
            """
            import {performance} from 'perf_hooks';
            performance.clearMarks();performance.clearMeasures();
            performance.mark('start',{startTime:5});performance.mark('end',{startTime:10});
            const result=performance.measure('elapsed','start','end');
            console.log(result.name,result.entryType,result.startTime,result.duration);
            console.log(performance.getEntriesByType('mark').length,performance.getEntriesByName('elapsed').length);
            performance.clearMarks();performance.clearMeasures();console.log(performance.getEntries().length);
            """,
            "elapsed measure 5 5\n2 1\n0\n", "main.ts"
        },
        new object[]
        {
            """
            import {performance,PerformanceObserver} from 'perf_hooks';console.log(typeof PerformanceObserver,typeof performance.now,performance.now()>=0);
            """,
            "function function true\n", "main.ts"
        },
        new object[]
        {
            """
            import {isatty as check} from 'node:tty';
            console.log(check(0),check(1),check(2),check(999));
            console.log(check(-1),check(NaN),check(Infinity),check(-Infinity));
            console.log(check('1' as any),check(null as any),check(undefined as any));
            """,
            "false false false false\nfalse false false false\nfalse false false\n", "main.ts"
        },
        new object[]
        {
            """
            import * as tty from 'tty';const check:any=tty.isatty;
            console.log(typeof check,check(999));console.log(check.call(null,NaN));
            """,
            "function false\nfalse\n", "main.ts"
        },
        new object[]
        {
            """
            const perf=require('perf_hooks');const tty=require('tty');
            const value=perf.performance.now();const terminal=tty.isatty(999);console.log(value>=0,terminal);
            """,
            "true false\n", "main.cjs"
        },
        new object[]
        {
            """
            import {performance} from 'perf_hooks';import {isatty} from 'tty';
            async function run(){const start=performance.now();await new Promise<void>(resolve=>setTimeout(resolve,1));const end=performance.now();console.log(end>=start,isatty(999));}run();
            """,
            "true false\n", "main.ts"
        },
        new object[]
        {
            """
            import {performance} from 'perf_hooks';import {isatty} from 'tty';
            function* values():Generator<boolean,void,any>{yield performance.now()>=0;yield isatty(999);}
            const it=values();console.log(it.next().value,it.next().value);
            """,
            "true false\n", "main.ts"
        }
    ];

    [Theory]
    [MemberData(nameof(HostPrimitiveMetadataPrograms))]
    public void Isolated_HostPrimitiveMetadata_PreservesClockStateAndTerminalChecks(string source, string expected, string entryPoint)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile(entryPoint, source);
        var dllPath = tempDir.GetPath("host_primitive_metadata.dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --verify --standalone", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }

    public static IEnumerable<object[]> MessageChannelMetadataPrograms =>
    [
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                const channel:any=new MessageChannel();console.log(channel.port1!==null,channel.port2!==null,channel.port1!==channel.port2);
                """,
            },
            "true true true\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                const channel:any=new MessageChannel();channel.port1.postMessage('first');channel.port1.postMessage('second');channel.port2.on('message',(value:any)=>console.log(value));
                """,
            },
            "first\nsecond\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                const channel:any=new MessageChannel();const value:any={id:1,nested:[2]};channel.port1.postMessage(value);value.id=9;value.nested[0]=8;channel.port2.on('message',(copy:any)=>{console.log(copy.id,copy.nested[0],copy===value);channel.port1.close();channel.port2.close();});
                """,
            },
            "1 2 false\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                import {MessageChannel} from 'worker_threads';const channel:any=new MessageChannel();channel.port2.on('messageerror',()=>console.log('error'));channel.port2.on('message',(value:any)=>console.log(value));channel.port1.postMessage({nested:[()=>{}]});channel.port1.postMessage('after');
                """,
            },
            "error\nafter\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                import {MessageChannel,receiveMessageOnPort} from 'worker_threads';const channel:any=new MessageChannel();
                console.log(receiveMessageOnPort(channel.port2)===undefined,receiveMessageOnPort(null)===undefined,receiveMessageOnPort({})===undefined);
                channel.port1.postMessage({value:7});const item:any=receiveMessageOnPort(channel.port2);console.log(item.message.value,receiveMessageOnPort(channel.port2)===undefined);
                channel.port1.postMessage(()=>{});const error:any=receiveMessageOnPort(channel.port2);console.log(error.message===undefined);
                channel.port1.postMessage(8);channel.port2.close();console.log(receiveMessageOnPort(channel.port2)===undefined);channel.port1.close();
                """,
            },
            "true true true\n7 true\ntrue\ntrue\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                const channel:any=new MessageChannel();channel.port2.on('close',()=>console.log('closed'));channel.port2.on('message',(value:any)=>console.log('unexpected'));channel.port1.postMessage('queued');channel.port2.close();channel.port2.close();channel.port1.postMessage('late');channel.port1.close();console.log('done');
                """,
            },
            "closed\ndone\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                const channel:any=new MessageChannel();channel.port1.ref();channel.port1.ref();channel.port1.unref();channel.port1.unref();channel.port2.start();channel.port2.start();channel.port1.close();channel.port2.close();console.log('done');
                """,
            },
            "done\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                import {receiveMessageOnPort} from 'worker_threads';const channel:any=new MessageChannel();const value=new Uint8Array([1,2]);channel.port1.postMessage(value);value[0]=9;const result:any=receiveMessageOnPort(channel.port2);console.log(result.message[0],result.message[1]);channel.port1.close();channel.port2.close();
                """,
            },
            "1 2\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                async function run(){const channel:any=new MessageChannel();await new Promise<void>(resolve=>setTimeout(resolve,1));channel.port2.on('message',(value:any)=>{console.log(value);channel.port1.close();channel.port2.close();});channel.port1.postMessage('async');}run();
                """,
            },
            "async\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                function* values():Generator<any,void,any>{const channel:any=new MessageChannel();yield channel;channel.port1.postMessage('generator');}const iterator=values();const channel:any=iterator.next().value;channel.port2.on('message',(value:any)=>{console.log(value);channel.port1.close();channel.port2.close();});iterator.next();
                """,
            },
            "generator\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.cjs"] = """
                const workers=require('node:worker_threads');console.log(workers.isMainThread);const channel=new MessageChannel();channel.port2.on('message',value=>{console.log(value);channel.port1.close();channel.port2.close();});channel.port1.postMessage('common');
                """,
            },
            "true\ncommon\n", "main.cjs", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                import {Worker,MessageChannel} from 'worker_threads';const {port1,port2}=new MessageChannel();
                const worker=new Worker(__dirname+'/worker.ts',{workerData:{port:port1},transferList:[port1]});
                port2.on('message',(value:any)=>{console.log(value);port2.close();});port2.postMessage('ping');
                """,
                ["worker.ts"] = """
                import {workerData,receiveMessageOnPort} from 'worker_threads';const port:any=workerData.port;const timer=setInterval(()=>{const item:any=receiveMessageOnPort(port);if(item){port.postMessage('reply:'+item.message);clearInterval(timer);port.close();}},10);
                """,
            },
            "reply:ping\n", "main.ts", false
        }
    ];

    public static IEnumerable<object[]> WorkerMetadataPrograms =>
    [
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                import {isMainThread,threadId,workerData,parentPort} from 'worker_threads';console.log(isMainThread,threadId,workerData===null,parentPort===null);
                """,
            },
            "true 0 true true\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                import * as workers from 'node:worker_threads';console.log(workers.isMainThread,workers.threadId,workers.workerData===null,workers.parentPort===null,typeof workers.Worker);
                """,
            },
            "true 0 true true function\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                import {Worker,isMainThread,threadId} from 'worker_threads';console.log(isMainThread,threadId);const worker=new Worker(__dirname+'/worker.ts',{workerData:{value:7}});worker.on('message',(value:any)=>console.log(value));
                """,
                ["worker.ts"] = """
                import {isMainThread,threadId,workerData,parentPort} from 'worker_threads';parentPort!.postMessage('worker:'+isMainThread+':'+(threadId>0)+':'+workerData.value);
                """,
            },
            "true 0\nworker:false:true:7\n", "main.ts", false
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                import {getEnvironmentData,setEnvironmentData} from 'worker_threads';console.log(getEnvironmentData('missing')===undefined);const value:any={id:3,nested:[4]};console.log(setEnvironmentData('key',value)===undefined);value.id=9;value.nested[0]=8;const result:any=getEnvironmentData('key');console.log(result.id,result.nested[0]);setEnvironmentData('key',null);console.log(getEnvironmentData('key')===undefined);
                """,
            },
            "true\ntrue\n3 4\ntrue\n", "main.ts", false
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                import {Worker,setEnvironmentData} from 'worker_threads';setEnvironmentData('worker-phase',17);const worker=new Worker(__dirname+'/worker.ts');worker.on('message',(value:any)=>console.log(value));
                """,
                ["worker.ts"] = """
                import {parentPort,getEnvironmentData} from 'worker_threads';parentPort!.postMessage(getEnvironmentData('worker-phase'));
                """,
            },
            "17\n", "main.ts", false
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                import {Worker,markAsUntransferable} from 'worker_threads';const buffer=new ArrayBuffer(8);console.log(markAsUntransferable(buffer)===undefined);const worker=new Worker(__dirname+'/worker.ts',{workerData:'go',transferList:[buffer]});console.log(buffer.byteLength);worker.on('message',(value:any)=>console.log(value));
                """,
                ["worker.ts"] = """
                postMessage('ok');
                """,
            },
            "true\n8\nok\n", "main.ts", false
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                import {Worker} from 'worker_threads';try{new Worker('missing.ts');}catch(error:any){console.log(error.message);}
                """,
            },
            "Worker requires the SharpTS runtime (SharpTS.dll) to be present. Compile without --standalone so it is co-located with the output.\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                import {MessageChannel,receiveMessageOnPort} from 'worker_threads';const channel:any=new MessageChannel();
                console.log(receiveMessageOnPort(channel.port2)===undefined,receiveMessageOnPort(null)===undefined,receiveMessageOnPort({})===undefined);
                channel.port1.postMessage({value:7});const item:any=receiveMessageOnPort(channel.port2);console.log(item.message.value,receiveMessageOnPort(channel.port2)===undefined);
                channel.port1.postMessage(()=>{});const error:any=receiveMessageOnPort(channel.port2);console.log(error.message===undefined);
                channel.port1.postMessage(8);channel.port2.close();console.log(receiveMessageOnPort(channel.port2)===undefined);channel.port1.close();
                """,
            },
            "true true true\n7 true\ntrue\ntrue\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                import {Worker,MessageChannel} from 'worker_threads';const {port1,port2}=new MessageChannel();
                const worker=new Worker(__dirname+'/worker.ts',{workerData:{port:port1},transferList:[port1]});
                port2.on('message',(value:any)=>{console.log(value);port2.close();});port2.postMessage('ping');
                """,
                ["worker.ts"] = """
                import {workerData,receiveMessageOnPort} from 'worker_threads';const port:any=workerData.port;const timer=setInterval(()=>{const item:any=receiveMessageOnPort(port);if(item){port.postMessage('reply:'+item.message);clearInterval(timer);port.close();}},10);
                """,
            },
            "reply:ping\n", "main.ts", false
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                import {isMainThread,threadId,receiveMessageOnPort} from 'worker_threads';async function run(){await new Promise<void>(resolve=>setTimeout(resolve,1));console.log(isMainThread,threadId,receiveMessageOnPort(null)===undefined);}run();
                """,
            },
            "true 0 true\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                import {isMainThread,threadId,receiveMessageOnPort} from 'worker_threads';function* values():Generator<any,void,any>{yield isMainThread;yield threadId;yield receiveMessageOnPort(null)===undefined;}for(const value of values())console.log(value);
                """,
            },
            "true\n0\ntrue\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.cjs"] = """
                const workers=require('node:worker_threads');console.log(workers.isMainThread,workers.threadId,workers.workerData===null,workers.parentPort===null,typeof workers.Worker);
                """,
            },
            "true 0 true true function\n", "main.cjs", true
        }
    ];

    [Theory]
    [MemberData(nameof(WorkerMetadataPrograms))]
    public void Isolated_WorkerMetadata_PreservesContextEnvironmentAndBridgeDeployment(Dictionary<string, string> files, string expected, string entryPoint, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        foreach (var (path, source) in files) tempDir.CreateFile(path, source);
        var dllPath = tempDir.GetPath("worker_metadata.dll");
        var deployment = standalone ? " --standalone" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --verify{deployment}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    [Theory]
    [MemberData(nameof(MessageChannelMetadataPrograms))]
    public void Isolated_MessageChannelMetadata_PreservesCloningQueueDeliveryAndWorkerTransfers(Dictionary<string, string> files, string expected, string entryPoint, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        foreach (var (path, source) in files) tempDir.CreateFile(path, source);
        var dllPath = tempDir.GetPath("message_channel_metadata.dll");
        var deployment = standalone ? " --standalone" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --verify{deployment}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> ClusterMetadataPrograms =>
    [
        new object[]
        {
            """
            import {isPrimary,isWorker,isMaster,SCHED_NONE,SCHED_RR} from 'cluster';console.log(isPrimary,isWorker,isMaster,SCHED_NONE,SCHED_RR);
            """,
            "true false true 1 2\n", "main.ts", true
        },
        new object[]
        {
            """
            import * as cluster from 'node:cluster';
            cluster.setupMaster({exec:'worker.ts',args:['one']});console.log(cluster.settings.exec,cluster.settings.args[0]);
            console.log(cluster.schedulingPolicy=cluster.SCHED_NONE);console.log(cluster.schedulingPolicy);
            cluster.schedulingPolicy=cluster.SCHED_RR;console.log(cluster.schedulingPolicy,Object.keys(cluster.workers).length,cluster.worker===undefined);
            """,
            "worker.ts one\n1\n1\n2 0 false\n", "main.ts", false
        },
        new object[]
        {
            """
            import * as cluster from 'cluster';
            function listener(value:any){console.log('event',value);}
            cluster.on('sample',listener);cluster.once('sample',(value:any)=>console.log('once',value));
            console.log(cluster.listenerCount('sample'));console.log(cluster.emit('sample',3));
            console.log(cluster.listenerCount('sample'));cluster.off('sample',listener);console.log(cluster.emit('sample',4));
            cluster.removeAllListeners();console.log(cluster.eventNames().length);
            """,
            "2\nevent 3\nonce 3\ntrue\n1\nfalse\n0\n", "main.ts", false
        },
        new object[]
        {
            """
            import {setupPrimary as setup,settings as state} from 'cluster';setup({exec:'alias.ts'});console.log(state.exec);
            """,
            "alias.ts\n", "main.ts", false
        },
        new object[]
        {
            """
            import {fork} from 'cluster';try{fork();}catch(error){console.log(error.message);}
            """,
            "cluster requires the SharpTS runtime (SharpTS.dll) to be present. Compile without --standalone so it is co-located with the output.\n", "main.ts", true
        },
        new object[]
        {
            """
            import * as cluster from 'cluster';
            if(cluster.isPrimary){const worker=cluster.fork();worker.on('message',(message:any)=>console.log('worker',message));worker.on('exit',(code:any)=>console.log('exit',code));}
            else{process.send(cluster.isWorker);}
            """,
            "worker true\nexit 0\n", "main.ts", false
        },
        new object[]
        {
            """
            import {listenerCount} from 'cluster';async function run(){await new Promise<void>(resolve=>setTimeout(resolve,1));console.log(listenerCount('missing'));}run();
            """,
            "0\n", "main.ts", false
        },
        new object[]
        {
            """
            import {listenerCount} from 'cluster';function* values():Generator<number,void,any>{yield listenerCount('missing');yield listenerCount('missing');}const iterator=values();console.log(iterator.next().value,iterator.next().value);
            """,
            "0 0\n", "main.ts", false
        },
        new object[]
        {
            """
            const cluster=require('node:cluster');console.log(cluster.isPrimary,cluster.isWorker,cluster.setupPrimary===null,cluster.fork===null);
            """,
            "true false true true\n", "main.cjs", false
        }
    ];

    [Theory]
    [MemberData(nameof(ClusterMetadataPrograms))]
    public void Isolated_ClusterMetadata_PreservesBridgeStateWorkerLifecycleAndDependencies(string source, string expected, string entryPoint, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile(entryPoint, source);
        var dllPath = tempDir.GetPath("cluster_metadata.dll");
        var deployment = standalone ? " --standalone" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --verify{deployment}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> AtomicsMetadataPrograms =>
    [
        new object[]
        {
            """
            const a=new Int32Array(new SharedArrayBuffer(16));a[0]=10;
            console.log(Atomics.add(a,0,3),Atomics.load(a,0));Atomics.add(a,0,1);
            console.log(Atomics.sub(a,0,4),Atomics.and(a,0,7),Atomics.or(a,0,8),Atomics.xor(a,0,3));
            console.log(Atomics.exchange(a,0,20),Atomics.compareExchange(a,0,20,30),Atomics.compareExchange(a,0,5,44),Atomics.load(a,0));
            const b=new Uint32Array(new SharedArrayBuffer(4));console.log(Atomics.add(b,0,-1),Atomics.load(b,0));Atomics.add(b,0,1);console.log(Atomics.load(b,0));
            """,
            "10 13\n14 10 2 10\n9 20 30 30\n0 4294967295\n0\n", "main.ts"
        },
        new object[]
        {
            """
            const a=new Int32Array(new SharedArrayBuffer(4));a[0]=1;
            console.log(Atomics.add(a,0,5000000000),Atomics.load(a,0));
            console.log(Atomics.exchange(a,0,NaN),Atomics.load(a,0),Atomics.compareExchange(a,0,Infinity,7),Atomics.load(a,0));
            console.log(Atomics.store(a,0,4294967296),Atomics.load(a,0));
            """,
            "1 705032705\n705032705 0 0 7\n0 0\n", "main.ts"
        },
        new object[]
        {
            """
            const view:any=new Int32Array(new SharedArrayBuffer(4));view[0]=5;
            console.log(Atomics.load(view,0),Atomics.store(view,0,9),Atomics.add(view,0,3),Atomics.sub(view,0,1),Atomics.and(view,0,7),Atomics.or(view,0,8),Atomics.xor(view,0,3),Atomics.exchange(view,0,20),Atomics.compareExchange(view,0,20,30),Atomics.load(view,0));
            """,
            "5 9 9 12 11 3 11 8 20 30\n", "main.ts"
        },
        new object[]
        {
            """
            function check(view:any){view[0]=5;console.log(Atomics.load(view,0),Atomics.store(view,0,9),Atomics.add(view,0,3),Atomics.sub(view,0,1),Atomics.and(view,0,7),Atomics.or(view,0,8),Atomics.xor(view,0,3),Atomics.exchange(view,0,20),Atomics.compareExchange(view,0,20,30),Atomics.load(view,0));}
            check(new Int8Array(new SharedArrayBuffer(4)));check(new Uint8Array(new SharedArrayBuffer(4)));check(new Int16Array(new SharedArrayBuffer(4)));check(new Uint16Array(new SharedArrayBuffer(4)));
            """,
            "5 9 9 12 11 3 11 8 20 30\n5 9 9 12 11 3 11 8 20 30\n5 9 9 12 11 3 11 8 20 30\n5 9 9 12 11 3 11 8 20 30\n", "main.ts"
        },
        new object[]
        {
            """
            const shared=new SharedArrayBuffer(16);const view=new Int32Array(shared,4,1);const neighbour=new Int32Array(shared,0,1);neighbour[0]=77;view[0]=5;
            console.log(Atomics.add(view,0,3),Atomics.load(view,0),neighbour[0]);
            try{Atomics.add(view,1,1);}catch(error){console.log(error instanceof RangeError);}
            try{console.log(Atomics.add(view,-1,1));}catch(error){console.log(error instanceof RangeError);}
            console.log(neighbour[0]);
            """,
            "5 8 77\ntrue\ntrue\n77\n", "main.ts"
        },
        new object[]
        {
            """
            console.log(Atomics.pause()===undefined,Atomics.pause(42)===undefined);
            const pause:any=Atomics.pause;console.log(pause.name,pause.length,pause(-1)===undefined);
            for(const value of [true,1.5,'2',null,NaN,Infinity]){try{Atomics.pause(value as any);}catch(error){console.log(error instanceof TypeError);}}
            """,
            "true true\npause 0 true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n", "main.ts"
        },
        new object[]
        {
            """
            const view=new Int32Array(new SharedArrayBuffer(4));view[0]=1;
            console.log(Atomics.wait(view,0,2,0),Atomics.wait(view,0,1,0),Atomics.notify(view,0,1));
            console.log(Atomics.isLockFree(1),Atomics.isLockFree(2),Atomics.isLockFree(4),Atomics.isLockFree(8),Atomics.isLockFree(3));
            """,
            "not-equal ok 0\ntrue true true true false\n", "main.ts"
        },
        new object[]
        {
            """
            const view=new Int32Array(new SharedArrayBuffer(4));
            async function run(){Atomics.add(view,0,1);await new Promise<void>(resolve=>setTimeout(resolve,1));console.log(Atomics.add(view,0,2),Atomics.load(view,0));}run();
            """,
            "1 3\n", "main.ts"
        },
        new object[]
        {
            """
            const view=new Int32Array(new SharedArrayBuffer(4));
            function* values():Generator<number,void,any>{yield Atomics.add(view,0,2);yield Atomics.load(view,0);}
            const iterator=values();console.log(iterator.next().value,iterator.next().value);
            """,
            "0 2\n", "main.ts"
        },
        new object[]
        {
            """
            const view=new Int32Array(new SharedArrayBuffer(4));view[0]=3;console.log(Atomics.add(view,0,2),Atomics.load(view,0));
            """,
            "3 5\n", "main.cjs"
        }
    ];

    [Theory]
    [MemberData(nameof(AtomicsMetadataPrograms))]
    public void Isolated_AtomicsMetadata_PreservesOptimizedLockedAndSuspendedOperations(string source, string expected, string entryPoint)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile(entryPoint, source);
        var dllPath = tempDir.GetPath("atomics_metadata.dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --verify --standalone", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> NodeErrorMetadataPrograms =>
    [
        new object[]
        {
            """
            import * as fs from 'fs';
            try{fs.readFileSync('missing-node-error.txt','utf8');}catch(e){console.log(e.code,e.syscall,e.path.endsWith('missing-node-error.txt'),typeof e.message);}
            """,
            "ENOENT open true string\n", "main.ts"
        },
        new object[]
        {
            """
            import * as fs from 'node:fs';
            try{fs.readdirSync('missing-node-error-directory');}catch(e){console.log(e.code,e.syscall,e.path.endsWith('missing-node-error-directory'));}
            """,
            "ENOENT readdir true\n", "main.ts"
        },
        new object[]
        {
            """
            import * as fs from 'fs';
            try{fs.fstatSync(99999);}catch(e){console.log(e.code,e.syscall,e.message.includes('bad file descriptor'));}
            try{fs.readSync(99999,Buffer.alloc(1),0,1,null);}catch(e){console.log(e.code,e.syscall);}
            """,
            "EBADF fstat true\nEBADF fstat\n", "main.ts"
        },
        new object[]
        {
            """
            import * as fs from 'fs';
            try{fs.closeSync(99999);}catch(e){console.log(e.code,e.syscall,e.message.includes('bad file descriptor'));}
            """,
            "EBADF close true\n", "main.ts"
        },
        new object[]
        {
            """
            import * as fs from 'fs';
            const path='node-error-data.txt';fs.writeFileSync(path,'ok');
            try{const fd=fs.openSync(path,'r');console.log(fs.fstatSync(fd).size);fs.closeSync(fd);try{fs.closeSync(fd);}catch(e){console.log(e.code,e.syscall);}console.log(fs.readFileSync(path,'utf8'));}finally{fs.unlinkSync(path);}
            """,
            "2\nEBADF close\nok\n", "main.ts"
        },
        new object[]
        {
            """
            import {readFile} from 'fs/promises';
            async function run(){try{await readFile('missing-node-error-async.txt','utf8');}catch(e){console.log(e.code,e.syscall,e.path.endsWith('missing-node-error-async.txt'));}}run();
            """,
            "ENOENT open true\n", "main.ts"
        },
        new object[]
        {
            """
            const fs=require('fs');
            try{fs.closeSync(99999);}catch(e){console.log(e.code,e.syscall);}
            """,
            "EBADF close\n", "main.cjs"
        },
        new object[]
        {
            """
            import * as fs from 'fs';
            function* failures():Generator<string,void,any>{try{fs.closeSync(99999);}catch(e){yield e.code;yield e.syscall;}}
            const iterator=failures();console.log(iterator.next().value,iterator.next().value);
            """,
            "EBADF close\n", "main.ts"
        },
        new object[]
        {
            """
            import {statSync as stat,closeSync as close} from 'node:fs';
            try{stat('missing-node-error-alias.txt');}catch(e){console.log(e.code,e.syscall);}
            try{close(99999);}catch(e){console.log(e.code,e.syscall);}
            """,
            "ENOENT stat\nEBADF close\n", "main.ts"
        }
    ];

    [Theory]
    [MemberData(nameof(NodeErrorMetadataPrograms))]
    public void Isolated_NodeErrorMetadata_PreservesSharedErrorsAndDescriptorLifetime(string source, string expected, string entryPoint)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile(entryPoint, source);
        var dllPath = tempDir.GetPath("node_error_metadata.dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --verify --standalone", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> IntlMetadataPrograms =>
    [
        new object[]
        {
            """
            const number=new Intl.NumberFormat('en-US',{useGrouping:false,minimumFractionDigits:2,maximumFractionDigits:2});console.log(number.format(1234.5));
            const date=new Intl.DateTimeFormat('en-US',{timeZone:'UTC'});console.log(date.resolvedOptions().timeZone);
            const collator=new Intl.Collator('en');console.log(collator.compare('a','b')<0);
            const plural=new Intl.PluralRules('en');console.log(plural.select(1),plural.select(2));
            const relative=new Intl.RelativeTimeFormat('en',{numeric:'auto'});console.log(relative.format(-1,'day'));
            const list=new Intl.ListFormat('en',{type:'conjunction'});console.log(list.format(['a','b']));
            const names=new Intl.DisplayNames('en-US',{type:'script'});console.log(names.of('Latn'));
            const segments=new Intl.Segmenter('en',{granularity:'grapheme'});console.log([...segments.segment('AB')].length);
            """,
            "1234.50\nUTC\ntrue\none other\nyesterday\na and b\nLatin\n2\n", "main.ts", false
        },
        new object[]
        {
            """
            const I:any=Intl;const J:any=Intl;console.log(typeof I,I===J);
            console.log(Object.keys(I).join(','));console.log(typeof I.NumberFormat,I.NumberFormat.length,I.NumberFormat===J.NumberFormat);
            const value=new I.NumberFormat('en-US',{useGrouping:false});console.log(value.format(1234.5));
            """,
            "object true\nNumberFormat,DateTimeFormat,Collator,PluralRules,RelativeTimeFormat,ListFormat,Segmenter,DisplayNames\nfunction 2 true\n1234.5\n", "main.ts", false
        },
        new object[]
        {
            """
            const I:any=Intl;const constructor=I.NumberFormat;
            const value:any=Reflect.construct(constructor,['en-US',{minimumFractionDigits:2,maximumFractionDigits:2,useGrouping:false}]);
            console.log(value.format(1234.5));
            """,
            "1,234.5\n", "main.ts", false
        },
        new object[]
        {
            """
            const I:any=Intl;console.log(typeof I,I===Intl,typeof I.NumberFormat,Object.keys(I).length);
            """,
            "object true function 8\n", "main.ts", true
        },
        new object[]
        {
            """
            try{new Intl.NumberFormat('en-US');}catch(e:any){console.log(String(e).includes('CreateIntlNumberFormat requires the SharpTS runtime'));}
            """,
            "true\n", "main.ts", true
        },
        new object[]
        {
            """
            async function run(){await Promise.resolve(1);console.log(new Intl.PluralRules('en').select(1));}run();
            """,
            "one\n", "main.ts", false
        },
        new object[]
        {
            """
            function* values():Generator<string,void,any>{const I:any=Intl;yield new I.NumberFormat('en-US',{useGrouping:false}).format(1234.5);}console.log(values().next().value);
            """,
            "1234.5\n", "main.ts", false
        },
        new object[]
        {
            """
            const I=Intl;const value=new I.NumberFormat('en-US',{useGrouping:false});console.log(value.format(1234.5));
            """,
            "1234.5\n", "main.cjs", false
        }
    ];

    [Theory]
    [MemberData(nameof(IntlMetadataPrograms))]
    public void Isolated_IntlMetadata_PreservesFactoriesNamespaceAndDeployment(string source, string expected, string entryPoint, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile(entryPoint, source);
        var dllPath = tempDir.GetPath("intl_metadata.dll");
        var deployment = standalone ? " --standalone" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --verify{deployment}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        // Intl constructors use the late-bound runtime; namespace reads alone work without it.
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> AsyncLocalStorageMetadataPrograms =>
    [
        new object[]
        {
            """
            import {AsyncLocalStorage as Storage} from 'node:async_hooks';
            const als=new Storage();console.log(als.getStore()==undefined);als.enterWith('base');
            const result=als.run('outer',()=>{console.log(als.getStore());console.log(als.run('inner',()=>als.getStore()));
            console.log(als.getStore());return als.exit(()=>{console.log(als.getStore()==undefined);return 7;});});
            console.log(result,als.getStore());
            """,
            "true\nouter\ninner\nouter\ntrue\n7 base\n", "main.ts"
        },
        new object[]
        {
            """
            import {AsyncLocalStorage} from 'async_hooks';const als=new AsyncLocalStorage();als.enterWith('base');
            try{als.run('throwing',()=>{console.log(als.getStore());throw new Error('run');});}catch(e:any){console.log('caught-run');}
            console.log(als.getStore());
            try{als.exit(()=>{console.log(als.getStore()==undefined);throw new Error('exit');});}catch(e:any){console.log('caught-exit');}
            console.log(als.getStore());
            """,
            "throwing\ncaught-run\nbase\ntrue\ncaught-exit\nbase\n", "main.ts"
        },
        new object[]
        {
            """
            import {AsyncLocalStorage} from 'async_hooks';const als=new AsyncLocalStorage();als.enterWith('before');
            als.disable();console.log(als.getStore()==undefined);als.enterWith('after');console.log(als.getStore()==undefined);
            console.log(als.run('ignored',()=>als.getStore()==undefined));console.log(als.getStore()==undefined);
            """,
            "true\ntrue\ntrue\ntrue\n", "main.ts"
        },
        new object[]
        {
            """
            import * as hooks from 'async_hooks';const a=new hooks.AsyncLocalStorage();const b=new hooks.AsyncLocalStorage();
            a.run({id:1},()=>{b.run({id:2},()=>{console.log(a.getStore().id,b.getStore().id);});console.log(b.getStore()==undefined);});
            console.log(a.getStore()==undefined,b.getStore()==undefined);
            """,
            "1 2\ntrue\ntrue true\n", "main.ts"
        },
        new object[]
        {
            """
            import {AsyncLocalStorage} from 'async_hooks';const als=new AsyncLocalStorage();
            async function read(){await new Promise<void>(resolve=>setTimeout(resolve,10));return als.getStore();}
            const left=als.run('left',read);const right=als.run('right',read);
            Promise.all([left,right]).then(values=>console.log(values[0],values[1],als.getStore()==undefined));
            """,
            "left right true\n", "main.ts"
        },
        new object[]
        {
            """
            import {AsyncLocalStorage} from 'async_hooks';const als=new AsyncLocalStorage();
            als.run('chain',()=>Promise.resolve(1).then(()=>{console.log(als.getStore());return Promise.resolve(2);}).then(()=>console.log(als.getStore())));
            """,
            "chain\nchain\n", "main.ts"
        },
        new object[]
        {
            """
            import {AsyncLocalStorage} from 'async_hooks';const als=new AsyncLocalStorage();als.enterWith('base');
            function* items():Generator<any,void,any>{yield als.getStore();yield als.getStore();}
            const it=als.run('creation',()=>items());console.log(als.run('iteration',()=>it.next().value));console.log(it.next().value,als.getStore());
            """,
            "iteration\nbase base\n", "main.ts"
        },
        new object[]
        {
            """
            const hooks=require('node:async_hooks');const als=new hooks.AsyncLocalStorage();als.enterWith('required');const value=als.getStore();console.log(value);als.disable();const empty=als.getStore();console.log(empty==undefined);
            """,
            "required\ntrue\n", "main.cjs"
        }
    ];

    [Theory]
    [MemberData(nameof(AsyncLocalStorageMetadataPrograms))]
    public void Isolated_AsyncLocalStorageMetadata_PreservesContextRestorationAndAsyncFlow(string source, string expected, string entryPoint)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile(entryPoint, source);
        var dllPath = tempDir.GetPath("async_local_storage_metadata.dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --verify --standalone", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> AbortMetadataPrograms =>
    [
        new object[]
        {
            """
            const c=new AbortController();
            console.log(c.signal===c.signal,c.signal.aborted,c.signal.reason===undefined);
            c.abort('stop'); c.abort('ignored'); console.log(c.signal.aborted,c.signal.reason);
            const d=new AbortController();d.abort(); console.log(String(d.signal.reason).includes('AbortError'));
            """,
            "true false false\ntrue stop\ntrue\n", "main.ts", true
        },
        new object[]
        {
            """
            const c=new AbortController(); const s:any=c.signal;
            const log:string[]=[];const removed=()=>log.push('removed');const onabort=()=>log.push('onabort');
            s.addEventListener('abort',removed);s.removeEventListener('abort',removed);
            s.addEventListener('abort',()=>log.push('listener'));s.onabort=onabort; console.log(s.onabort===onabort);
            try{s.throwIfAborted();console.log('ready');}catch(e){console.log('unexpected');}
            c.abort('stop');c.abort('ignored');console.log(log.join(','));
            try{s.throwIfAborted();}catch(e){console.log('threw');}
            console.log(s.aborted,s.reason);
            """,
            "true\nready\nlistener,onabort\nthrew\ntrue stop\n", "main.ts", true
        },
        new object[]
        {
            """
            const c=new AbortController();const s:any=c.signal;let count=0;
            const add=s.addEventListener;const remove=s.removeEventListener;const check=s.throwIfAborted;
            const callback=()=>count++; add.call(s,'abort',callback); remove.call(s,'abort',callback);
            add.call(s,'abort',callback);c.abort(); console.log(count);
            try{check.call(s);}catch(e){console.log('threw');}
            """,
            "1\nthrew\n", "main.ts", true
        },
        new object[]
        {
            """
            const A=AbortSignal; console.log(typeof A,A===AbortSignal);
            const s:any=A.abort('now');console.log(s.aborted,s.reason);
            console.log(s instanceof (AbortSignal as any),({} as any) instanceof (A as any));
            console.log(new AbortController().signal instanceof (A as any));
            """,
            "object true\ntrue now\ntrue false\ntrue\n", "main.ts", true
        },
        new object[]
        {
            """
            async function run() {
                const s=AbortSignal.timeout(1);
                // CLR cancellation and guest timers use separate schedulers.
                while (!s.aborted) {
                    await new Promise<void>(resolve=>setTimeout(resolve,10));
                }
                console.log(s.aborted);
                console.log(String(s.reason).includes('TimeoutError'));
            }
            run();
            """,
            "true\ntrue\n", "main.ts", true
        },
        new object[]
        {
            """
            const one=new AbortController();const two=new AbortController();
            const s=AbortSignal.any([one.signal,two.signal]);console.log(s.aborted);one.abort('first');console.log(s.aborted,s.reason);
            console.log(AbortSignal.any([AbortSignal.abort('already')]).reason);
            """,
            "false\ntrue first\nalready\n", "main.ts", false
        },
        new object[]
        {
            """
            try{AbortSignal.any([]);}catch(e:any){console.log(e.message);}
            """,
            "AbortSignalAnyCompiled requires the SharpTS runtime (SharpTS.dll). Recompile without --standalone or deploy SharpTS.dll next to the output.\n", "main.ts", true
        },
        new object[]
        {
            """
            import {Readable,addAbortSignal} from 'stream';
            const r=new Readable({read(){}});const c=new AbortController();const log:string[]=[];
            r.on('error',(e:any)=>log.push('error:'+e.name));r.on('close',()=>log.push('close'));
            console.log(addAbortSignal(c.signal,r)===r);c.abort();console.log(log.join(','),r.destroyed);
            """,
            "true\nerror:AbortError,close true\n", "main.ts", true
        },
        new object[]
        {
            """
            const c=new AbortController();c.abort('stop');
            const source=new ReadableStream({start(controller){controller.close();}});const sink=new WritableStream({});
            source.pipeTo(sink,{signal:c.signal}).then(()=>console.log('done'),()=>console.log('rejected'));
            """,
            "rejected\n", "main.ts", true
        },
        new object[]
        {
            """
            async function run(){await Promise.resolve(0);const c=new AbortController();c.abort('async');console.log(c.signal.reason);}run();
            function* items():Generator<any,void,any>{yield AbortSignal.abort('generator').reason;}console.log(items().next().value);
            """,
            "async\ngenerator\n", "main.ts", true
        },
        new object[]
        {
            """
            import * as http from 'http'; console.log(typeof http.createServer);
            """,
            "function\n", "main.ts", true
        }
    ];

    [Theory]
    [MemberData(nameof(AbortMetadataPrograms))]
    public void Isolated_AbortMetadata_PreservesSignalStateEventsAndDependencies(string source, string expected, string entryPoint, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile(entryPoint, source);
        var dllPath = tempDir.GetPath("abort_metadata.dll");
        var deployment = standalone ? " --standalone" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --verify{deployment}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        // Only AbortSignal.any needs the late-bound SharpTS runtime; ordinary controllers remain standalone.
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> SourceExecutionMetadataPrograms =>
    [
        new object[]
        {
            """
            import {runSourceJson} from 'sharpts:execution';
            const r=JSON.parse(runSourceJson("console.log('nested');",'interpret',1024));
            console.log(r.Success,r.Output.trim(),r.Errors.length);
            """,
            "true nested 0\n", "main.ts", false
        },
        new object[]
        {
            """
            import {runSourceJson} from 'sharpts:execution';
            const r=JSON.parse(runSourceJson('console.log(40+2);','compile',1024));
            console.log(r.Success,r.Output.trim(),r.Errors.length,r.CompileTimeMs!==null);
            """,
            "true 42 0 true\n", "main.ts", false
        },
        new object[]
        {
            """
            import * as execution from 'sharpts:execution';
            const run=execution.runSourceJson; const configure=execution.configureUntrustedProcess;
            console.log(typeof run,typeof configure);
            console.log(JSON.parse(run("console.log('alias');",'interpret',1024)).Output.trim());
            """,
            "function function\nalias\n", "main.ts", false
        },
        new object[]
        {
            """
            const execution=require('sharpts:execution');
            const r=JSON.parse(execution.runSourceJson("console.log('required');",'interpret',1024));
            console.log(r.Success,r.Output.trim());
            """,
            "true required\n", "main.cjs", false
        },
        new object[]
        {
            """
            import {runSourceJson,configureUntrustedProcess} from 'sharpts:execution';
            const r=JSON.parse(runSourceJson("const x: number = 'wrong';",'interpret',1024)); console.log(r.Success,r.Errors.length>0);
            try {runSourceJson('','interpret',1024);} catch(e:any) {console.log('empty');}
            try {runSourceJson('1;','invalid',1024);} catch(e:any) {console.log('mode');}
            try {runSourceJson('1;','interpret',0);} catch(e:any) {console.log('limit');}
            try {configureUntrustedProcess('');} catch(e:any) {console.log('proxy');}
            """,
            "false true\nempty\nmode\nlimit\nproxy\n", "main.ts", false
        },
        new object[]
        {
            """
            import {configureUntrustedProcess,runSourceJson} from 'sharpts:execution';
            configureUntrustedProcess('http://127.0.0.1:9');
            console.log('configured');
            console.log(JSON.parse(runSourceJson("console.log('isolated');",'interpret',1024)).Output.trim());
            """,
            "configured\nisolated\n", "main.ts", false
        },
        new object[]
        {
            """
            import {runSourceJson} from 'sharpts:execution';
            async function run() {await Promise.resolve(0);console.log(JSON.parse(runSourceJson('console.log(14);','interpret',1024)).Output.trim());} run();
            function* items():Generator<any,void,any> {yield JSON.parse(runSourceJson('console.log(28);','compile',1024)).Output.trim();}
            console.log(items().next().value);
            """,
            "14\n28\n", "main.ts", false
        },
        new object[]
        {
            """
            import {runSourceJson,configureUntrustedProcess} from 'sharpts:execution';
            try {runSourceJson('1;','interpret',1024);} catch(e:any) {console.log(e.message);}
            try {configureUntrustedProcess('http://127.0.0.1:9');} catch(e:any) {console.log(e.message);}
            """,
            "RunJson requires the SharpTS runtime (SharpTS.dll). Recompile without --standalone or deploy SharpTS.dll next to the output.\nConfigureUntrustedProcess requires the SharpTS runtime (SharpTS.dll). Recompile without --standalone or deploy SharpTS.dll next to the output.\n", "main.ts", true
        }
    ];

    [Theory]
    [MemberData(nameof(SourceExecutionMetadataPrograms))]
    public void Isolated_SourceExecutionMetadata_PreservesInterpreterBridgeAndDeployment(string source, string expected, string entryPoint, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile(entryPoint, source);
        var dllPath = tempDir.GetPath("source_execution_metadata.dll");
        var deployment = standalone ? " --standalone" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --verify{deployment}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        // The bridge is late-bound; normal output needs the complete managed compiler closure.
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!standalone)
        {
            foreach (var dependency in new[] { "SharpTS.deps.json", "SharpTS.runtimeconfig.json", "NuGet.Protocol.dll" })
                Assert.True(File.Exists(tempDir.GetPath(dependency)), dependency);
        }
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> VmMetadataPrograms =>
    [
        new object[]
        {
            """
            import * as vm from 'node:vm';
            console.log(vm.runInNewContext('1+2'));
            const ctx = vm.createContext({x: 1});
            console.log(vm.isContext(ctx), vm.isContext({}));
            vm.runInContext('x=42', ctx); console.log(ctx.x);
            try {console.log(vm.runInThisContext('6*7'));} catch(e:any) {console.log('current-context-error');}
            console.log(typeof vm.constants === 'object');
            console.log(vm.constants.USE_MAIN_CONTEXT_DEFAULT_LOADER === vm.constants.USE_MAIN_CONTEXT_DEFAULT_LOADER);
            """,
            "3\ntrue false\n42\ncurrent-context-error\ntrue\ntrue\n", "main.ts", false
        },
        new object[]
        {
            """
            import { Script, createContext } from 'vm';
            const s = new Script('x+y');
            console.log(s.runInNewContext({x:1,y:2}));
            console.log(s.runInContext(createContext({x:10,y:20})));
            console.log(new Script('40+2').runInThisContext());
            """,
            "3\n30\n42\n", "main.ts", false
        },
        new object[]
        {
            """
            import { compileFunction, createContext } from 'vm';
            const add = compileFunction('return a + b;', ['a','b']);
            console.log(add(2,3), add(10,20));
            const context = createContext({x:7});
            const read = compileFunction('return x;', [], {parsingContext:context}); console.log(read());
            """,
            "5 30\n7\n", "main.ts", false
        },
        new object[]
        {
            """
            import { SourceTextModule, SyntheticModule } from 'vm';
            async function main() {
            const syn = new SyntheticModule(['x','y'], function(this:any) {this.setExport('x',42);this.setExport('y','hello');});
            const m = new SourceTextModule('import {x,y} from "syn"; export const r=x+1; export const s=y+"!";');
            console.log(m.status); await m.link((spec:string)=>syn); console.log(m.status); await m.evaluate();
            console.log(m.namespace.r, m.namespace.s, syn.namespace.x, syn.status);
            } main();
            """,
            "unlinked\nlinked\n43 hello! 42 evaluated\n", "main.ts", false
        },
        new object[]
        {
            """
            import { measureMemory } from 'vm';
            async function run() {const result:any = await measureMemory();
            console.log(typeof result.total.jsMemoryEstimate === 'number');
            console.log(result.total.jsMemoryRange !== undefined && result.total.jsMemoryRange !== null);
            } run();
            """,
            "true\ntrue\n", "main.ts", false
        },
        new object[]
        {
            """
            const vm = require('node:vm');
            console.log(typeof vm, typeof vm.runInNewContext, typeof vm.Script, typeof vm.constants);
            try {console.log(vm.runInNewContext('4+5'));} catch(e) {console.log('cjs-call-error');}
            """,
            "object object object object\ncjs-call-error\n", "main.cjs", false
        },
        new object[]
        {
            """
            import { runInNewContext } from 'vm';
            async function run() {await Promise.resolve(0); console.log(runInNewContext('20+1'));} run();
            function* items():Generator<any,void,any> {yield runInNewContext('6*7');}
            console.log(items().next().value);
            """,
            "21\n42\n", "main.ts", false
        },
        new object[]
        {
            """
            import { runInNewContext } from 'vm';
            try {runInNewContext('1+1');} catch(e:any) {console.log(e.message);}
            """,
            "vm module is not supported in standalone compiled output (SharpTS runtime not present).\n", "main.ts", true
        }
    ];

    [Theory]
    [MemberData(nameof(VmMetadataPrograms))]
    public void Isolated_VmMetadata_PreservesInterpreterBridgeAndDeployment(string source, string expected, string entryPoint, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile(entryPoint, source);
        var dllPath = tempDir.GetPath("vm_metadata.dll");
        var deployment = standalone ? " --standalone" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --verify{deployment}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        // VM is late-bound: normal output co-locates the interpreter; standalone output reports its absence.
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> ModuleLoadingMetadataPrograms =>
    [
        new object[]
        {
            new Dictionary<string, string>
            {
                ["lib.ts"] = """
                    export let value = 3; export function bump(): void { value++; }
                    """,
                ["main.ts"] = """
                    import { value, bump } from './lib'; console.log(value); bump(); console.log(value);
                    """,
            },
            "main.ts",
            "3\n3\n"
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.cjs"] = """
                    console.log(typeof module, typeof module.id, typeof module.filename);
                    console.log(module.loaded, Array.isArray(module.paths), Array.isArray(module.children));
                    module.exports = {value: 1}; const alias = module; alias.exports = {value: 2};
                    console.log(module.exports.value); const key = 'exports'; module[key] = {value: 3}; console.log(alias[key].value);
                    """,
            },
            "main.cjs",
            "object string string\nfalse true true\n2\n2\n"
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["lib.cjs"] = """
                    const alias = module; alias.exports = {value: 7};
                    """,
                ["main.cjs"] = """
                    const one = require('./lib.cjs'); const two = require('./lib.cjs'); console.log(one.value, one === two);
                    """,
            },
            "main.cjs",
            "7 true\n"
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["lib.ts"] = """
                    export const value = 9;
                    """,
                ["nested/load.ts"] = """
                    export async function load(): Promise<any> { return await import('../lib'); }
                    """,
                ["main.ts"] = """
                    import { load } from './nested/load'; async function run(): Promise<void> { const one = await import('./lib'); const two = await load(); console.log(one.value, one === two); } run();
                    """,
            },
            "main.ts",
            "9 false\n"
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                    async function run(): Promise<void> { const path = './missing'; try { await import(path); } catch (error) { console.log('rejected'); } } run();
                    """,
            },
            "main.ts",
            "rejected\n"
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["lib.ts"] = """
                    export const value = 5;
                    """,
                ["main.ts"] = """
                    async function run(): Promise<void> { console.log((await import('./lib')).value); } run();
                    function* items(): Generator<any, void, any> { yield import('./lib'); } const it = items(); it.next().value.then(module => console.log(module.value));
                    """,
            },
            "main.ts",
            "5\n5\n"
        },
    ];

    [Theory]
    [MemberData(nameof(ModuleLoadingMetadataPrograms))]
    public void Isolated_ModuleLoadingMetadata_PreservesExportsRegistryAndImports(Dictionary<string, string> files, string entryPoint, string expected)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        foreach (var (path, source) in files) tempDir.CreateFile(path, source);
        var dllPath = tempDir.GetPath("module_metadata.dll");
        // The CLI discovers and type-checks dynamic-only dependencies before emitting the module graph.
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --standalone --verify", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> ReadlineMetadataPrograms =>
    [
        new object[]
        {
            """
            import { createInterface } from 'readline';
            const rl = createInterface({prompt:'start> '}); console.log(rl.getPrompt());
            rl.setPrompt('next> '); rl.prompt(); rl.write('text'); console.log('line');
            rl.close(); rl.prompt(); rl.write('hidden'); console.log(rl.getPrompt());
            """,
            "start> \nnext> textline\nnext> \n",
            ""
        },
        new object[]
        {
            """
            import { createInterface } from 'readline';
            const rl = createInterface();
            rl.on('pause', () => console.log('pause')); rl.on('resume', () => console.log('resume'));
            rl.once('close', () => console.log('close'));
            console.log(rl.pause() === rl); console.log(rl.resume() === rl); rl.close(); rl.close();
            console.log('done');
            """,
            "pause\ntrue\nresume\ntrue\nclose\ndone\n",
            ""
        },
        new object[]
        {
            """
            import { questionSync, createInterface } from 'readline';
            console.log(questionSync('first> ')); const rl = createInterface();
            rl.question('next> ', answer => console.log('callback', answer)); rl.close();
            rl.question('hidden> ', answer => console.log('hidden', answer)); console.log('done');
            """,
            "first> one\nnext> callback two\ndone\n",
            "one\ntwo\n"
        },
        new object[]
        {
            """
            import { questionSync, createInterface } from 'readline';
            console.log(questionSync('eof> ') === ''); const rl = createInterface();
            rl.question('next> ', answer => console.log(answer === '')); rl.close();
            """,
            "eof> true\nnext> true\n",
            ""
        },
        new object[]
        {
            """
            import * as readline from 'node:readline';
            const rl = new readline.Interface({prompt:'named> '});
            console.log(rl.getPrompt()); console.log(typeof readline.questionSync); rl.close();
            """,
            "named> \nfunction\n",
            ""
        },
        new object[]
        {
            """
            import { createInterface } from 'readline';
            const rl = createInterface(); let pauses = 0; let resumes = 0; let closes = 0;
            rl.on('pause', () => { pauses++; }); rl.on('resume', () => { resumes++; });
            rl.on('close', () => { closes++; });
            rl.pause(); rl.pause(); rl.resume(); rl.resume(); rl.close(); rl.close();
            console.log(pauses, resumes, closes);
            """,
            "2 2 2\n",
            ""
        },
    ];

    [Theory]
    [MemberData(nameof(ReadlineMetadataPrograms))]
    public void Isolated_ReadlineMetadata_PreservesStateEventsAndInput(string source, string expected, string input)
    {
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
            Assert.False(File.Exists(Path.Combine(tempDir, "SharpTS.dll")));
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000,
                verifyStandardError: error => Assert.Empty(error), standardInput: input));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    public static IEnumerable<object[]> TextEncodingMetadataPrograms =>
    [
        new object[]
        {
            """
            const encoder = new TextEncoder(); const decoder = new TextDecoder();
            const text = 'h\u00e9\u{1F600}'; const bytes = encoder.encode(text);
            console.log(encoder.encoding, bytes.length, decoder.decode(bytes) === text);
            console.log(encoder.encode('').length, decoder.decode().length, decoder.encoding);
            """,
            "utf-8 7 true\n0 0 utf-8\n"
        },
        new object[]
        {
            """
            const decoder = new TextDecoder('latin1');
            console.log(decoder.encoding, decoder.decode(Buffer.from([233])) === '\uFFFD');
            const unknown = new TextDecoder('not-a-real-label');
            console.log(unknown.encoding, unknown.decode(Buffer.from('ok')));
            """,
            "latin1 true\nnot-a-real-label ok\n"
        },
        new object[]
        {
            """
            const decoder = new TextDecoder(); const decode = decoder.decode;
            console.log(decode(Buffer.from('direct')));
            console.log(decode.call(decoder, Buffer.from('call')));
            console.log(decode.apply(decoder, [Buffer.from('apply')]));
            """,
            "direct\ncall\napply\n"
        },
        new object[]
        {
            """
            const encoder = new globalThis.TextEncoder(); const decoder = new globalThis.TextDecoder();
            console.log(decoder.decode(encoder.encode('global')));
            console.log(globalThis.TextEncoder === TextEncoder, globalThis.TextDecoder === TextDecoder);
            """,
            "global\ntrue true\n"
        },
        new object[]
        {
            """
            import { TextEncoder, TextDecoder } from 'util';
            const encoder = new TextEncoder(); const decoder = new TextDecoder();
            console.log(decoder.decode(encoder.encode('module')), encoder.encoding, decoder.encoding);
            """,
            "module utf-8 utf-8\n"
        },
        new object[]
        {
            """
            async function run(): Promise<void> {
             const encoder = new TextEncoder(); const decoder = new TextDecoder();
             console.log(decoder.decode(encoder.encode(await Promise.resolve('async'))));
            }
            run();
            function* items(): Generator<number, void, string> {
             const decoder = new TextDecoder(); console.log(decoder.decode(Buffer.from(yield 1)));
            }
            const it = items(); console.log(it.next().value); it.next('generator');
            """,
            "async\n1\ngenerator\n"
        },
    ];

    [Theory]
    [MemberData(nameof(TextEncodingMetadataPrograms))]
    public void Isolated_TextEncodingMetadata_PreservesConstructorsWrappersAndSuspension(string source, string expected)
    {
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
            Assert.False(File.Exists(Path.Combine(tempDir, "SharpTS.dll")));
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000,
                verifyStandardError: error => Assert.Empty(error)));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    public static IEnumerable<object[]> InspectionMetadataPrograms =>
    [
        new object[]
        {
            """
            console.dir(null); console.dir(undefined); console.dir('text'); console.dir(42.5);
            console.dir(true); console.dir(false); console.dir([]); console.dir({});
            globalThis.console.dir({global:'yes'});
            """,
            "null\nundefined\n'text'\n42.5\ntrue\nfalse\n[  ]\n{  }\n{ global: 'yes' }\n"
        },
        new object[]
        {
            """
            console.dir({name:'x', list:[1,{a:2}]});
            const obj: any = {}; obj.self = obj; console.dir(obj);
            const arr: any[] = []; arr.push(arr); console.dir(arr);
            """,
            "{ name: 'x', list: [ 1, [Object] ] }\n{ self: { self: [Object] } }\n[ [ [Array] ] ]\n"
        },
        new object[]
        {
            """
            async function run(): Promise<void> { console.dir(await Promise.resolve({label:'async', values:[1,2]})); }
            run();
            function* items(): Generator<number, void, any> { console.dir(yield 1); }
            const it = items(); console.log(it.next().value); it.next({label:'generator'});
            """,
            "{ label: 'async', values: [ 1, 2 ] }\n1\n{ label: 'generator' }\n"
        },
    ];

    [Theory]
    [MemberData(nameof(InspectionMetadataPrograms))]
    public void Isolated_InspectionMetadata_PreservesFormattingRecursionAndSuspension(string source, string expected)
    {
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
            Assert.False(File.Exists(Path.Combine(tempDir, "SharpTS.dll")));
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000,
                verifyStandardError: error => Assert.Empty(error)));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    public static IEnumerable<object[]> ConsoleMetadataPrograms =>
    [
        new object[]
        {
            """
            console.log(); console.info('info', 1); console.debug('debug');
            console.log(null, undefined, true, [1,2], {a:3});
            console.clear(); console.log('after clear');
            globalThis.console.log('global', 4);
            """,
            "\ninfo 1\ndebug\nnull undefined true [1, 2] { a: 3 }\nafter clear\nglobal 4\n",
            ""
        },
        new object[]
        {
            """
            console.log('Hello %s, %d %i %f', 'world', 42.7, -3.9, 3.5);
            console.log('JSON %j; object %o', {a:1}, {b:2});
            console.log('percent %% %s', 'ok'); console.log('missing %s %s', 'one');
            console.log('extra %s', 'one', 'two');
            """,
            "Hello world, 42 -3 3.5\nJSON {\"a\":1}; object { b: 2 }\npercent % ok\nmissing one %s\nextra one two\n",
            ""
        },
        new object[]
        {
            """
            console.count(); console.count(); console.countReset(); console.count();
            console.count('x'); console.count('x'); console.countReset('x'); console.count('x');
            console.group('outer'); console.log('one'); console.groupCollapsed('inner', 2);
            console.log('two'); console.groupEnd(); console.log('three'); console.groupEnd();
            console.groupEnd(); console.log('done');
            """,
            "default: 1\ndefault: 2\ndefault: 1\nx: 1\nx: 2\nx: 1\nouter\n  one\ninner 2\n    two\n  three\ndone\n",
            ""
        },
        new object[]
        {
            """
            console.error(); console.error('error', 2); console.warn('warn');
            console.assert(true, 'hidden'); console.assert(false); console.assert(false, 'failure', 3);
            console.log('stdout');
            """,
            "stdout\n",
            "\nerror 2\nwarn\nAssertion failed\nAssertion failed: failure 3\n"
        },
        new object[]
        {
            """
            console.table([{a:1}, {a:2}]); console.dir({name:'test', value:42});
            console.table([]); console.log('inspected');
            """,
            "+---------+----------------------+\n| (index) | Value                |\n+---------+----------------------+\n|       0 | { a: 1 }             |\n|       1 | { a: 2 }             |\n+---------+----------------------+\n{ name: 'test', value: 42 }\n(empty array)\ninspected\n",
            ""
        },
        new object[]
        {
            """
            async function run(): Promise<void> {
              console.log('await', await Promise.resolve(7), 'done');
              console.error('async', await Promise.resolve('err'));
              console.group('group', await Promise.resolve(2)); console.log('inside'); console.groupEnd();
            }
            run();
            """,
            "await 7 done\ngroup 2\n  inside\n",
            "async err\n"
        },
        new object[]
        {
            """
            function* values(): Generator<number, void, string> {
              console.log('yield', yield 3, 'done'); console.warn('warn', yield 4);
            }
            const it = values(); console.log(it.next().value); console.log(it.next('ok').value);
            console.log(it.next('end').done);
            """,
            "3\nyield ok done\n4\ntrue\n",
            "warn end\n"
        },
    ];

    [Theory]
    [MemberData(nameof(ConsoleMetadataPrograms))]
    public void Isolated_ConsoleMetadata_PreservesOutputStreamsAndSuspendedArguments(
        string source, string expectedOutput, string expectedError)
    {
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
            Assert.False(File.Exists(Path.Combine(tempDir, "SharpTS.dll")));
            Assert.Equal(expectedOutput, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000,
                verifyStandardError: error => Assert.Equal(expectedError, error)));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_ConsoleMetadata_PreservesTimerLifecycleAndTraceOutput()
    {
        const string source = """
            console.time('timer'); console.timeLog('timer'); console.timeEnd('timer');
            console.timeLog('timer'); console.timeEnd('timer');
            console.trace('single'); console.trace('multiple', 2); console.log('done');
            """;
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
            Assert.False(File.Exists(Path.Combine(tempDir, "SharpTS.dll")));
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000,
                verifyStandardError: error => Assert.Empty(error));
            var lines = output.Split('\n');
            Assert.Matches(@"^timer: [0-9]+(?:[.,][0-9]+)?ms$", lines[0]);
            Assert.Matches(@"^timer: [0-9]+(?:[.,][0-9]+)?ms$", lines[1]);
            Assert.Equal(2, lines.Count(line => line.StartsWith("timer:", StringComparison.Ordinal)));
            Assert.Contains("Trace: single\n", output);
            Assert.Contains("Trace: multiple 2\n", output);
            Assert.Contains("ConsoleTrace(", output);
            Assert.Contains("ConsoleTraceMultiple(", output);
            Assert.EndsWith("done\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    public static IEnumerable<object[]> OsMetadataPrograms
    {
        get
        {
            yield return new object[]
            {
                """
                import { freemem, loadavg, networkInterfaces } from 'os';
                console.log(typeof freemem(), freemem() > 0);
                const load = loadavg(); console.log(Array.isArray(load), load.length, load.join(','));
                console.log(Object.keys(networkInterfaces()).length);
                """,
                "number true\ntrue 3 0,0,0\n0\n"
            };
            yield return new object[]
            {
                """
                import * as os from 'node:os';
                console.log(os.freemem() > 0, os.loadavg().length, typeof os.networkInterfaces());
                console.log(typeof os.platform(), typeof os.arch(), os.hostname().length > 0);
                """,
                "true 3 object\nstring string true\n"
            };
            yield return new object[]
            {
                """
                import os from 'os';
                console.log(os.freemem() > 0, os.loadavg().join(','), Object.keys(os.networkInterfaces()).length);
                """,
                "true 0,0,0 0\n"
            };
        }
    }

    [Theory]
    [MemberData(nameof(OsMetadataPrograms))]
    public void Isolated_OsMetadata_PreservesHelpersAndModuleBindings(string source, string expected)
    {
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.DoesNotContain(GetAssemblyReferences(dllPath), name => name == "SharpTS");
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally { CleanupTempDir(tempDir); }
    }

    public static IEnumerable<object[]> ProcessMetadataPrograms
    {
        get
        {
            yield return new object[]
            {
                """
                import p from 'node:process';
                const value: any = process;
                console.log(p === value, value === (globalThis as any).process, String(value));
                (p as any).metadataFlag = 42;
                const env: any = p.env;
                env.SHARPTS_METADATA_PROBE = 'owned';
                console.log((process as any).metadataFlag, (process.env as any).SHARPTS_METADATA_PROBE, p.env === process.env);
                delete env.SHARPTS_METADATA_PROBE;
                console.log(Array.isArray(process.argv), process.ppid > 0, process.version === 'v' + process.versions.node);
                """,
                "true true [object process]\n42 owned true\ntrue true true\n"
            };
            yield return new object[]
            {
                """
                const time = process.uptime();
                const cpu = process.cpuUsage(); const delta = process.cpuUsage(cpu);
                console.log(cpu.user >= 0, cpu.system >= 0, delta.user >= 0, delta.system >= 0);
                const resources: any = process.resourceUsage();
                console.log(resources.maxRSS > 0, typeof resources.fsRead, typeof process.availableMemory());
                const memory = process.memoryUsage();
                console.log(memory.rss > 0, memory.heapUsed > 0, process.memoryUsage.rss() > 0);
                console.log(process.uptime() >= time, process.hrtime().length, typeof process.hrtime.bigint());
                console.log(Array.isArray(process.getActiveResourcesInfo()));
                """,
                "true true true true\ntrue number number\ntrue true true\ntrue 2 bigint\ntrue\n"
            };
            yield return new object[]
            {
                """
                const original = process.umask();
                console.log(process.umask(0o077) === original, process.umask() === 0o077);
                process.umask(original); console.log(process.umask() === original);
                const title = process.title; process.title = 'metadata-probe'; console.log(process.title); process.title = title;
                process.noDeprecation = true; console.log(process.noDeprecation); process.noDeprecation = false;
                const report: any = process.report.getReport();
                console.log(report.header.processId === process.pid, report.header.nodejsVersion === process.version);
                console.log(typeof report.resourceUsage, Array.isArray(report.nativeStack), process.report === process.report);
                """,
                "true true\ntrue\nmetadata-probe\ntrue\ntrue true\nobject true true\n"
            };
            yield return new object[]
            {
                """
                import { cpuUsage, hrtime, memoryUsage, versions, kill, emitWarning } from 'process';
                console.log(typeof cpuUsage, typeof hrtime, typeof memoryUsage, typeof kill, typeof emitWarning);
                console.log(typeof cpuUsage().user, hrtime().length, memoryUsage().rss > 0);
                const p: any = process;
                console.log(p.hrtime === p.hrtime, p.memoryUsage === p.memoryUsage);
                console.log(typeof p.hrtime.bigint(), p.memoryUsage.rss() > 0, versions.node === p.versions.node);
                """,
                "function function function function function\nnumber 2 true\ntrue true\nbigint true true\n"
            };
            yield return new object[]
            {
                """
                let rounds = 0;
                process.on('beforeExit', () => {
                    rounds++;
                    if (rounds === 1) setTimeout(() => console.log('extra work'), 1);
                });
                process.on('exit', (code: number) => console.log('exit after', rounds, code));
                """,
                "extra work\nexit after 2 0\n"
            };
            yield return new object[]
            {
                """
                process.on('warning', (warning: any) => console.log(warning.name, warning.message, warning.code));
                process.emitWarning('metadata', { type: 'CustomWarning', code: 'META' });
                """,
                "CustomWarning metadata META\n"
            };
            yield return new object[]
            {
                """
                console.log(process.stdout === process.stdout, process.stderr === process.stderr, process.stdin === process.stdin);
                const output: any = process.stdout;
                console.log(output.writable, process.stdin.readable, typeof process.stderr.on);
                output.write('stream:'); process.stdout.write('direct
                ');
                """,
                "true true true\ntrue true function\nstream:direct\n"
            };
            yield return new object[]
            {
                """
                import { nextTick } from 'node:process';
                console.log('main');
                nextTick((text: string, number: number, flag: boolean) => console.log(text, number, flag), 'tick', 7, true);
                """,
                "main\ntick 7 true\n"
            };
        }
    }

    [Theory]
    [MemberData(nameof(ProcessMetadataPrograms))]
    public void Isolated_ProcessMetadata_PreservesSingletonsHelpersLifecycleAndStreams(string source, string expected)
    {
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.DoesNotContain(GetAssemblyReferences(dllPath), name => name == "SharpTS");
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally { CleanupTempDir(tempDir); }
    }

    public static IEnumerable<object[]> FileSystemWatcherMetadataPrograms
    {
        get
        {
            yield return new object[]
            {
                """
                import { watch, writeFileSync, unlinkSync } from 'node:fs';
                const path = process.cwd() + '/watch.txt'; writeFileSync(path, 'old'); let detected = false;
                const watcher: any = watch(path, (event: string, filename: string) => {
                    if (detected) return; detected = true; watcher.close(); watcher.close(); clearTimeout(timeout);
                    console.log(event, filename === 'watch.txt'); unlinkSync(path);
                });
                const timeout = setTimeout(() => { watcher.close(); throw new Error('watch callback timed out'); }, 5000);
                setTimeout(() => writeFileSync(path, 'updated'), 50);
                """,
                "change true\n"
            };
            yield return new object[]
            {
                """
                import * as fs from 'fs';
                const path = process.cwd() + '/watch-options.txt'; fs.writeFileSync(path, 'old'); let detected = false;
                const watch = fs.watch;
                const watcher: any = watch(path, {}, (event: string, filename: string) => {
                    if (detected) return; detected = true; watcher.close(); clearTimeout(timeout);
                    console.log(event, filename === 'watch-options.txt'); fs.unlinkSync(path);
                });
                const timeout = setTimeout(() => { watcher.close(); throw new Error('options callback timed out'); }, 5000);
                setTimeout(() => fs.writeFileSync(path, 'updated'), 50);
                """,
                "change true\n"
            };
            yield return new object[]
            {
                """
                import * as fs from 'fs';
                const directory = process.cwd() + '/watch-directory'; fs.mkdirSync(directory); let detected = false;
                const watcher: any = fs.watch(directory);
                watcher.on('change', (event: string, filename: string) => {
                    if (detected) return; detected = true; watcher.close(); clearTimeout(timeout);
                    console.log(event, filename === 'created.txt'); fs.unlinkSync(directory + '/created.txt'); fs.rmdirSync(directory);
                });
                const timeout = setTimeout(() => { watcher.close(); throw new Error('directory callback timed out'); }, 5000);
                setTimeout(() => fs.writeFileSync(directory + '/created.txt', 'created'), 50);
                """,
                "change true\n"
            };
            yield return new object[]
            {
                """
                import { watchFile, unwatchFile, writeFileSync, appendFileSync, unlinkSync } from 'fs';
                const path = process.cwd() + '/poll.txt'; writeFileSync(path, 'old'); let detected = false;
                watchFile(path, { interval: 25 }, (curr: any, prev: any) => {
                    if (detected || curr.size !== 7) return; detected = true; unwatchFile(path); clearTimeout(timeout);
                    console.log(curr.size, prev.size, curr.isFile(), prev.isFile()); unlinkSync(path);
                });
                const timeout = setTimeout(() => { unwatchFile(path); throw new Error('poll callback timed out'); }, 5000);
                setTimeout(() => appendFileSync(path, 'new!'), 50);
                """,
                "7 3 true true\n"
            };
            yield return new object[]
            {
                """
                import * as fs from 'fs';
                class Options { interval: number = 25; }
                const path = process.cwd() + '/poll-class.txt'; fs.writeFileSync(path, 'old'); let detected = false;
                const watchFile = fs.watchFile; const unwatchFile = fs.unwatchFile;
                watchFile(path, new Options(), (curr: any, prev: any) => {
                    if (detected || curr.size !== 7) return; detected = true; unwatchFile(path); clearTimeout(timeout);
                    console.log(curr.size, prev.size); fs.unlinkSync(path);
                });
                const timeout = setTimeout(() => { unwatchFile(path); throw new Error('class options callback timed out'); }, 5000);
                setTimeout(() => fs.appendFileSync(path, 'new!'), 50);
                """,
                "7 3\n"
            };
            yield return new object[]
            {
                """
                import * as fs from 'fs';
                const path = process.cwd() + '/unwatch.txt'; fs.writeFileSync(path, 'old');
                fs.unwatchFile(path);
                fs.watchFile('unwatch.txt', (curr: any, prev: any) => console.log('unexpected'));
                fs.unwatchFile(path); fs.unwatchFile('unwatch.txt');
                const watcher: any = fs.watch(path); watcher.close(); watcher.close();
                fs.writeFileSync(path, 'updated'); fs.unlinkSync(path); console.log('closed');
                """,
                "closed\n"
            };
        }
    }

    [Theory]
    [MemberData(nameof(FileSystemWatcherMetadataPrograms))]
    public void Isolated_FileSystemWatcherMetadata_PreservesCallbacksPollingAndClose(string source, string expected)
    {
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.DoesNotContain(GetAssemblyReferences(dllPath), name => name == "SharpTS");
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally { CleanupTempDir(tempDir); }
    }

    public static IEnumerable<object[]> FileSystemStreamMetadataPrograms
    {
        get
        {
            yield return new object[]
            {
                """
                import * as fs from 'fs';
                fs.writeFileSync('range.txt', 'ABCDEFGH');
                const events: string[] = []; const chunks: string[] = [];
                const rs: any = fs.createReadStream('range.txt', { encoding: 'utf8', start: 1, end: 5, highWaterMark: 2 });
                rs.on('open', () => events.push('open')); rs.on('ready', () => events.push('ready'));
                rs.on('data', (c: any) => chunks.push(c)); rs.on('end', () => events.push('end'));
                rs.on('close', () => { events.push('close'); console.log(events.join('>')); console.log(chunks.join('|')); console.log(rs.bytesRead, rs.pending); fs.unlinkSync('range.txt'); });
                """,
                "open>ready>end>close\nBC|DE|F\n5 false\n"
            };
            yield return new object[]
            {
                """
                import { createWriteStream, readFileSync, unlinkSync } from 'node:fs';
                const events: string[] = []; const ws: any = createWriteStream('write.txt');
                ws.on('open', () => events.push('open')); ws.on('ready', () => events.push('ready'));
                ws.on('finish', () => events.push('finish'));
                ws.on('close', () => { events.push('close'); console.log(events.join('>')); console.log(ws.bytesWritten, ws.pending); console.log(readFileSync('write.txt', 'utf8')); });
                ws.write(Buffer.from('AB')); ws.end('CD'); unlinkSync('write.txt');
                """,
                "open>ready>finish>close\n4 false\nABCD\n"
            };
            yield return new object[]
            {
                """
                import * as fs from 'fs';
                fs.writeFileSync('source.txt', 'piped payload');
                const read = fs.createReadStream; const write = fs.createWriteStream;
                const rs: any = read('source.txt', { highWaterMark: 3 }); const ws: any = write('copy.txt');
                console.log(rs.pipe(ws) === ws); console.log(fs.readFileSync('copy.txt', 'utf8')); console.log(ws.bytesWritten);
                fs.unlinkSync('source.txt'); fs.unlinkSync('copy.txt');
                """,
                "true\npiped payload\n13\n"
            };
            yield return new object[]
            {
                """
                import * as fs from 'fs';
                fs.writeFileSync('descriptor.txt', 'ABCD'); const fd = fs.openSync('descriptor.txt', 'r+');
                const ws: any = fs.createWriteStream('descriptor.txt', { fd, start: 1, autoClose: false, emitClose: false });
                let closes = 0; ws.on('close', () => { closes++; }); ws.end('xy');
                console.log(fs.fstatSync(fd).size, closes); fs.closeSync(fd);
                const append: any = fs.createWriteStream('descriptor.txt', { flags: 'a' }); append.end('!');
                console.log(fs.readFileSync('descriptor.txt', 'utf8')); fs.unlinkSync('descriptor.txt');
                """,
                "4 0\nAxyD!\n"
            };
            yield return new object[]
            {
                """
                import * as fs from 'fs';
                fs.writeFileSync('read.txt', 'data'); const fd = fs.openSync('read.txt', 'r');
                const rs: any = fs.createReadStream('read.txt', { fd, encoding: 'utf8', emitClose: false, autoClose: false });
                let closed = false; let value = ''; rs.on('close', () => { closed = true; });
                rs.on('data', (c: any) => { value += c; });
                rs.on('end', () => { console.log(value, closed, fs.fstatSync(fd).size); fs.closeSync(fd); fs.unlinkSync('read.txt'); });
                """,
                "data false 4\n"
            };
            yield return new object[]
            {
                """
                import * as fs from 'fs'; import { Writable } from 'stream';
                fs.writeFileSync('node.txt', 'node stream'); let value = '';
                const destination: any = new Writable({ write(chunk: any, encoding: any, callback: any) { value += chunk.toString(); callback(); } });
                destination.on('finish', () => { console.log(value); fs.unlinkSync('node.txt'); });
                fs.createReadStream('node.txt', { highWaterMark: 2 }).pipe(destination);
                """,
                "node stream\n"
            };
        }
    }

    [Theory]
    [MemberData(nameof(FileSystemStreamMetadataPrograms))]
    public void Isolated_FileSystemStreamMetadata_PreservesEventsRangesDescriptorsAndPipes(string source, string expected)
    {
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.DoesNotContain(GetAssemblyReferences(dllPath), name => name == "SharpTS");
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally { CleanupTempDir(tempDir); }
    }

    public static IEnumerable<object[]> FileSystemAsyncMetadataPrograms
    {
        get
        {
            yield return new object[]
            {
                """
                import { writeFile, appendFile, readFile, stat, rename, unlink } from 'node:fs/promises';
                async function main() {
                  await writeFile('named.txt', 'ab'); await appendFile('named.txt', 'c');
                  await rename('named.txt', 'renamed.txt');
                  console.log(await readFile('renamed.txt', 'utf8'));
                  console.log((await stat('renamed.txt')).size);
                  await unlink('renamed.txt'); console.log('done');
                }
                main();
                """,
                "abc\n3\ndone\n"
            };
            yield return new object[]
            {
                """
                import * as fs from 'fs';
                async function main() {
                  const write = fs.promises.writeFile;
                  const read = fs.promises.readFile;
                  await write('namespace.txt', '6869', 'hex');
                  console.log((await read('namespace.txt')).toString());
                  console.log(typeof fs.promises.constants.F_OK);
                  await fs.promises.unlink('namespace.txt'); console.log('done');
                }
                main();
                """,
                "hi\nnumber\ndone\n"
            };
            yield return new object[]
            {
                """
                import * as fs from 'fs/promises';
                async function main() {
                  await Promise.all([fs.writeFile('left.txt', 'left'), fs.writeFile('right.txt', 'right')]);
                  const values = await Promise.all([fs.readFile('left.txt', 'utf8'), fs.readFile('right.txt', 'utf8')]);
                  console.log(values[0], values[1]);
                  await Promise.all([fs.unlink('left.txt'), fs.unlink('right.txt')]); console.log('done');
                }
                main();
                """,
                "left right\ndone\n"
            };
            yield return new object[]
            {
                """
                import * as fs from 'fs';
                import * as fsp from 'fs/promises';
                async function main() {
                  try { await fsp.readFile('missing.txt'); } catch (e: any) { console.log(e.code); }
                  await fsp.rm('missing.txt', { force: true });
                  await fsp.mkdir('tree'); await fsp.writeFile('tree/entry.txt', 'entry');
                  await fsp.rm('tree', { recursive: true, force: true });
                  console.log(fs.existsSync('tree')); console.log('done');
                }
                main();
                """,
                "ENOENT\nfalse\ndone\n"
            };
            yield return new object[]
            {
                """
                import * as fs from 'fs';
                fs.writeFileSync('callback.txt', 'callback');
                fs.readFile('callback.txt', 'utf8', (error: any, data: any) => {
                  console.log(data); fs.unlinkSync('callback.txt'); console.log('done');
                });
                """,
                "callback\ndone\n"
            };
            yield return new object[]
            {
                """
                import * as fs from 'fs';
                fs.readFile('missing-callback.txt', 'utf8', (error: any, data: any) => {
                  console.log(error.code); console.log('done');
                });
                """,
                "ENOENT\ndone\n"
            };
        }
    }

    [Theory]
    [MemberData(nameof(FileSystemAsyncMetadataPrograms))]
    public void Isolated_FileSystemAsyncMetadata_PreservesPromisesErrorsConcurrencyAndCallbackDrain(string source, string expected)
    {
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.DoesNotContain(GetAssemblyReferences(dllPath), name => name == "SharpTS");
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally { CleanupTempDir(tempDir); }
    }

    public static IEnumerable<object[]> FileSystemMetadataPrograms
    {
        get
        {
            yield return new object[]
            {
                """
                import * as fs from 'fs';
                fs.writeFileSync('encoding.txt', '6869', 'hex');
                fs.appendFileSync('encoding.txt', '!');
                console.log(fs.readFileSync('encoding.txt', 'utf8'));
                console.log(fs.readFileSync('encoding.txt').toString('hex'));
                const stat = fs.statSync('encoding.txt');
                console.log(stat.isFile(), stat.size);
                fs.unlinkSync('encoding.txt');
                """,
                "hi!\n686921\ntrue 3\n"
            };
            yield return new object[]
            {
                """
                import * as fs from 'fs';
                const fd = fs.openSync('descriptor.txt', 'w+');
                console.log(fs.writeSync(fd, Buffer.from('abcdef'), 0, 6, 0));
                const buffer = Buffer.alloc(3);
                console.log(fs.readSync(fd, buffer, 0, 3, 2), buffer.toString());
                fs.ftruncateSync(fd, 4); fs.fsyncSync(fd);
                console.log(fs.fstatSync(fd).size);
                fs.closeSync(fd);
                try { fs.readSync(fd, buffer, 0, 1, 0); } catch (e: any) { console.log(e.code); }
                fs.unlinkSync('descriptor.txt');
                """,
                "6\n3 cde\n4\nEBADF\n"
            };
            yield return new object[]
            {
                """
                import * as fs from 'fs';
                fs.mkdirSync('entries');
                fs.writeFileSync('entries/item.txt', 'abc');
                console.log(fs.statSync('entries').isDirectory());
                const entries = fs.readdirSync('entries', { withFileTypes: true });
                console.log(entries[0].name, entries[0].isFile());
                const dir = fs.opendirSync('entries');
                const entry = dir.readSync();
                console.log(entry.name, entry.isFile(), dir.readSync() === null);
                dir.closeSync();
                fs.unlinkSync('entries/item.txt'); fs.rmdirSync('entries');
                """,
                "true\nitem.txt true\nitem.txt true true\n"
            };
            yield return new object[]
            {
                """
                import * as fs from 'fs';
                fs.writeFileSync('original.txt', 'abcd');
                fs.linkSync('original.txt', 'linked.txt');
                fs.copyFileSync('linked.txt', 'copy.txt'); fs.renameSync('copy.txt', 'moved.txt');
                fs.truncateSync('moved.txt', 2);
                console.log(fs.readFileSync('linked.txt', 'utf8'), fs.readFileSync('moved.txt', 'utf8'));
                console.log(fs.lstatSync('moved.txt').isSymbolicLink());
                try { fs.readFileSync('missing.txt'); } catch (e: any) { console.log(e.code); }
                fs.unlinkSync('original.txt'); fs.unlinkSync('linked.txt'); fs.unlinkSync('moved.txt');
                """,
                "abcd ab\nfalse\nENOENT\n"
            };
            yield return new object[]
            {
                """
                import * as fs from 'fs';
                import * as fsp from 'fs/promises';
                async function main() {
                  await fsp.writeFile('async.txt', 'ab');
                  await fs.promises.appendFile('async.txt', 'c');
                  console.log(await fsp.readFile('async.txt', 'utf8'));
                  const stat = await fsp.stat('async.txt');
                  console.log(stat.isFile(), stat.size);
                  await fsp.unlink('async.txt');
                  console.log('done');
                }
                main();
                """,
                "abc\ntrue 3\ndone\n"
            };
            yield return new object[]
            {
                """
                import * as fs from 'fs';
                fs.writeFileSync('source.txt', 'stream');
                const source = fs.createReadStream('source.txt', { encoding: 'utf8' });
                console.log(source.read(), source.bytesRead);
                const destination = fs.createWriteStream('destination.txt');
                destination.write('out'); destination.end();
                console.log(fs.readFileSync('destination.txt', 'utf8'));
                fs.unlinkSync('source.txt'); fs.unlinkSync('destination.txt');
                """,
                "stream 6\nout\n"
            };
        }
    }

    [Theory]
    [MemberData(nameof(FileSystemMetadataPrograms))]
    public void Isolated_FileSystemMetadata_PreservesDataDescriptorsDirectoriesAndConsumers(string source, string expected)
    {
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.DoesNotContain(GetAssemblyReferences(dllPath), name => name == "SharpTS");
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally { CleanupTempDir(tempDir); }
    }

    public static IEnumerable<object[]> WebStreamMetadataPrograms
    {
        get
        {
            yield return new object[]
            {
                """
                import { CountQueuingStrategy, ByteLengthQueuingStrategy } from 'stream/web';
                const count = new CountQueuingStrategy({ highWaterMark: 7 });
                const bytes = new ByteLengthQueuingStrategy({ highWaterMark: 12 });
                console.log(count.highWaterMark, count.size('abc'));
                console.log(bytes.highWaterMark, bytes.size(Buffer.from('abc')));
                """,
                "7 1\n12 3\n"
            };
            yield return new object[]
            {
                """
                async function main() {
                  const stream = (ReadableStream as any).from(['x', 'y']);
                  const reader = stream.getReader();
                  console.log(stream.locked);
                  const a = await reader.read(); console.log(a.value, a.done);
                  const b = await reader.read(); console.log(b.value, b.done);
                  const c = await reader.read(); console.log(c.value, c.done);
                }
                main();
                """,
                "true\nx false\ny false\nundefined true\n"
            };
            yield return new object[]
            {
                """
                async function main() {
                  const stream = new WritableStream({ write(c: any) { console.log(c); }, close() { console.log('closed'); } });
                  const writer = stream.getWriter();
                  console.log(stream.locked);
                  await writer.write('written'); await writer.close();
                  console.log('done');
                }
                main();
                """,
                "true\nwritten\nclosed\ndone\n"
            };
            yield return new object[]
            {
                """
                async function main() {
                  const stream = new TransformStream({ transform(c: any, ctrl: any) { ctrl.enqueue(c * 2); }, flush(ctrl: any) { ctrl.enqueue(9); } });
                  const writer = stream.writable.getWriter();
                  await writer.write(2); await writer.close();
                  const reader = stream.readable.getReader();
                  console.log((await reader.read()).value);
                  console.log((await reader.read()).value);
                  console.log((await reader.read()).done);
                }
                main();
                """,
                "4\n9\ntrue\n"
            };
            yield return new object[]
            {
                """
                import * as consumers from 'stream/consumers';
                import { ReadableStream } from 'stream/web';
                async function main() {
                  const stream = new ReadableStream({ start(c: any) { c.enqueue('ab'); c.enqueue('cd'); c.close(); } });
                  console.log(await consumers.text(stream));
                }
                main();
                """,
                "abcd\n"
            };
            yield return new object[]
            {
                """
                const source = new ReadableStream();
                const reader = source.getReader();
                console.log(source.locked);
                reader.releaseLock(); console.log(source.locked);
                console.log(source.getReader() !== reader);
                const sink = new WritableStream({}, { highWaterMark: 5 });
                const writer = sink.getWriter();
                console.log(sink.locked, writer.desiredSize);
                writer.releaseLock(); console.log(sink.locked);
                const next = sink.getWriter(); console.log(next !== writer);
                console.log('done');
                """,
                "true\nfalse\ntrue\ntrue 5\nfalse\ntrue\ndone\n"
            };
        }
    }

    [Theory]
    [MemberData(nameof(WebStreamMetadataPrograms))]
    public void Isolated_WebStreamMetadata_PreservesStrategiesStreamsConsumersAndPeerAccess(string source, string expected)
    {
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.DoesNotContain(GetAssemblyReferences(dllPath), name => name == "SharpTS");
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    public static IEnumerable<object[]> NodeStreamMetadataPrograms
    {
        get
        {
            yield return new object[]
            {
                """
                import { Readable, Writable, Transform, PassThrough, pipeline, finished } from 'stream';
                const chunks: string[] = [];
                const source = new Readable({ objectMode: true });
                const upper = new Transform({ objectMode: true, transform(c: any, e: any, cb: any) { cb(null, String(c).toUpperCase()); } });
                const pass = new PassThrough({ objectMode: true });
                const sink = new Writable({ objectMode: true, write(c: any, e: any, cb: any) { chunks.push(c); cb(); } });
                const cleanup = finished(sink, () => console.log('removed'));
                cleanup();
                pipeline(source, upper, pass, sink, (err: any) => console.log('finished', err == null, chunks.join(',')));
                source.push('a'); source.push('b'); source.push(null);
                const buffered = new Writable({ write(c: any, e: any, cb: any) { console.log('write', c); cb(); } });
                buffered.cork(); buffered.write('x'); buffered.write('y');
                console.log('uncork'); buffered.uncork(); buffered.end();
                """,
                "finished true A,B\nuncork\nwrite x\nwrite y\n"
            };
            yield return new object[]
            {
                """
                import { compose, Transform, Duplex, getDefaultHighWaterMark, setDefaultHighWaterMark } from 'stream';
                async function main() {
                  const up = new Transform({ objectMode: true, transform(c: any, e: any, cb: any) { cb(null, String(c).toUpperCase()); } });
                  const bang = new Transform({ objectMode: true, transform(c: any, e: any, cb: any) { cb(null, c + '!'); } });
                  const composed = compose(up, bang);
                  composed.on('data', (c: any) => console.log(c));
                  composed.write('a'); composed.write('b'); composed.end();
                  for await (const n of Duplex.from([1, 2])) console.log(n);
                  console.log(getDefaultHighWaterMark(true));
                  setDefaultHighWaterMark(true, 23); console.log(getDefaultHighWaterMark(true));
                }
                main();
                """,
                "A!\nB!\n1\n2\n16\n23\n"
            };
            yield return new object[]
            {
                """
                import { Readable } from 'stream';
                async function main() {
                  const source = new Readable({ objectMode: true });
                  let n = 0;
                  const timer = setInterval(() => {
                    n++; if (n <= 2) source.push(n * 10);
                    else { source.push(null); clearInterval(timer); }
                  }, 5);
                  for await (const value of source) console.log(value);
                  const early = Readable.from(['a', 'b']);
                  for await (const value of early) { console.log(value); break; }
                  console.log(early.destroyed);
                  console.log('done');
                }
                main();
                """,
                "10\n20\na\ntrue\ndone\n"
            };
            yield return new object[]
            {
                """
                import { Readable, Writable, addAbortSignal, isErrored } from 'stream';
                import { pipeline, finished } from 'stream/promises';
                function main() {
                  const controller = new AbortController();
                  const source = new Readable({ objectMode: true });
                  source.on('error', (e: any) => console.log(e.name));
                  console.log(addAbortSignal(controller.signal, source) === source);
                  controller.abort(); console.log(source.destroyed, isErrored(source));
                  const input = new Readable();
                  const output = new Writable({ write(c: any, e: any, cb: any) { console.log(c); cb(); } });
                  input.push('promise'); input.push(null);
                  console.log(typeof pipeline(input, output).then);
                  console.log(typeof finished);
                  console.log('done');
                }
                main();
                """,
                "true\nAbortError\ntrue true\npromise\nfunction\nfunction\ndone\n"
            };
        }
    }

    [Theory]
    [MemberData(nameof(NodeStreamMetadataPrograms))]
    public void Isolated_NodeStreamMetadata_PreservesPipelinesCompositionIterationAndCancellation(string source, string expected)
    {
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.DoesNotContain(GetAssemblyReferences(dllPath), name => name == "SharpTS");
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    public static IEnumerable<object[]> EventLoopMetadataPrograms
    {
        get
        {
            yield return new object[]
            {
                """
                import * as net from 'net';
                const server = net.createServer((socket: any) => {
                    let received = '';
                    socket.on('data', (chunk: any) => {
                        received += chunk.toString();
                        if (received === 'ping') {
                            console.log('server', received);
                            socket.end('pong');
                        }
                    });
                });
                server.listen(0, '127.0.0.1', () => {
                    let received = '';
                    const client = net.createConnection({ port: server.address().port, host: '127.0.0.1' }, () => client.write('ping'));
                    client.on('data', (chunk: any) => { received += chunk.toString(); });
                    client.on('end', () => {
                        console.log('client', received);
                        client.destroy(); server.close(() => console.log('closed'));
                    });
                });
                """,
                "server ping\nclient pong\nclosed\n"
            };
            yield return new object[]
            {
                """
                import { Readable } from 'stream';
                async function main() {
                    const stream = new Readable({ objectMode: true });
                    let count = 0;
                    const timer = setInterval(() => {
                        count++;
                        if (count <= 2) stream.push(count * 10);
                        else { stream.push(null); clearInterval(timer); }
                    }, 5);
                    for await (const value of stream) console.log(value);
                    console.log('done');
                }
                main();
                """,
                "10\n20\ndone\n"
            };
            yield return new object[]
            {
                """
                let turns = 0;
                process.on('beforeExit', () => {
                    console.log('before', turns);
                    if (turns === 0) { turns++; setTimeout(() => console.log('late'), 1); }
                });
                process.on('exit', (code: number) => console.log('exit', code));
                console.log('start');
                """,
                "start\nbefore 0\nlate\nbefore 1\nexit 0\n"
            };
            yield return new object[]
            {
                """
                async function main() {
                    await new Promise<void>(() => {});
                    console.log('unexpected');
                }
                main();
                console.log('sync');
                """,
                "sync\n"
            };
        }
    }

    [Theory]
    [MemberData(nameof(EventLoopMetadataPrograms))]
    public void Isolated_EventLoopMetadata_PreservesIoResumeLifecycleAndQuiescence(string source, string expected)
    {
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.DoesNotContain(GetAssemblyReferences(dllPath), name => name == "SharpTS");
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    public static IEnumerable<object[]> TimerMetadataPrograms
    {
        get
        {
            yield return new object[]
            {
                """
                import * as timers from 'timers';
                import { setImmediate, clearImmediate } from 'node:timers';
                const cancelled = timers.setTimeout(() => console.log('cancelled'), 1000);
                console.log(cancelled.hasRef);
                cancelled.unref(); console.log(cancelled.hasRef);
                cancelled.ref(); console.log(cancelled.hasRef);
                timers.clearTimeout(cancelled);
                const immediate = setImmediate(() => console.log('cancelled immediate'));
                clearImmediate(immediate);
                setImmediate((value: string) => console.log(value), 'immediate');
                let ticks = 0;
                const interval = timers.setInterval(() => {
                    ticks++;
                    if (ticks === 2) { timers.clearInterval(interval); console.log('ticks', ticks); }
                }, 10);
                timers.setTimeout((a: string, b: number) => console.log(a, b), 0, 'args', 7);
                """,
                "true\nfalse\ntrue\nimmediate\nargs 7\nticks 2\n"
            };
            yield return new object[]
            {
                """
                console.log('sync');
                queueMicrotask(() => console.log('microtask 1'));
                Promise.resolve(1).then(() => console.log('promise'));
                queueMicrotask(() => {
                    console.log('microtask 2');
                    queueMicrotask(() => console.log('nested'));
                });
                setTimeout(() => console.log('timer'), 0);
                """,
                "sync\nmicrotask 1\npromise\nmicrotask 2\nnested\ntimer\n"
            };
            yield return new object[]
            {
                """
                import * as timers from 'node:timers/promises';
                async function main() {
                    console.log(await timers.setTimeout(1, 'timeout'));
                    console.log(await timers.setImmediate('immediate'));
                    const controller = new AbortController();
                    controller.abort();
                    try { await timers.setTimeout(1000, 'bad', { signal: controller.signal }); }
                    catch (error: any) { console.log(error.message); }
                    try { await timers.setImmediate('bad', { signal: controller.signal }); }
                    catch (error: any) { console.log(error.message); }
                    let count = 0;
                    for await (const value of timers.setInterval(1, 'tick')) {
                        console.log(value);
                        count++;
                        if (count === 2) break;
                    }
                    console.log('done', count);
                }
                main();
                """,
                "timeout\nimmediate\nAbortError: The operation was aborted\nAbortError: The operation was aborted\ntick\ntick\ndone 2\n"
            };
            yield return new object[]
            {
                """
                let count = 0;
                queueMicrotask(() => console.log('microtask'));
                const interval = setInterval(() => {
                    Date.now();
                    count++;
                    if (count === 2) clearInterval(interval);
                }, 1);
                const deadline = Date.now() + 5000;
                while (count < 2 && Date.now() < deadline) { }
                console.log('count', count);
                """,
                "microtask\ncount 2\n"
            };
        }
    }

    [Theory]
    [MemberData(nameof(TimerMetadataPrograms))]
    public void Isolated_TimerMetadata_PreservesSchedulingCancellationAndPromiseJobs(string source, string expected)
    {
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.DoesNotContain(GetAssemblyReferences(dllPath), name => name == "SharpTS");
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_EventEmitterMetadata_ListenerOrderingOnceRemovalAndLimitsPassStandaloneAndILChecks()
    {
        const string source = """
            import { EventEmitter } from 'events';
            const ee = new EventEmitter();
            let seen = '';
            const regular = (value: string) => { seen += 'r' + value; };
            const once = (value: string) => { seen += 'o' + value; };
            ee.on('x', regular);
            ee.prependOnceListener('x', once);
            console.log(ee.emit('x', '1'), ee.emit('x', '2'));
            console.log(seen, ee.listenerCount('x'), ee.listeners('x').length);
            ee.off('x', regular);
            console.log(ee.emit('x', '3'), ee.eventNames().length);
            console.log(ee.setMaxListeners(17) === ee, ee.getMaxListeners());
            ee.once('other', regular); ee.removeAllListeners();
            console.log(ee.eventNames().length);
            """;
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.DoesNotContain(GetAssemblyReferences(dllPath), name => name == "SharpTS");
            Assert.Equal("true true\no1r1r2 1 1\nfalse 0\ntrue 17\n0\n", ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_EventEmitterMetadata_ErrorMonitorAndRejectionRoutingPassStandaloneAndILChecks()
    {
        const string source = """
            import { EventEmitter, errorMonitor } from 'events';
            const ee = new EventEmitter({ captureRejections: true });
            const marker: any = { message: 'rejected' };
            ee.on(errorMonitor, (reason: any) => console.log('monitor', reason === marker));
            ee.on('error', (reason: any) => console.log('error', reason === marker));
            ee.on('go', () => Promise.reject(marker));
            ee.emit('go');
            const plain = new EventEmitter();
            plain.on(errorMonitor, () => console.log('unhandled-monitor'));
            try { plain.emit('error', new Error('boom')); }
            catch (error: any) { console.log(error.message); }
            """;
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.DoesNotContain(GetAssemblyReferences(dllPath), name => name == "SharpTS");
            Assert.Equal("monitor true\nerror true\nunhandled-monitor\nboom\n", ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_EventEmitterMetadata_ProcessAndStreamConsumersPassStandaloneAndILChecks()
    {
        const string source = """
            import { PassThrough } from 'stream';
            process.once('custom', (value: number) => console.log('process', value));
            process.emit('custom', 7); process.emit('custom', 9);
            const stream = new PassThrough();
            stream.on('data', (chunk: any) => console.log('data', chunk.toString()));
            stream.once('end', () => console.log('end'));
            stream.write('payload'); stream.end();
            """;
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.DoesNotContain(GetAssemblyReferences(dllPath), name => name == "SharpTS");
            Assert.Equal("process 7\ndata payload\nend\n", ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_EventsModule_ShouldExecuteWithoutSharpTsDll()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();
                let count = 0;
                emitter.on('test', () => { count++; });
                emitter.emit('test');
                emitter.emit('test');
                console.log(count);
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("2\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_OsModule_ShouldExecuteWithoutSharpTsDll()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                console.log(typeof os.platform());
                console.log(os.homedir().length > 0);
                console.log(typeof os.tmpdir());
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("string\ntrue\nstring\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_QuerystringModule_ShouldExecuteWithoutSharpTsDll()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { parse, stringify } from 'querystring';
                const parsed = parse('foo=bar&baz=qux');
                console.log(parsed.foo);
                console.log(stringify({ a: '1', b: '2' }));
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("bar\na=1&b=2\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_StreamModule_ShouldExecuteWithoutSharpTsDll()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { PassThrough } from 'stream';
                const pt = new PassThrough();
                pt.write('hello');
                pt.end();
                const data = pt.read();
                console.log(data);
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("hello\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_StringDecoderModule_ShouldExecuteWithoutSharpTsDll()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { StringDecoder } from 'string_decoder';
                const decoder = new StringDecoder('utf8');
                const result = decoder.write(Buffer.from('hello'));
                console.log(result);
                console.log(decoder.encoding);
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("hello\nutf8\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_TimersModule_ShouldExecuteWithoutSharpTsDll()
    {
        var source = """
            let called = false;
            const handle = setTimeout(() => { called = true; }, 10);
            clearTimeout(handle);
            console.log(called);
            console.log(typeof handle);
            """;

        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("false\nobject\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_UrlModule_ShouldExecuteWithoutSharpTsDll()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { parse, format } from 'url';
                const parsed = parse('https://example.com:8080/path?key=value');
                console.log(parsed.hostname);
                console.log(parsed.port);
                console.log(parsed.pathname);
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("example.com\n8080\n/path\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_ZlibModule_ShouldExecuteWithoutSharpTsDll()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('hello world');
                const compressed = zlib.deflateSync(input);
                const decompressed = zlib.inflateSync(compressed);
                console.log(decompressed.toString());
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("hello world\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_DgramModule_ShouldSendAndReceiveWithoutSharpTsDll()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dgram from 'dgram';
                import { createSocket } from 'dgram';
                const receiver = dgram.createSocket('udp4');
                receiver.on('message', (msg: any, rinfo: any) => {
                    console.log(msg.toString());
                    console.log(rinfo.address === '127.0.0.1');
                    console.log(rinfo.size === 5);
                    receiver.close();
                });
                receiver.bind(0, '127.0.0.1', () => {
                    const sender = createSocket('udp4');
                    sender.send('hello', receiver.address().port, '127.0.0.1', () => sender.close());
                });
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
            Assert.False(File.Exists(Path.Combine(tempDir, "SharpTS.dll")));
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("hello\ntrue\ntrue\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_DnsModule_ShouldExecuteWithoutSharpTsDll()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { lookup } from 'dns';
                const result = lookup('localhost');
                console.log(typeof result.address);
                console.log(result.family === 4 || result.family === 6);
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("string\ntrue\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_PerfHooksModule_ShouldExecuteWithoutSharpTsDll()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { performance } from 'perf_hooks';
                const start = performance.now();
                let sum = 0;
                for (let i = 0; i < 1000; i++) sum += i;
                const elapsed = performance.now() - start;
                console.log(elapsed >= 0);
                console.log(typeof elapsed);
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("true\nnumber\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_ProcessModule_ShouldExecuteWithoutSharpTsDll()
    {
        var source = """
            console.log(typeof process.platform);
            console.log(typeof process.pid);
            console.log(process.ppid > 0);
            console.log(process.cwd().length > 0);
            """;

        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("string\nnumber\ntrue\ntrue\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// Phase 3 guardrail: the TypeScript net facade and its native enforcement
    /// handle must remain executable without SharpTS.dll co-located.
    /// </summary>
    [Fact]
    public void Isolated_NetFacade_ShouldExecuteWithoutSharpTsDll()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as net from 'net';
                const list = new net.BlockList();
                list.addAddress('127.0.0.1');
                const address = new net.SocketAddress({ family: 'ipv6', address: '2001:db8::1' });
                const socket = net.Socket({ highWaterMark: 2048 });
                console.log(net.connect === net.createConnection);
                console.log(list.check('127.0.0.1'));
                console.log(address.address);
                console.log(socket.writableHighWaterMark);
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.DoesNotContain(GetAssemblyReferences(dllPath), r => r == "SharpTS");
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("true\ntrue\n2001:db8::1\n2048\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public void Isolated_NetTransport_ShouldSendAndReceiveWithoutSharpTsDll()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { createServer, createConnection, BlockList } from 'net';
                const list = new BlockList();
                list.addAddress('127.0.0.2');
                const server = createServer({ blockList: list }, (socket: any) => {
                    socket.setEncoding('utf8');
                    let received = '';
                    socket.on('data', (chunk: string) => { received += chunk; });
                    socket.on('end', () => {
                        console.log(received);
                        socket.end();
                        server.close();
                    });
                });
                server.listen(0, '127.0.0.1', () => {
                    const client = createConnection({ port: server.address().port, host: '127.0.0.1' });
                    client.on('connect', () => client.end('hello from TCP'));
                });
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.DoesNotContain(GetAssemblyReferences(dllPath), r => r == "SharpTS");
            Assert.Equal("hello from TCP\n", ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// #1033 guardrail: a --compile'd tls client↔server program must complete a real SslStream
    /// handshake and exchange data with NO SharpTS.dll co-located. The handshake, introspection,
    /// and socket I/O are all emitted as pure-BCL IL, so the output DLL is genuinely standalone.
    /// </summary>
    [Fact]
    public void Isolated_Tls_ShouldExecuteWithoutSharpTsDll()
    {
        var (certPem, keyPem) = GenerateSelfSignedCertForStandalone();
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as tls from 'tls';
                const cert = `{{certPem}}`;
                const key = `{{keyPem}}`;
                const server = tls.createServer({ cert, key }, (socket: any) => {
                    socket.write('hi-from-server');
                    socket.end();
                    server.close();
                });
                server.on('tlsClientError', (err: any) => {
                    console.log('tls-server-error:' + err.message);
                    server.close();
                });
                server.listen(0, '127.0.0.1', () => {
                    const addr = server.address();
                    const client = tls.connect(addr.port, '127.0.0.1', { rejectUnauthorized: false });
                    client.on('error', (err: any) => {
                        console.log('tls-client-error:' + err.message);
                        server.close();
                    });
                    client.setEncoding('utf8');
                    let data = '';
                    client.on('data', (c: string) => { data += c; });
                    client.on('end', () => {
                        console.log('recv:' + data);
                        console.log('encrypted:' + client.encrypted);
                        const proto = client.getProtocol() as string;
                        console.log('protoOk:' + (proto !== null && proto.indexOf('TLSv1.') === 0));
                        client.destroy();
                    });
                });
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            // No SharpTS.dll is copied next to the DLL — a genuine standalone run.
            Assert.DoesNotContain(GetAssemblyReferences(dllPath), r => r == "SharpTS");

            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 20000);
            Assert.Equal("recv:hi-from-server\nencrypted:true\nprotoOk:true\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Isolated_ProbeTimeout_ShouldTerminateChildAndReportCapturedOutput(bool supplyInput)
    {
        var source = """
            console.log('probe-started');
            setInterval(() => {}, 1000);
            """;

        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            // The 500 ms window tests termination of a child that is known to be running.
            // CLR startup is a separate phase and must not race the output assertion.
            var ex = Assert.Throws<TimeoutException>(
                () => ExecuteCompiledDllIsolated(
                    dllPath,
                    timeoutMs: 500,
                    timeoutStartsAfterOutput: "probe-started",
                    standardInput: supplyInput ? new string('x', 1024 * 1024) : null));
            Assert.Contains("timed out after 500 ms", ex.Message);
            Assert.Contains("probe-started", ex.Message);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    private static (string certPem, string keyPem) GenerateSelfSignedCertForStandalone()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var certReq = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=localhost", rsa,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        var sanBuilder = new System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        certReq.CertificateExtensions.Add(sanBuilder.Build());
        var cert = certReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));
        return (cert.ExportCertificatePem().Replace("`", "\\`"), rsa.ExportPkcs8PrivateKeyPem().Replace("`", "\\`"));
    }

    [Fact]
    public void FetchWebApiCookiesRedirectsAndRejectionRunStandalone()
    {
        using var server = new MockHttpServer();
        server.AddSetCookieRoute("/set", "session=value; Path=/");
        server.AddCookieEchoRoute("/echo");
        server.AddSetCookieRedirectRoute("/redirect", "/echo", "redirect=yes; Path=/", 302);
        server.Start();
        var source = $$"""
            async function main() {
                const headers = new Headers({ 'X-Test': 'one' });
                headers.set('X-Test', 'two');
                console.log('header=' + headers.get('x-test'));
                const request = new Request('{{server.BaseUrl}}echo', { method: 'POST', body: 'payload' });
                console.log('request=' + await request.clone().text());
                console.log('original=' + await request.text());
                const response = Response.json({ ok: true });
                console.log('json=' + (await response.clone().json()).ok);
                await response.text();
                console.log('body-used=' + response.bodyUsed);
                fetch.cookieJar.clear();
                await fetch('{{server.BaseUrl}}set');
                console.log('stored=' + fetch.cookieJar.getCookies('{{server.BaseUrl}}').includes('session=value'));
                const omitted = await fetch('{{server.BaseUrl}}echo', { credentials: 'omit' });
                console.log('omitted=' + (await omitted.text() === ''));
                const manual = await fetch('{{server.BaseUrl}}redirect', { redirect: 'manual', credentials: 'omit' });
                console.log('manual=' + manual.status);
                const manualCookies = await fetch('{{server.BaseUrl}}redirect', { redirect: 'manual' });
                console.log('manual-cookies=' + manualCookies.status);
                const followed = await fetch('{{server.BaseUrl}}redirect');
                const followedBody = await followed.text();
                console.log('followed=' + followedBody.includes('redirect=yes'));
                fetch.cookieJar.setCookie('manual=injected; Path=/', '{{server.BaseUrl}}');
                console.log('injected=' + fetch.cookieJar.getCookies('{{server.BaseUrl}}').includes('manual=injected'));
                fetch.cookieJar.clear();
                console.log('cleared=' + (fetch.cookieJar.getCookies('{{server.BaseUrl}}') === ''));
                try { await fetch('invalid-url'); } catch (error) { console.log('rejected'); }
            }
            main();
            """;
        var errors = TestHarness.CompileAndVerifyOnly(source);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000);
            Assert.Equal("header=two\nrequest=payload\noriginal=payload\njson=true\nbody-used=true\nstored=true\nomitted=true\nmanual=302\nmanual-cookies=302\nfollowed=true\ninjected=true\ncleared=true\nrejected\n", output);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    private static (string tempDir, string dllPath) CompileStandalone(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_standalone_guard_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var dllPath = Path.Combine(tempDir, "standalone_test.dll");

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseOrThrow();

        var checker = new TypeChecker();
        var typeMap = checker.Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);

        var compiler = new ILCompiler("standalone_test");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        compiler.Save(dllPath);

        File.WriteAllText(
            Path.Combine(tempDir, "standalone_test.runtimeconfig.json"),
            """
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "framework": {
                  "name": "Microsoft.NETCore.App",
                  "version": "10.0.0"
                }
              }
            }
            """);

        return (tempDir, dllPath);
    }

    private static (string tempDir, string dllPath) CompileStandaloneModule(
        Dictionary<string, string> files,
        string entryPoint)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_standalone_guard_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var dllPath = Path.Combine(tempDir, "standalone_test.dll");

        foreach (var (path, content) in files)
        {
            var fullPath = Path.Combine(tempDir, path);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, content);
        }

        var entryPath = Path.Combine(tempDir, entryPoint);
        var resolver = new ModuleResolver(entryPath);
        var entryModule = resolver.LoadModule(entryPath);
        var modules = resolver.GetModulesInOrder(entryModule);

        var checker = new TypeChecker();
        var typeMap = TestHarness.CheckModulesOrThrow(checker, modules, resolver);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(modules.SelectMany(m => m.Statements).ToList());

        var compiler = new ILCompiler("standalone_test");
        compiler.CompileModules(modules, resolver, typeMap, deadCodeInfo);
        compiler.Save(dllPath);

        File.WriteAllText(
            Path.Combine(tempDir, "standalone_test.runtimeconfig.json"),
            """
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "framework": {
                  "name": "Microsoft.NETCore.App",
                  "version": "10.0.0"
                }
              }
            }
            """);

        return (tempDir, dllPath);
    }

    private static string ExecuteCompiledDllIsolated(
        string dllPath,
        int timeoutMs,
        string? timeoutStartsAfterOutput = null,
        int readinessTimeoutMs = 15000,
        Action<string>? verifyStandardError = null,
        string? standardInput = null)
    {
        var workingDir = Path.GetDirectoryName(dllPath)!;
        var psi = new ProcessStartInfo("dotnet", dllPath)
        {
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDir
        };

        using var process = Process.Start(psi)!;
        TaskCompletionSource<bool>? readiness = null;
        Task<string> outputTask;
        if (timeoutStartsAfterOutput is null)
        {
            outputTask = process.StandardOutput.ReadToEndAsync();
        }
        else
        {
            readiness = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            outputTask = ReadToEndAndSignalAsync(
                process.StandardOutput,
                timeoutStartsAfterOutput,
                readiness);
        }

        var errorTask = process.StandardError.ReadToEndAsync();

        using var inputCancellation = new CancellationTokenSource();
        var inputTask = standardInput is null ? Task.CompletedTask
            : WriteInputAndCloseAsync(process.StandardInput, standardInput, inputCancellation.Token);
        // Timeout cleanup must preserve its diagnostic even if killing the child breaks a pending write.
        _ = inputTask.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try
        {
            if (readiness is not null &&
                (!readiness.Task.Wait(readinessTimeoutMs) || !readiness.Task.GetAwaiter().GetResult()))
            {
                TryTerminateProcessTree(process);
                process.WaitForExit(5000);
                Task.WaitAll([outputTask, errorTask], 5000);

                var readinessOutput = outputTask.IsCompletedSuccessfully ? outputTask.Result : string.Empty;
                var readinessError = errorTask.IsCompletedSuccessfully ? errorTask.Result : string.Empty;
                throw new TimeoutException(
                    $"Compiled standalone probe did not emit '{timeoutStartsAfterOutput}' within " +
                    $"{readinessTimeoutMs} ms. Stdout: {readinessOutput} Stderr: {readinessError}");
            }

            if (!process.WaitForExit(timeoutMs))
            {
                TryTerminateProcessTree(process);

                if (!process.WaitForExit(5000))
                {
                    throw new TimeoutException(
                        $"Compiled standalone probe timed out after {timeoutMs} ms and its process tree did not terminate.");
                }

                if (!Task.WaitAll([outputTask, errorTask], 5000))
                {
                    throw new TimeoutException(
                        $"Compiled standalone probe timed out after {timeoutMs} ms and its output pipes did not close.");
                }

                var timedOutOutput = outputTask.GetAwaiter().GetResult();
                var timedOutError = errorTask.GetAwaiter().GetResult();
                throw new TimeoutException(
                    $"Compiled standalone probe timed out after {timeoutMs} ms. " +
                    $"Stdout: {timedOutOutput} Stderr: {timedOutError}");
            }

            if (!Task.WaitAll([outputTask, errorTask], 5000))
                throw new TimeoutException("Compiled standalone probe exited, but its output pipes did not close.");

            var output = outputTask.GetAwaiter().GetResult();
            var error = errorTask.GetAwaiter().GetResult();

            if (process.ExitCode != 0)
            {
                throw new Exception(
                    $"Compiled standalone probe exited with code {process.ExitCode}. Stderr: {error}");
            }

            if (!inputTask.Wait(5000))
                throw new TimeoutException("Compiled standalone probe exited, but its input pipe did not close.");
            inputTask.GetAwaiter().GetResult();

            verifyStandardError?.Invoke(error.Replace("\r\n", "\n"));
            return output.Replace("\r\n", "\n");
        }
        finally
        {
            inputCancellation.Cancel();
        }

    }

    private static async Task WriteInputAndCloseAsync(StreamWriter writer, string input, CancellationToken cancellationToken)
    {
        await writer.WriteAsync(input.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        writer.Close();
    }

    private static async Task<string> ReadToEndAndSignalAsync(
        StreamReader reader,
        string readinessMarker,
        TaskCompletionSource<bool> readiness)
    {
        var output = new StringBuilder();
        var buffer = new char[1024];

        while (true)
        {
            var charsRead = await reader.ReadAsync(buffer).ConfigureAwait(false);
            if (charsRead == 0)
                break;

            output.Append(buffer, 0, charsRead);
            if (!readiness.Task.IsCompleted &&
                output.ToString().Contains(readinessMarker, StringComparison.Ordinal))
            {
                readiness.TrySetResult(true);
            }
        }

        readiness.TrySetResult(false);
        return output.ToString();
    }

    private static void TryTerminateProcessTree(Process process)
    {
        ProcessTreeTermination.Terminate(process);
    }

    private static List<string> GetAssemblyReferences(string dllPath)
    {
        using var stream = File.OpenRead(dllPath);
        using var peReader = new PEReader(stream);
        var metadataReader = peReader.GetMetadataReader();

        var refs = new List<string>();
        foreach (var refHandle in metadataReader.AssemblyReferences)
        {
            var reference = metadataReader.GetAssemblyReference(refHandle);
            refs.Add(metadataReader.GetString(reference.Name));
        }
        return refs;
    }

    private static void CleanupTempDir(string tempDir)
    {
        try
        {
            Directory.Delete(tempDir, true);
        }
        catch
        {
            // Ignore cleanup failures in tests.
        }
    }

}
