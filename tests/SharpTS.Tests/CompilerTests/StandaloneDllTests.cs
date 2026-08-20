using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Parsing;
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
        "Compilation/RuntimeEmitter.TSProcess.cs",           // process.ppid late-binds to ProcessBuiltIns.GetParentPid (graceful 0 when SharpTS absent) — #1085
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
                items: { id: number; label: string; note: null }[]
            } = {
                items: [{ id: 1, label: "a", note: null }]
            };
            const json: string = JSON.stringify(payload);
            const parsed: any = JSON.parse(json);
            console.log(json);
            console.log(parsed.items[0].id, parsed.items[0].label,
                parsed.items[0].note === null);
            """;

        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            Assert.DoesNotContain(
                GetAssemblyReferences(dllPath), r => r == "SharpTS");
            Assert.Equal(
                "{\"items\":[{\"id\":1,\"label\":\"a\",\"note\":null}]}\n" +
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
                import * as vm from 'vm';
                console.log(vm.runInNewContext('1 + 1'));
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            var ex = Assert.Throws<Exception>(() => ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
            Assert.Contains("vm module is not supported in standalone", ex.Message);
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
                import * as cluster from 'cluster';
                if (cluster.isPrimary) {
                    cluster.fork();
                }
                """
        };

        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            var ex = Assert.Throws<Exception>(() => ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
            Assert.Contains("cluster requires the SharpTS runtime", ex.Message);
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
            console.log(process.cwd().length > 0);
            """;

        var (tempDir, dllPath) = CompileStandalone(source);
        try
        {
            var output = ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000);
            Assert.Equal("string\nnumber\ntrue\n", output);
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

    [Fact]
    public void Isolated_ProbeTimeout_ShouldTerminateChildAndReportCapturedOutput()
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
                    timeoutStartsAfterOutput: "probe-started"));
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
        int readinessTimeoutMs = 15000)
    {
        var workingDir = Path.GetDirectoryName(dllPath)!;
        var psi = new ProcessStartInfo("dotnet", dllPath)
        {
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

        return output.Replace("\r\n", "\n");
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
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the timeout and Kill().
        }
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
