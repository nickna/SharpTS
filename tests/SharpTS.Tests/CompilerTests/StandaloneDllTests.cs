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

    public static IEnumerable<object[]> StructuredCloneMetadataPrograms =>
    [
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const values:any[]=[null,undefined,1,true,'text',123n];for(const value of values)console.log(structuredClone(value)===value);"
            },
            "true\ntrue\ntrue\ntrue\ntrue\ntrue\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const value:any={id:1,nested:[{value:2}]};const copy:any=structuredClone(value);value.id=9;value.nested[0].value=8;console.log(copy.id,copy.nested[0].value,copy===value,copy.nested===value.nested);"
            },
            "1 2 false false\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const values:number[]=[];for(let i=0;i<3;i++)values.push(i+1);const copy:any=structuredClone(values);values[0]=9;console.log(copy.join(','),values[0]);"
            },
            "1,2,3 9\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const key:any={id:1};const value:any={n:2};const map=new Map<any,any>();map.set(key,value);const copy:any=structuredClone(map);value.n=8;const copiedKey:any=Array.from(copy.keys())[0];console.log(copy.size,copy.has(key),copiedKey.id,copy.get(copiedKey).n);"
            },
            "1 false 1 2\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const value=new Set<any>([1,2]);const copy:any=structuredClone(value);copy.add(3);console.log(value.size,copy.size,value.has(3),copy.has(2));"
            },
            "2 3 false true\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const value=new ArrayBuffer(4);const view=new Uint8Array(value);view[0]=7;const copy:any=structuredClone(value);view[0]=9;const copiedView=new Uint8Array(copy);console.log(copy.byteLength,copiedView[0],copy===value);"
            },
            "4 7 false\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const buffer=new SharedArrayBuffer(8);const view=new Int32Array(buffer);view[0]=7;const copy:any=structuredClone(view);copy[0]=9;console.log(copy!==view,copy.buffer===buffer,view[0],structuredClone(buffer)===buffer);"
            },
            "true true 9 true\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const value=new Uint8Array([1,2]);const copy:any=structuredClone(value);value[0]=9;console.log(copy[0],copy[1],copy===value);"
            },
            "1 2 false\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "import {Buffer} from 'buffer';const value=Buffer.from([1,2]);const copy:any=structuredClone(value);value[0]=9;console.log(copy[0],copy[1],copy===value);"
            },
            "1 2 false\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const value=new Date(1000);const copy:any=structuredClone(value);value.setTime(9000);console.log(copy.getTime(),copy===value);"
            },
            "1000 false\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const value=/abc/gi;value.lastIndex=3;const copy:any=structuredClone(value);console.log(copy.source,copy.flags,copy.lastIndex);"
            },
            "abc gi 0\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const values:any[]=[new Error('a'),new TypeError('b'),new RangeError('c'),new ReferenceError('d'),new SyntaxError('e'),new URIError('f'),new EvalError('g')];for(const value of values){const copy:any=structuredClone(value);console.log(copy.name,copy.message,copy.stack===value.stack,copy===value);}"
            },
            "Error a true false\nTypeError b true false\nRangeError c true false\nReferenceError d true false\nSyntaxError e true false\nURIError f true false\nEvalError g true false\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "class Value{value=1;}const map=new Map<any,any>();map.set('key',()=>{});const set=new Set<any>([()=>{}]);const values:any[]=[()=>{},Symbol('x'),new Value(),{nested:[()=>{}]},map,set];for(const value of values){try{structuredClone(value);console.log('unexpected');}catch(error:any){console.log(typeof error,error.includes('DataCloneError'));}}"
            },
            "string true\nstring true\nstring true\nstring true\nstring true\nstring true\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const value=new ArrayBuffer(4);const copy:any=structuredClone(value,{transfer:[value]});console.log(value.byteLength,copy.byteLength,copy===value);"
            },
            "4 4 false\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "import {MessageChannel} from 'worker_threads';const channel:any=new MessageChannel();channel.port2.on('messageerror',()=>console.log('error'));channel.port2.on('message',(value:any)=>console.log(value));channel.port1.postMessage({nested:[()=>{}]});channel.port1.postMessage('after');"
            },
            "error\nafter\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const a=new BroadcastChannel('errors');const b=new BroadcastChannel('errors');b.on('messageerror',()=>console.log('listener-error'));b.onmessageerror=()=>console.log('property-error');b.on('message',(event:any)=>console.log(event.data));a.postMessage({nested:[()=>{}]});a.postMessage('after');a.close();b.close();"
            },
            "listener-error\nproperty-error\nafter\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "async function run(){await new Promise<void>(resolve=>setTimeout(resolve,1));const value:any=structuredClone({value:7});console.log(value.value);}run();"
            },
            "7\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "function* values():Generator<any,void,any>{yield structuredClone({value:42});}console.log(values().next().value.value);"
            },
            "42\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.cjs"] = "const value=structuredClone({value:7});console.log(value.value);"
            },
            "7\n", "main.cjs", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "import {Worker,MessageChannel} from 'worker_threads';const {port1,port2}=new MessageChannel();const worker=new Worker(__dirname+'/worker.ts',{workerData:{port:port1},transferList:[port1]});port2.on('message',(value:any)=>{console.log(value.value);port2.close();});port2.postMessage({value:7});",
                ["worker.ts"] = "import {workerData,receiveMessageOnPort} from 'worker_threads';const port:any=workerData.port;const timer=setInterval(()=>{const item:any=receiveMessageOnPort(port);if(item){port.postMessage({value:item.message.value+1});clearInterval(timer);port.close();}},10);"
            },
            "8\n", "main.ts", false
        }
    ];

    [Theory]
    [MemberData(nameof(StructuredCloneMetadataPrograms))]
    public void Isolated_StructuredCloneMetadata_PreservesDataErrorsAndWorkerBoundaries(Dictionary<string, string> files, string expected, string entryPoint, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        foreach (var (path, source) in files) tempDir.CreateFile(path, source);
        var dllPath = tempDir.GetPath("structured_clone_metadata.dll");
        var deployment = standalone ? " --standalone" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --verify{deployment}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> StringCoreMetadataPrograms =>
    [
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const s='Abcd';console.log(s.charAt(1),s.charCodeAt(1),s.toUpperCase(),s.toLowerCase(),s.concat('!','?'));"
            },
            "b 98 ABCD abcd Abcd!?\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const s='abcdef';console.log(s.slice(-3,-1),s.substring(4,1),s.substr(-3,2),s.at(-1),s.charAt(99)==='');console.log(s.substring(NaN,Infinity),s.slice(-Infinity,Infinity));"
            },
            "de bcd de f true\nabcdef abcdef\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const s='ababa';console.log(s.indexOf('ba'),s.indexOf('ba',2),s.lastIndexOf('ba'),s.includes('ab',1),s.startsWith('ba',1),s.endsWith('ba',5));"
            },
            "1 3 3 true true true\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "function examine(s:string,start:number):string{return s.slice(start,s.length-1)+':'+s.substring(start,4)+':'+s.indexOf('b',start)+':'+s.includes('b',start);}console.log(examine('abcdef',1));console.log(examine('ababa',2));"
            },
            "bcde:bcd:1:true\nab:ab:3:true\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const trim:any=String.prototype.trim;const substring:any=String.prototype.substring;const upper:any=String.prototype.toUpperCase;console.log(trim.call(false),substring.call(1234,1,3),upper.call({toString(){return 'abc';}}));"
            },
            "false 23 ABC\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const method:any=String.prototype.substring;const desc:any=Object.getOwnPropertyDescriptor(String.prototype,'substring');const iterator:any=String.prototype[Symbol.iterator];console.log(method.name,method.length,desc.value===method,desc.writable,desc.enumerable,desc.configurable);console.log(String.prototype.constructor===String,iterator.name,iterator.length);"
            },
            "substring 2 true true false true\ntrue [Symbol.iterator] 0\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const saved:any=String.prototype.indexOf;(String.prototype as any).indexOf=function(search:any,position:any){return 42;};const value:string='abc';console.log(value.indexOf('b'));(String.prototype as any).indexOf=saved;console.log(value.indexOf('b'));(String.prototype as any).custom='value';console.log((value as any).custom);delete (String.prototype as any).custom;console.log(typeof (value as any).custom);"
            },
            "42\n1\nvalue\nundefined\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const s='\\uFEFF \\tvalue\\n\\uFEFF';console.log(s.trim()==='value',s.trimStart().startsWith('value'),s.trimEnd().endsWith('value'));"
            },
            "true true true\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "console.log('ab'.repeat(3),'x'.padStart(5,'ab'),'x'.padEnd(5,'ab'),'x'.padStart(2),''.repeat(4)==='');"
            },
            "ababab ababx xabab  x true\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const s='ababa';console.log(s.replace('b','X'),s.replaceAll('a','Q'),s.replace('b',(value:string)=>value.toUpperCase()));"
            },
            "aXaba QbQbQ aBaba\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const s='A\\uD83D\\uDE00B';console.log(s.length,s.codePointAt(1),String.fromCodePoint(128512).length,String.fromCharCode(65,66));console.log('\\uD800\\uDC00'.isWellFormed(),'x\\uD800y'.isWellFormed());const fixed='\\uD800A\\uDC00'.toWellFormed();console.log(fixed.charCodeAt(0),fixed.charCodeAt(2));"
            },
            "4 128512 2 AB\ntrue false\n65533 65533\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "console.log('\\u00e9'.normalize('NFD').length,'e\\u0301'.normalize('NFC').length,'a'.localeCompare('b')<0,'b'.localeCompare('a')>0,'a'.localeCompare('a'));"
            },
            "2 1 true true 0\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const values:string[]=[];for(const part of 'A\\uD83D\\uDE00B')values.push(part.length+':'+part.codePointAt(0));console.log(values.join(','));const method:any=String.prototype[Symbol.iterator];const iterator:any=method.call(123);console.log(iterator.next().value,iterator.next().value,iterator.next().value,iterator.next().done);"
            },
            "1:65,2:128512,1:66\n1 2 3 true\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const methods:any[]=[String.prototype.trim,String.prototype.substring,String.prototype.toUpperCase,String.prototype.valueOf];for(const method of methods){try{method.call(null);console.log('unexpected');}catch(error:any){console.log(error.name);}}try{String.fromCodePoint(-1);}catch(error:any){console.log(error.name);}try{'x'.normalize('invalid');}catch(error:any){console.log(error.name);}"
            },
            "TypeError\nTypeError\nTypeError\nTypeError\nRangeError\nRangeError\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const boxed:any=new String('abc');console.log(boxed.toString(),boxed.valueOf(),boxed.substring(1),String.prototype.valueOf.call(String.prototype)==='');"
            },
            "abc abc bc true\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "console.log('aba'.replaceAll(/a/g,'X'),'aba'.search(/b/),'a,b,c'.split(/,/,2).join('|'));const matches:any='aba'.match(/a/g);console.log(matches.length);"
            },
            "XbX 1 a|b\n2\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "async function run(){await new Promise<void>(resolve=>setTimeout(resolve,1));const value:any=' abc ';console.log(value.trim().substring(1));}run();"
            },
            "bc\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "function* values():Generator<string,void,any>{const value='abcdef';yield value.slice(1,3);yield value.substring(4,2);yield value.toUpperCase();}console.log(Array.from(values()).join(','));"
            },
            "bc,cd,ABCDEF\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.cjs"] = "const method=String.prototype.substring;console.log(method.call('abcdef',1,3),' abc '.trim());"
            },
            "bc abc\n", "main.cjs", true
        }
    ];

    [Theory]
    [MemberData(nameof(StringCoreMetadataPrograms))]
    public void Isolated_StringCoreMetadata_PreservesMethodsPrototypesAndUnicode(Dictionary<string, string> files, string expected, string entryPoint, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        foreach (var (path, source) in files) tempDir.CreateFile(path, source);
        var dllPath = tempDir.GetPath("string_core_metadata.dll");
        var deployment = standalone ? " --standalone" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --verify{deployment}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> TemplateMetadataPrograms =>
    [
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const a:any=12;const b:any=false;console.log(`x${a}:${b}:${null}:${undefined}`);const value:any=42n;console.log(`big=${value}`);"
            },
            "x12:false:null:undefined\nbig=42\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "function tag(strings:any,...values:any[]):string{return strings.join('|')+':'+values.join(',');}console.log(tag`a${1}b${2}c`);console.log(tag`only`);"
            },
            "a|b|c:1,2\nonly:\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "function tag(strings:any):string{return strings[0].length+':'+strings.raw[0].length+':'+strings.raw[0];}console.log(tag`a\\nb`);console.log(String.raw`x\\n${3}\\t`);"
            },
            "3:4:a\\nb\nx\\n3\\t\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const obj:any={prefix:'P',tag(strings:any,...values:any[]){return this.prefix+':'+strings.join('|')+':'+values.join(',');}};console.log(obj.tag`a${7}b`);console.log(obj['tag']`c${8}d`);"
            },
            "P:a|b:7\nundefined:c|d:8\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "function tag(strings:any,...values:any[]){console.log(Object.isFrozen(strings),Object.isFrozen(strings.raw),strings.length,values[0]);return strings.raw.join('|');}console.log(tag`a${5}b`);"
            },
            "true true 2 5\na|b\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "console.log(String.raw({raw:['a','b','c']},1,2));console.log(String.raw({raw:'ABC'},'x','y'));console.log(String.raw({raw:{0:'p',1:'q',length:2}},9));"
            },
            "a1b2c\nAxByC\np9q\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "for(const length of [-1,NaN,0,1,2.8])console.log('['+String.raw({raw:{0:'a',1:'b',length}},7)+']');"
            },
            "[]\n[]\n[]\n[a]\n[a7b]\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "console.log(String.raw({raw:['a','b','c']},1));console.log(String.raw({raw:['a','b']},1,2,3));console.log(String.raw({raw:[1,2]},42n));"
            },
            "a1bc\na1b\n1422\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const raw:any=String.raw;console.log(raw({raw:['a','b']},9));console.log(raw.call(null,{raw:['x','y']},8));const ctor:any=String;console.log(ctor.raw({raw:['p','q']},7));console.log(raw.length,raw.name);"
            },
            "a9b\nx8y\np7q\n1 raw\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "for(const value of [null,undefined,{}, {raw:null},{raw:undefined}]){try{String.raw(value as any);console.log('unexpected');}catch(e:any){console.log(e.name);}}const tag:any=null;try{tag`x`;}catch(e:any){console.log(e.name);}"
            },
            "TypeError\nTypeError\nTypeError\nTypeError\nTypeError\nTypeError\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const symbol:any=Symbol('x');try{console.log(`${symbol}`);}catch(e:any){console.log(e.name);}try{console.log(String.raw({raw:['a','b']},symbol));}catch(e:any){console.log(e.name);}try{console.log(String.raw({raw:[symbol]}));}catch(e:any){console.log(e.name);}function tag(strings:any,...values:any[]){return values[0]===symbol;}console.log(tag`${symbol}`);"
            },
            "TypeError\nTypeError\nTypeError\ntrue\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "let log='';const raw:any={get length(){log+='L';return 2;},get 0(){log+='A';return 'a';},get 1(){log+='B';return 'b';}};const template:any={get raw(){log+='R';return raw;}};const value:any={toString(){log+='S';return 'x';}};console.log(String.raw(template,value),log);"
            },
            "axb RLASB\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "function tag(strings:any,...values:any[]){return strings.join('|')+':'+values.join(',');}async function run(){const value=await Promise.resolve(3);console.log(tag`a${value}b`);console.log(`v=${value}`);}run();"
            },
            "a|b:3\nv=3\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "function tag(strings:any,...values:any[]){return strings.join('|')+':'+values.join(',');}function* run():Generator<string,void,any>{yield tag`a${2}b`;yield `v=${3}`;}console.log(Array.from(run()).join(','));"
            },
            "a|b:2,v=3\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.cjs"] = "function tag(strings,...values){return strings.join('|')+':'+values.join(',');}console.log(tag`a${4}b`);console.log(String.raw({raw:['x','y']},5));"
            },
            "a|b:4\nx5y\n", "main.cjs", true
        }
    ];

    [Theory]
    [MemberData(nameof(TemplateMetadataPrograms))]
    public void Isolated_TemplateMetadata_PreservesRawValuesInvocationAndEvaluationOrder(Dictionary<string, string> files, string expected, string entryPoint, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        foreach (var (path, source) in files) tempDir.CreateFile(path, source);
        var dllPath = tempDir.GetPath("template_metadata.dll");
        var deployment = standalone ? " --standalone" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --verify{deployment}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> StringCoercionMetadataPrograms =>
    [
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "console.log(String(null),String(undefined),String(false),String(-0),String(NaN),String(Infinity));"
            },
            "null undefined false 0 NaN Infinity\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const values:any[]=[0.1+0.2,1e21,1e20,1e-6,1e-7];for(const value of values)console.log(String(value),`${value}`);"
            },
            "0.30000000000000004 0.30000000000000004\n1e+21 1e+21\n100000000000000000000 100000000000000000000\n0.000001 0.000001\n1e-7 1e-7\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const value:any=42n;console.log(value);console.log(String(value),`${value}`,'v='+value);console.log(String(Object(value)));"
            },
            "42n\n42 42 v=42\n42\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const value:any=Symbol('x');console.log(String(value));try{console.log(`${value}`);}catch(e:any){console.log(e.name);}try{console.log(''+value);}catch(e:any){console.log(e.name);}try{console.log(String(Object(value)));}catch(e:any){console.log(e.name);}"
            },
            "Symbol(x)\nTypeError\nTypeError\nTypeError\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const values:number[]=[1,2,3];console.log(String(values),`${values}`);console.log(values.length,values[1]);const holes:any[]=[1,,3];console.log(String(holes));"
            },
            "1,2,3 1,2,3\n3 2\n1,,3\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const saved:any=Array.prototype.toString;(Array.prototype as any).toString=function(){return 'override';};const values:any=[1,2];console.log(String(values),`${values}`);(Array.prototype as any).toString=saved;console.log(String(values));"
            },
            "override override\n1,2\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const first:any={toString(){return 'own';},valueOf(){return 99;}};console.log(String(first),`${first}`);const second:any={toString(){return {};},valueOf(){return 7;}};console.log(String(second));const third:any={toString:null,valueOf(){return true;}};console.log(String(third));"
            },
            "own own\n7\ntrue\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "class Plain{}class Custom{toString(){return 'custom';}}const plain:any=new Plain();const custom:any=new Custom();console.log(String(plain),`${plain}`);console.log(String(custom),`${custom}`,'v='+custom);"
            },
            "[object Object] [object Object]\ncustom custom v=custom\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "for(const value of [new String('abc'),new Number(7),new Boolean(false)])console.log(String(value),`${value}`);const box:any=new String('abc');box.toString=function(){return 'own';};console.log(String(box));"
            },
            "abc abc\n7 7\nfalse false\nown\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "let order='';const value:any={};Object.defineProperty(value,Symbol.toPrimitive,{get(){order+='G';return function(hint:any){order+=hint;return 12;};}});console.log(String(value),order);"
            },
            "12 Gstring\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "for(const value of [{[Symbol.toPrimitive]:1},{[Symbol.toPrimitive](){return {};}}]){try{console.log(String(value));}catch(e:any){console.log(e.name);}}const value:any={[Symbol.toPrimitive](){throw 'sentinel';}};try{String(value);}catch(e){console.log(e);}"
            },
            "TypeError\nTypeError\nsentinel\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const values:any[]=[{toString:undefined,valueOf:undefined},{toString(){return {};},valueOf(){return {};}},Object.create(null)];for(const value of values){try{console.log(String(value));}catch(e:any){console.log(e.name);}}"
            },
            "TypeError\nTypeError\nTypeError\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "for(const result of [null,undefined,false,42n]){const value:any={toString(){return result;}};console.log(String(value));}"
            },
            "null\nundefined\nfalse\n42\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "var toString=function(){return 'GLOBAL';};console.log(String(this));"
            },
            "GLOBAL\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "function inspect(a:any,b:any){console.log(String(arguments));}inspect(1,2);"
            },
            "[object Arguments]\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const values=new Map<any,any>([['a',2],[null,undefined]]);for(const value of values)console.log(String(value));"
            },
            "a,2\n,\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const value:any=function(){};value.toString=function(){return 'FUNCTION';};console.log(String(value));"
            },
            "FUNCTION\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const value:any=/a/g;console.log(String(value));value.toString=function(){return 'REGEXP';};console.log(String(value));console.log(String(new TypeError('message')));"
            },
            "/a/g\nREGEXP\nTypeError: message\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "function render(n:number):string{let result='';for(let i=0;i<n;i++){result+='['+i+']';result+=i+';';}return result;}console.log(render(4));console.log('v='+-42,42+'=v','v='+-0);"
            },
            "[0]0;[1]1;[2]2;[3]3;\nv=-42 42=v v=0\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "async function run(){const value:any=await Promise.resolve({toString(){return 'async';}});console.log(String(value),`${value}`);}run();"
            },
            "async async\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "function* run():Generator<string,void,any>{const value:any={toString(){return 'generator';}};yield String(value);yield `${value}`;}console.log(Array.from(run()).join(','));"
            },
            "generator,generator\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.cjs"] = "const value={toString(){return 'commonjs';}};console.log(String(value),`${value}`);console.log(String(Symbol('x')));"
            },
            "commonjs commonjs\nSymbol(x)\n", "main.cjs", true
        }
    ];

    [Theory]
    [MemberData(nameof(StringCoercionMetadataPrograms))]
    public void Isolated_StringCoercionMetadata_PreservesDisplayLanguageAndPrimitiveConversions(Dictionary<string, string> files, string expected, string entryPoint, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        foreach (var (path, source) in files) tempDir.CreateFile(path, source);
        var dllPath = tempDir.GetPath("string_coercion_metadata.dll");
        var deployment = standalone ? " --standalone" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --verify{deployment}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> BoxedPrimitiveMetadataPrograms =>
    [
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const n:any=new Number(7);const b:any=new Boolean(false);const s:any=new String('hi');console.log(typeof n,n instanceof Number,n instanceof Object,n.valueOf());console.log(typeof b,b instanceof Boolean,b.valueOf());console.log(typeof s,s instanceof String,s.valueOf());"
            },
            "object true true 7\nobject true false\nobject true hi\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const values:any[]=[true,7,'hi'];for(const v of values){const box:any=Object(v);console.log(typeof box,box.valueOf()===v,Object(box)===box);}console.log(typeof Object(null),Object.keys(Object(undefined)).length);const a:any=[];const o:any={x:1};console.log(Object(a)===a,new Object(o)===o);"
            },
            "object true true\nobject true true\nobject true true\nobject 0\ntrue true\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const box:any=Object(42n);console.log(typeof box,box instanceof BigInt,box.valueOf()===42n,box+1n,box==42n);"
            },
            "object true true 43n true\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const symbol:any=Symbol('x');const box:any=Object(symbol);console.log(typeof box,box!==symbol,box instanceof Symbol,box.valueOf()===symbol,Object(box)===box);"
            },
            "object true true true true\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const box:any=new String('hi');for(const key of ['length','0']){const d:any=Object.getOwnPropertyDescriptor(box,key);console.log(d.value,d.writable,d.enumerable,d.configurable);}console.log(box.length,box[0],box[1],Object.keys(box).join(','));console.log(Reflect.set(box,'0','x'),Reflect.deleteProperty(box,'0'),box[0]);"
            },
            "2 false false false\nh false true false\n2 h i 0,1\nfalse false h\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "console.log(Object.getPrototypeOf(new Number(1))===Number.prototype,Object.getPrototypeOf(new Boolean(false))===Boolean.prototype,Object.getPrototypeOf(new String('a'))===String.prototype);console.log(Object.getPrototypeOf(Object(1n))===BigInt.prototype,Object.getPrototypeOf(Object(Symbol('s')))===Symbol.prototype);"
            },
            "true true true\ntrue true\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const box:any=new Number(1);box.valueOf=function(){return 9;};box.toString=function(){return 'own';};console.log(box+2,box==9,String(box));const s:any=new String('base');s.valueOf=function(){return 'value';};console.log(s+'!');"
            },
            "11 true own\nvalue!\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "let order='';const left:any={valueOf(){order+='L';return 2;}};const right:any={valueOf(){order+='R';return 3;}};console.log(left+right,order);order='';console.log(left==left,left==null,left==undefined,order);"
            },
            "5 LR\ntrue false false \n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "let hints='';const box:any=new Number(1);box[Symbol.toPrimitive]=function(hint:any){hints+=hint+';';return 4;};console.log(box+1,box==4,String(box),hints);"
            },
            "5 true 1 default;default;\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "let order='';const value:any={};Object.defineProperty(value,Symbol.toPrimitive,{get(){order+='G';return function(hint:any){order+=hint;return 5;};}});console.log(value+1,order);"
            },
            "6 Gdefault\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const values:any[]=[{[Symbol.toPrimitive]:1},{[Symbol.toPrimitive](){return {}; }},{valueOf(){return {};},toString(){return {};}}];for(const value of values){try{console.log(value+1);}catch(e:any){console.log(e.name);}}const value:any={valueOf(){throw 'sentinel';}};try{console.log(value+1);}catch(e){console.log(e);}"
            },
            "TypeError\nTypeError\nTypeError\nsentinel\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const value:any={valueOf(){return {};},toString(){return 7;}};console.log(value+2,value==7);const proto:any={valueOf(){return 8;}};console.log(Object.create(proto)+1);const noncallable:any={valueOf:0,toString(){return 'fallback';}};console.log(noncallable+1);"
            },
            "9 true\n9\nfallback1\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const date:any=new Date(0);console.log(date+''===date.toString());date[Symbol.toPrimitive]=function(hint:any){return hint;};console.log(date+1);"
            },
            "true\ndefault1\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const box:any=new String('abc');console.log(box.slice(1),String.prototype.slice.call(box,1),String.prototype.slice.call(123,1));try{String.prototype.slice.call(null,1);}catch(e:any){console.log(e.name);}"
            },
            "bc bc 23\nTypeError\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "for(const source of ['new Number(7)','new Boolean(false)',\"new String('hi')\"]){const value:any=eval(source);console.log(typeof value,value.valueOf());}const value:any=eval('var x=1;');console.log(typeof value,!!value,value===undefined);"
            },
            "object 7\nobject false\nobject hi\nundefined false true\n", "main.ts", false
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const box:any=new Number(4);async function run(){await Promise.resolve(0);console.log(box+2);}run();"
            },
            "6\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "function* run():Generator<any,void,any>{yield new Number(3);yield new String('x');}for(const box of run())console.log(box+1);"
            },
            "4\nx1\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.cjs"] = "const box=new Number(8);console.log(box+1,box instanceof Number);console.log(new String('hi').slice(1));"
            },
            "9 true\ni\n", "main.cjs", true
        }
    ];

    [Theory]
    [MemberData(nameof(BoxedPrimitiveMetadataPrograms))]
    public void Isolated_BoxedPrimitiveMetadata_PreservesWrappersAndDefaultHintConversions(Dictionary<string, string> files, string expected, string entryPoint, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        foreach (var (path, source) in files) tempDir.CreateFile(path, source);
        var dllPath = tempDir.GetPath("boxed_primitive_metadata.dll");
        var deployment = standalone ? " --standalone" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --verify{deployment}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> NumberMetadataPrograms =>
    [
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "function parse(value:string){return parseInt(value,10);}for(const s of ['42tail','-0','9007199254740995','18446744073709551616','  +17','x'])console.log(parse(s));console.log(Object.is(parse('-0'),-0));"
            },
            "42\n0\n9007199254740996\n18446744073709552000\n17\nNaN\ntrue\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "function parse(value:string,radix:number){return parseInt(value,radix);}console.log(parse('0xff',16),parse('10101',2),parse('zz',36),parse('-11',8),parse('10',1),parse('0X10',0));"
            },
            "0 21 1295 -9 NaN 16\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "let order='';const input:any={toString(){order+='S';return '42tail';}};const parse:any=parseInt;console.log(parse(input,10),order);"
            },
            "42 S\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "console.log(parseFloat('42.5tail'),Number.parseFloat(' -.25e2tail'),parseFloat('x'),parseFloat('1.2.3'));"
            },
            "42.5 -25 NaN 1.2\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "for(const value of [NaN,Infinity,1,1.5,'1',null])console.log(Number.isNaN(value),Number.isFinite(value),Number.isInteger(value),Number.isSafeInteger(value));console.log(Number.isSafeInteger(9007199254740991),Number.isSafeInteger(9007199254740992));"
            },
            "true false false false\nfalse false false false\nfalse true true true\nfalse true false false\nfalse false false false\nfalse false false false\ntrue false\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const finite:any=isFinite;const nan:any=isNaN;console.log(finite('42'),finite('x'),finite(false),nan('42'),nan('x'),nan(false));"
            },
            "false false false false false false\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "console.log((2.5).toFixed(0),(1.005).toFixed(2),(-0).toFixed(2),(0.125).toFixed(2),(1e21).toFixed(2));console.log((-0.0001).toFixed(2));"
            },
            "3 1.00 0.00 0.13 1e+21\n-0.00\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "console.log((0.1).toFixed(20));console.log((123.456).toFixed(100).length);console.log((Number.MIN_VALUE).toFixed(100).length);"
            },
            "0.10000000000000000555\n104\n102\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "console.log((100).toPrecision(2),(0).toPrecision(3),(12.34).toPrecision(3),(0.0000001).toPrecision(2));"
            },
            "1.0e+2 0.00 12.3 1.0e-7\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "console.log((25).toExponential(0),(12345).toExponential(3),(-0).toExponential(2));console.log((123).toExponential(null),(123).toExponential(undefined));"
            },
            "3e+1 1.235e+4 0.00e+0\n1e+2 1.23e+2\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "for(const value of [1e20,1e21,1e-6,1e-7,0.1+0.2,-0,NaN,-Infinity])console.log(String(value),`${value}`);"
            },
            "100000000000000000000 100000000000000000000\n1e+21 1e+21\n0.000001 0.000001\n1e-7 1e-7\n0.30000000000000004 0.30000000000000004\n0 0\nNaN NaN\n-Infinity -Infinity\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "console.log((255).toString(16),(-10).toString(2),(35).toString(36),(1e20).toString(10));"
            },
            "ff -1010 z 100000000000000000000\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const value:any=new Number(12.5);console.log(value.toFixed(1),value.toPrecision(3),value.toExponential(1),value.valueOf());console.log(Number.prototype.valueOf(),Number.prototype.toFixed(2));"
            },
            "12.5 12.5 1.3e+1 12.5\n0 0.00\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "console.log(Number.prototype.constructor===Number,Object.getPrototypeOf(Number.prototype)===Object.prototype);const d:any=Object.getOwnPropertyDescriptor(Number.prototype,'toFixed');console.log(d.writable,d.enumerable,d.configurable,d.value===Number.prototype.toFixed,d.value.name,d.value.length);"
            },
            "true true\ntrue false true true toFixed 1\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const saved:any=Number.prototype.toFixed;(Number.prototype as any).toFixed=function(){return 'override';};const n:any=12.5;console.log(n.toFixed(2));Number.prototype.toFixed=saved;console.log(n.toFixed(2));"
            },
            "override\n12.50\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "try{Number.prototype.toFixed.call({},2);}catch(e:any){console.log(e.name);}try{(1).toFixed(101);}catch(e:any){console.log(e.name);}try{(1).toPrecision(0);}catch(e:any){console.log(e.name);}try{(1).toString(37);}catch(e:any){console.log(e.name);}console.log((Infinity).toPrecision(101));"
            },
            "TypeError\nRangeError\nRangeError\nRangeError\nInfinity\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "let calls=0;const digits:any={valueOf(){calls++;return 2;}};console.log((12.5).toFixed(digits),calls);try{(NaN).toExponential(Symbol('x') as any);}catch(e:any){console.log(e.name);}"
            },
            "12.50 1\nTypeError\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const parse:any=Number.parseInt;const finite:any=Number.isFinite;const n:any=12.5;const fixed:any=n.toFixed;console.log(parse('42',10),finite(42),fixed.call(n,1));"
            },
            "42 true 12.5\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "async function run(){const value:any=await Promise.resolve(12.5);console.log(value.toFixed(1));}run();"
            },
            "12.5\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "function* run():Generator<string,void,any>{const value:any=12.5;yield value.toFixed(1);yield value.toString(10);}console.log(Array.from(run()).join(','));"
            },
            "12.5,12.5\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.cjs"] = "console.log(Number.parseInt('42',10),(12.5).toFixed(1),Number.isInteger(42));"
            },
            "42 12.5 true\n", "main.cjs", true
        }
    ];

    [Theory]
    [MemberData(nameof(NumberMetadataPrograms))]
    public void Isolated_NumberMetadata_PreservesParsingFormattingAndPrototypeBehavior(Dictionary<string, string> files, string expected, string entryPoint, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        foreach (var (path, source) in files) tempDir.CreateFile(path, source);
        var dllPath = tempDir.GetPath("number_metadata.dll");
        var deployment = standalone ? " --standalone" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --verify{deployment}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> MathMetadataPrograms =>
    [
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const m:any=Math;console.log(m.floor('2.8'),m.ceil(-2.8),m.abs(-4),m.sqrt(9),m.trunc(-2.8),m.sin(0),m.cos(0),m.tan(0),m.log(1),m.exp(0));"
            },
            "2 -2 4 3 -2 0 1 0 0 1\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const m:any=Math;console.log(m.asin(0),m.acos(1),m.atan(0),m.atan2(0,1),m.sinh(0),m.cosh(0),m.tanh(0),m.asinh(0),m.acosh(1),m.atanh(0),m.cbrt(8),m.log10(100),m.log2(8),m.log1p(0),m.expm1(0));"
            },
            "0 0 0 0 0 1 0 0 0 0 2 2 3 0 0\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const f:any=Math.round;console.log(f(0.5),f(-0.5),f(-1.5),f(4503599627370497),f(NaN),f(Infinity));console.log(Object.is(f(-0),-0),Object.is(f(-0.25),-0));"
            },
            "1 0 -1 4503599627370497 NaN Infinity\ntrue true\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const sign:any=Math.sign;const pow:any=Math.pow;console.log(sign(-2),sign(0),sign(3),sign(NaN),Object.is(sign(-0),-0));console.log(pow('2','3'),pow(NaN,0),pow(-1,0.5));"
            },
            "-1 0 1 NaN false\n8 1 NaN\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const m:any=Math;console.log(m.clz32(0),m.clz32(1),m.clz32(-1),m.clz32(Infinity),m.imul(4294967295,5),m.imul(4294967296,7),m.imul('7','6'));"
            },
            "32 31 0 32 -5 0 42\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const m:any=Math;console.log(m.fround(1.5),m.f16round(1.5),m.fround(Infinity),m.f16round(65520),Object.is(m.f16round(-0),-0));console.log(m.hypot(3,4),m.hypot(NaN,Infinity),m.hypot());"
            },
            "1.5 1.5 Infinity Infinity true\n5 Infinity 0\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const m:any=Math;console.log(m.max(),m.min(),m.max(1,9,3),m.min(1,-2,3),m.max(1,NaN));console.log(Object.is(m.max(-0,0),-0),Object.is(m.min(0,-0),-0));"
            },
            "-Infinity Infinity 9 -2 NaN\ntrue false\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "let order='';const a:any={valueOf(){order+='a';return 2;}};const b:any={valueOf(){order+='b';return 3;}};const pow:any=Math.pow;console.log(pow(a,b),order);const floor:any=Math.floor;console.log(floor(null),floor(undefined),floor('2.9'));"
            },
            "8 ab\n0 NaN 2\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const values=[3,9,1];console.log(Math.max(...values),Math.min(...values),Math.hypot(...[3,4]));const max:any=Math.max;console.log(max.apply(null,values),max.call(null,1,5,2));"
            },
            "9 1 5\n9 5\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const m:any=Math;console.log(m===globalThis.Math,m.floor===Math.floor,m.random===Math.random,m.sumPrecise===Math.sumPrecise);console.log(m.floor.name,m.floor.length,m.random.length,m.pow.length,m.hypot.length);"
            },
            "false true true true\nfloor 1 0 2 2\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const d:any=Object.getOwnPropertyDescriptor(Math,'sumPrecise');console.log(d.value===Math.sumPrecise,d.writable,d.enumerable,d.configurable);const c:any=Object.getOwnPropertyDescriptor(Math,'PI');console.log(c.value===Math.PI,c.writable,c.enumerable,c.configurable);console.log(Object.keys(Math).length);"
            },
            "true true false true\ntrue false false false\n0\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "console.log(Object.prototype.toString.call(Math));const d:any=Object.getOwnPropertyDescriptor(Math,Symbol.toStringTag);console.log(d.value,d.writable,d.enumerable,d.configurable);"
            },
            "[object Math]\nMath false false true\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const m:any=Math;const saved=m.floor;m.floor=function(){return 99;};console.log(m.floor(2.5));m.floor=saved;console.log(m.floor(2.5));m.extra=7;console.log(Object.keys(m).join(','),Object.values(m)[0]);"
            },
            "99\n2\nextra 7\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const m:any=Math;const random:any=Math.random;let valid=true;for(let i=0;i<64;i++){const n=m.random();const k=random();if(!(n>=0&&n<1&&k>=0&&k<1))valid=false;}console.log(valid,m.random===random);"
            },
            "true true\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const sum:any=Math.sumPrecise;console.log(sum([1e30,0.1,-1e30]),sum([1,2,3]),sum([Number.MIN_VALUE,Number.MIN_VALUE]));console.log(Object.is(sum([]),-0),Object.is(sum([-0,-0]),-0),Object.is(sum([-0,0]),0));"
            },
            "0.1 6 1e-323\ntrue true true\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const sum:any=Math.sumPrecise;console.log(sum([Infinity,1]),sum([-Infinity,1]),sum([Infinity,-Infinity]),sum([NaN,1]),sum([Number.MAX_VALUE,Number.MAX_VALUE]));"
            },
            "0 -Infinity NaN 0 Infinity\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const values:any=[100];values[Symbol.iterator]=function(){let i=0;return {next(){return i++<3?{value:2,done:false}:{done:true};}};};console.log(Math.sumPrecise(values));"
            },
            "6\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "let closed=0;const value:any={[Symbol.iterator](){return {next(){return {value:'bad',done:false};},return(){closed++;return {done:true};}};}};try{Math.sumPrecise(value);}catch(e:any){console.log(e.name);}console.log(closed);"
            },
            "TypeError\n1\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const m:any=Math;try{m.floor(Symbol('x'));}catch(e:any){console.log(e.name);}try{m.sumPrecise([1,'2']);}catch(e:any){console.log(e.name);}try{m.sumPrecise(42);}catch(e:any){console.log(e.name);}"
            },
            "TypeError\nTypeError\nTypeError\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const j:any=JSON;const r:any=Reflect;console.log(j.stringify({x:1}),j.parse('{\"y\":2}').y,r.get({z:3},'z'));console.log(j.stringify===JSON.stringify,r.get===Reflect.get,Object.keys(j).length,Object.keys(r).length);"
            },
            "{\"x\":1} 2 3\ntrue true 0 0\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "const floor:any=Math.floor;async function run(){const n=await Promise.resolve(2.5);console.log(floor(n));}run();"
            },
            "2\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = "function* values():Generator<number,void,any>{yield 1e30;yield 0.1;yield -1e30;}console.log(Math.sumPrecise(values()));"
            },
            "0.1\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.cjs"] = "const m=Math;console.log(m.floor(2.5),m.sumPrecise([1,2]),m.max(1,3));"
            },
            "2 3 3\n", "main.cjs", true
        }
    ];

    [Theory]
    [MemberData(nameof(MathMetadataPrograms))]
    public void Isolated_MathMetadata_PreservesAdaptersSummationAndSingletonBehavior(Dictionary<string, string> files, string expected, string entryPoint, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        foreach (var (path, source) in files) tempDir.CreateFile(path, source);
        var dllPath = tempDir.GetPath("math_metadata.dll");
        var deployment = standalone ? " --standalone" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --verify{deployment}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> BigIntMetadataPrograms =>
    [
        new object[]
        {
            "arithmetic", "let a=7n;let b=3n;console.log(a+b,a-b,a*b,a/b,-a/b,a%b,-a%b,a**b,-a);",
            "10n 4n 21n 2n -2n 1n -1n 343n -7n\n", "main.ts"
        },
        new object[]
        {
            "bitwise", "let a=10n;let b=6n;console.log(a&b,a|b,a^b,~a,a<<3n,a>>1n,(-a)>>1n);",
            "2n 14n 12n -11n 80n 5n -5n\n", "main.ts"
        },
        new object[]
        {
            "comparisons", "let a=7n;let b=3n;console.log(a===b,a!==b,a>b,a>=b,a<b,a<=b,a===7n);",
            "false true true true false false true\n", "main.ts"
        },
        new object[]
        {
            "loose", "console.log(10n==10,10n==10.5,10n=='10',10n=='bad',0n=='',1n==true,0n==false);console.log(10n==Infinity,10n==NaN,10n===10);",
            "true false true false true true true\nfalse false false\n", "main.ts"
        },
        new object[]
        {
            "callable", "console.log(BigInt(42),BigInt(-0),BigInt(true),BigInt(false),BigInt('  -123 '),BigInt(''),BigInt('0xff'),BigInt('0B101'),BigInt('0o17'));",
            "42n 0n 1n 0n -123n 0n 255n 5n 15n\n", "main.ts"
        },
        new object[]
        {
            "coercion", "let order='';const a:any={[Symbol.toPrimitive](hint:any){order+=hint;return '23';}};const b:any={valueOf(){order+='v';return {};},toString(){order+='s';return '17';}};console.log(BigInt(a),BigInt(b),order);console.log(BigInt([] as any),BigInt([10n] as any));",
            "23n 17n numbervs\n0n 10n\n", "main.ts"
        },
        new object[]
        {
            "conversion_errors", "const inputs:any[]=[1.5,NaN,Infinity,'1.2','0b2',null,undefined,Symbol('x')];for(const x of inputs){try{console.log(BigInt(x));}catch(e:any){console.log(e.name);}}",
            "RangeError\nRangeError\nRangeError\nSyntaxError\nSyntaxError\nTypeError\nTypeError\nTypeError\n", "main.ts"
        },
        new object[]
        {
            "static", "console.log(BigInt.asIntN(8,255n),BigInt.asUintN(8,-1n),BigInt.asIntN(0,9n),BigInt.asUintN(NaN,9n));const B:any=BigInt;console.log(B.asIntN(4,'15'),B.asUintN(4,true),B.asIntN===BigInt.asIntN);",
            "-1n 255n 0n 0n\n-1n 1n true\n", "main.ts"
        },
        new object[]
        {
            "static_order", "let order='';const bits:any={valueOf(){order+='b';return 0;}};const value:any={[Symbol.toPrimitive](hint:any){order+=hint;return 3n;}};console.log(BigInt.asIntN(bits,value),order);try{BigInt.asIntN(0,3 as any);}catch(e:any){console.log(e.name);}try{BigInt.asUintN(-1,3n);}catch(e:any){console.log(e.name);}",
            "0n bnumber\nTypeError\nRangeError\n", "main.ts"
        },
        new object[]
        {
            "rounding", "console.log(Number(9007199254740993n)===9007199254740992,Number(9007199254740995n)===9007199254740996,Number(-9007199254740995n)===-9007199254740996);console.log(Number((1n<<1024n)-1n)===Infinity,Number(-((1n<<1024n)-1n))===-Infinity,Number(0n),Number(-1n));",
            "true true true\ntrue true 0 -1\n", "main.ts"
        },
        new object[]
        {
            "radix", "console.log((255n).toString(2),(255n).toString(8),(255n).toString(16),(255n).toString(36),(-255n).toString(16),(0n).toString());console.log((123456789012345678901234567890n).toString(16));try{console.log((10n).toString(1));}catch(e:any){console.log(e.name);}",
            "11111111 377 ff 73 -ff 0\n18ee90ff6c373e0ee4e3f0ad2\nRangeError\n", "main.ts"
        },
        new object[]
        {
            "prototype", "const p:any=BigInt.prototype;const boxed:any=Object(42n);console.log(p.valueOf.call(boxed),p.toString.call(boxed,16),Object.getPrototypeOf(boxed)===p,p.constructor===BigInt);console.log(Object.prototype.toString.call(boxed));",
            "42n 2a true true\n[object BigInt]\n", "main.ts"
        },
        new object[]
        {
            "descriptor", "const p:any=BigInt.prototype;const d:any=Object.getOwnPropertyDescriptor(p,'valueOf');console.log(d.value===p.valueOf,d.writable,d.enumerable,d.configurable,Object.keys(p).length);const tag:any=Object.getOwnPropertyDescriptor(p,Symbol.toStringTag);console.log(tag.value,tag.writable,tag.enumerable,tag.configurable);",
            "true true false true 0\nBigInt false false true\n", "main.ts"
        },
        new object[]
        {
            "brand", "const p:any=BigInt.prototype;try{p.valueOf.call(42);}catch(e:any){console.log(e.name);}try{p.toString.call('42');}catch(e:any){console.log(e.name);}console.log(p.valueOf.call(-7n));",
            "TypeError\nTypeError\n-7n\n", "main.ts"
        },
        new object[]
        {
            "prototype_override", "const p:any=BigInt.prototype;p.extra=9;const boxed:any=Object(1n);console.log(boxed.extra,Object.keys(p).join(','));delete p.extra;console.log(boxed.extra);",
            "9 extra\nundefined\n", "main.ts"
        },
        new object[]
        {
            "dataview", "const view=new DataView(new ArrayBuffer(16));view.setBigInt64(0,-123n,true);view.setBigUint64(8,18446744073709551615n,false);console.log(view.getBigInt64(0,true),view.getBigUint64(8,false));const v:any=view;v.setBigInt64(0,'17',true);console.log(v.getBigInt64(0,true));try{v.setBigInt64(0,2,true);}catch(e:any){console.log(e.name);}",
            "-123n 18446744073709551615n\n17n\nTypeError\n", "main.ts"
        },
        new object[]
        {
            "dataview_only", "const view=new DataView(new ArrayBuffer(8));view.setInt32(0,42,true);console.log(view.getInt32(0,true));",
            "42\n", "main.ts"
        },
        // Preserve the existing compatibility limitation while changing metadata ownership.
        new object[]
        {
            "typedarray", "const buffer=new ArrayBuffer(16);const view=new DataView(buffer);view.setBigInt64(0,-1n,true);view.setBigInt64(8,123n,true);const a=new BigInt64Array(buffer);const b=new BigUint64Array(buffer);console.log(a[0],a[1],a.length,b[0],b[1]);const value:any=a;try{value[0]=1n;}catch(e:any){console.log('assignment failed');}",
            "-1n 123n 2 18446744073709551615n 123n\nassignment failed\n", "main.ts"
        },
        new object[]
        {
            "string_boolean", "console.log(String(42n),`${-7n}`,''+5n);console.log(Boolean(0n),Boolean(-1n),typeof 2n);",
            "42 -7 5\nfalse true bigint\n", "main.ts"
        },
        new object[]
        {
            "clone", "const value:any={n:12345678901234567890n};const copy:any=structuredClone(value);console.log(copy.n,copy!==value,copy.n===value.n);",
            "12345678901234567890n true true\n", "main.ts"
        },
        new object[]
        {
            "async", "async function run(){const n=await Promise.resolve(7);console.log(BigInt(n));}run().catch((e:any)=>console.log(e.name,e.message));",
            "7n\n", "main.ts"
        },
        new object[]
        {
            "generator", "function* values():Generator<bigint,void,any>{yield BigInt('7');yield BigInt('9');}for(const n of values()){console.log(n);}",
            "7n\n9n\n", "main.ts"
        },
        // Preserve the existing compatibility limitation while changing metadata ownership.
        new object[]
        {
            "cjs", "const B=BigInt;console.log(BigInt('123'),B('123'),B.asUintN(8,-1n),(255n).toString(16));",
            "123n null 255n ff\n", "main.cjs"
        },
        new object[]
        {
            "minimal", "const value=1;",
            "", "main.ts"
        },
        // Preserve the existing compatibility limitation while changing metadata ownership.
        new object[]
        {
            "async_static", "async function run(){const n=await Promise.resolve(7n);console.log(BigInt.asIntN(3,n),n+2n);}run().catch((e:any)=>console.log(e.name,e.message));",
            "ReferenceError Undefined variable 'BigInt'.\n", "main.ts"
        },
        // Preserve the existing compatibility limitation while changing metadata ownership.
        new object[]
        {
            "generator_literal", "function* values():Generator<bigint,void,any>{yield 7n;yield 9n;}try{for(const n of values()){console.log(n*2n);}}catch(e:any){console.log('literal generator failed');}",
            "literal generator failed\n", "main.ts"
        },
    ];

    [Theory]
    [MemberData(nameof(BigIntMetadataPrograms))]
    public void Isolated_BigIntMetadata_PreservesConversionsOperatorsAndPrototypeBehavior(string name, string source, string expected, string entryPoint)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile(entryPoint, source);
        var dllPath = tempDir.GetPath($"bigint_{name}.dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --verify --standalone", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> BooleanMetadataPrograms =>
    [
        new object[]
        {
            "falsy", "console.log(Boolean(null),Boolean(undefined),Boolean(false),Boolean(0),Boolean(-0),Boolean(NaN),Boolean(''));",
            "false false false false false false false\n", "main.ts"
        },
        new object[]
        {
            "truthy", "console.log(Boolean(true),Boolean(1),Boolean(-1),Boolean(Infinity),Boolean(-Infinity),Boolean('0'),Boolean([]),Boolean({}),Boolean(()=>0));",
            "true true true true true true true true true\n", "main.ts"
        },
        new object[]
        {
            "bigint", "console.log(Boolean(0n),Boolean(1n),Boolean(-1n));console.log(!0n,!1n);",
            "false true true\ntrue false\n", "main.ts"
        },
        new object[]
        {
            "no_hooks", "let calls=0;const value:any={[Symbol.toPrimitive](){calls++;return false;},valueOf(){calls++;return false;},toString(){calls++;return '';}};console.log(Boolean(value),!value);if(value)console.log('yes');console.log(calls);",
            "true false\nyes\n0\n", "main.ts"
        },
        new object[]
        {
            "dynamic", "const values:any[]=[false,0,-0,NaN,'',null,undefined,true,1,'x',[],{}];for(const value of values)console.log(Boolean(value),!value);",
            "false true\nfalse true\nfalse true\nfalse true\nfalse true\nfalse true\nfalse true\ntrue false\ntrue false\ntrue false\ntrue false\ntrue false\n", "main.ts"
        },
        new object[]
        {
            "control_flow", "let yes=true;let no=false;console.log(yes&&3,no||4,!yes,!no,yes?'yes':'no');let count=0;while(count<2){count++;}console.log(count);",
            "3 4 false true yes\n2\n", "main.ts"
        },
        new object[]
        {
            "short_circuit", "let trace='';function mark(label:string,value:any):any{trace+=label;return value;}console.log(mark('a',0)&&mark('b',1),mark('c','')||mark('d','x'),trace);",
            "0 x acd\n", "main.ts"
        },
        new object[]
        {
            "prototype", "const p:any=Boolean.prototype;console.log(p.valueOf(),p.toString(),p.constructor===Boolean,Object.getPrototypeOf(p)===Object.prototype);",
            "false false true true\n", "main.ts"
        },
        new object[]
        {
            "boxed", "const p:any=Boolean.prototype;const value:any=new Boolean(false);console.log(Boolean(value),p.valueOf.call(value),p.toString.call(value),Object.getPrototypeOf(value)===p);console.log(Object.prototype.toString.call(value));",
            "true false false true\n[object Boolean]\n", "main.ts"
        },
        new object[]
        {
            "descriptor", "const p:any=Boolean.prototype;const d:any=Object.getOwnPropertyDescriptor(p,'valueOf');console.log(d.value===p.valueOf,d.writable,d.enumerable,d.configurable,Object.keys(p).length);console.log(p.valueOf.name,p.valueOf.length,p.toString.name,p.toString.length);",
            "true true false true 0\nvalueOf 0 toString 0\n", "main.ts"
        },
        new object[]
        {
            "brand", "const p:any=Boolean.prototype;for(const value of [1,'',null,Symbol('x')] as any[]){try{console.log(p.valueOf.call(value));}catch(e:any){console.log(e.name);}}console.log(p.toString.call(true),p.valueOf.call(false));",
            "TypeError\nTypeError\nTypeError\nTypeError\ntrue false\n", "main.ts"
        },
        new object[]
        {
            "mutation", "const p:any=Boolean.prototype;p.extra=9;const value:any=Object(false);console.log(value.extra,Object.keys(p).join(','));delete p.extra;console.log(value.extra);",
            "9 extra\nundefined\n", "main.ts"
        },
        new object[]
        {
            "descriptor_flags", "const value:any={};Object.defineProperty(value,'x',{value:1,writable:'yes' as any,enumerable:0 as any,configurable:[] as any});const d:any=Object.getOwnPropertyDescriptor(value,'x');console.log(d.writable,d.enumerable,d.configurable);",
            "true false true\n", "main.ts"
        },
        new object[]
        {
            "regexp", "console.log(/a/.test('cat'),/a/.test('dog'),Boolean(/x/));console.log('aba'.replace(/a/g,'x'));",
            "true false true\nxbx\n", "main.ts"
        },
        new object[]
        {
            "array_callbacks", "const a=[0,1,2];console.log(a.filter((x:number):any=>x).length,a.filter((x:number):any=>({})).length,a.find((x:number):any=>x),a.some((x:number):any=>x),a.every((x:number):any=>x));",
            "2 3 1 true false\n", "main.ts"
        },
        new object[]
        {
            "async", "async function run(){const value=await Promise.resolve(0);console.log(!!value,Boolean(value));}run();",
            "false false\n", "main.ts"
        },
        new object[]
        {
            "generator", "function* values():Generator<boolean,void,any>{yield true;yield false;}for(const value of values())console.log(Boolean(value),!value);",
            "true false\nfalse true\n", "main.ts"
        },
        // Preserve the existing compatibility limitation while changing metadata ownership.
        new object[]
        {
            "cjs", "const B=Boolean;console.log(Boolean(0),Boolean('x'),B(0),B('x'),B===Boolean);",
            "false true null null true\n", "main.cjs"
        },
        new object[]
        {
            "minimal", "const value=1;",
            "", "main.ts"
        },
    ];

    [Theory]
    [MemberData(nameof(BooleanMetadataPrograms))]
    public void Isolated_BooleanMetadata_PreservesTruthinessAndPrototypeBehavior(string name, string source, string expected, string entryPoint)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile(entryPoint, source);
        var dllPath = tempDir.GetPath($"boolean_{name}.dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --verify --standalone", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> NumericCoercionMetadataPrograms =>
    [
        new object[]
        {
            "primitives", "console.log(Number(),Number(undefined),Number(null),Number(false),Number(true),Number(''),Number('  '));",
            "0 NaN 0 0 1 0 0\n", "main.ts"
        },
        new object[]
        {
            "strings", "console.log(Number('0xff'),Number('0b101'),Number('0o17'),Number('1e2'),Number('1.5'),Number('bad'));console.log(1/Number('-0')===-Infinity);",
            "255 5 15 100 1.5 NaN\ntrue\n", "main.ts"
        },
        new object[]
        {
            "arrays", "console.log(Number([]),Number([4]),Number([1,2]),Number([null]),Number([undefined]));",
            "0 4 NaN 0 0\n", "main.ts"
        },
        new object[]
        {
            "boxed", "console.log(Number(Object(3)),Number(Object(false)),Number(Object('7')));",
            "3 0 7\n", "main.ts"
        },
        // Preserve the existing compatibility limitation while changing metadata ownership.
        new object[]
        {
            "exotic", "let trace='';const value:any={[Symbol.toPrimitive](hint:any){trace+=hint;return '9';}};console.log(Number(value),+value,trace);",
            "NaN 9 number\n", "main.ts"
        },
        new object[]
        {
            "ordinary", "let trace='';const value:any={valueOf(){trace+='v';return {};},toString(){trace+='s';return '7';}};console.log(+value,trace);",
            "7 vs\n", "main.ts"
        },
        new object[]
        {
            "errors", "const values:any[]=[Symbol('x'),{[Symbol.toPrimitive]:1},{[Symbol.toPrimitive](){return {};}}];for(const value of values){try{console.log(+value);}catch(e:any){console.log(e.name);}}",
            "TypeError\nTypeError\nTypeError\n", "main.ts"
        },
        new object[]
        {
            "abrupt", "const value:any={valueOf(){throw new RangeError('sentinel');}};try{console.log(+value);}catch(e:any){console.log(e.name,e.message);}",
            "RangeError sentinel\n", "main.ts"
        },
        new object[]
        {
            "bigint", "console.log(Number(1n),Number(9007199254740993n));const value:any=1n;try{console.log(+value);}catch(e:any){console.log(e.name);}try{console.log(Math.abs(value));}catch(e:any){console.log(e.name);}",
            "1 9007199254740992\nTypeError\nTypeError\n", "main.ts"
        },
        new object[]
        {
            "int32", "function typed(value:number):number{return value|0;}function dynamic(value:any):number{return value|0;}for(const value of [1.9,-1.9,4294967295,4294967296,2147483648,NaN,Infinity,-Infinity])console.log(typed(value),dynamic(value));",
            "1 1\n-1 -1\n-1 -1\n0 0\n-2147483648 -2147483648\n0 0\n0 0\n0 0\n", "main.ts"
        },
        new object[]
        {
            "operator_order", "let trace='';const left:any={valueOf(){trace+='l';return 1;}};const right:any={valueOf(){trace+='r';return 2;}};console.log(left|right,trace);",
            "3 lr\n", "main.ts"
        },
        new object[]
        {
            "indices", "const a=[1,2,3];console.log(a.slice(undefined).join(','),a.slice(null as any).join(','),a.slice(Infinity).length,a.slice(-Infinity).length,a.indexOf(2,1.9));",
            "1,2,3 1,2,3 0 3 1\n", "main.ts"
        },
        new object[]
        {
            "index_hook", "let trace='';const index:any={[Symbol.toPrimitive](hint:any){trace+=hint;return 1.9;}};console.log([1,2,3].indexOf(2,index),trace);",
            "1 number\n", "main.ts"
        },
        new object[]
        {
            "regexp", "const re:any=/a/g;re.lastIndex='1';console.log(re.exec('ba')[0],re.lastIndex);",
            "a 2\n", "main.ts"
        },
        new object[]
        {
            "async", "async function run(){const value:any=await Promise.resolve('7');console.log(+value,value|0);}run();",
            "7 7\n", "main.ts"
        },
        new object[]
        {
            "generator", "function* values():Generator<any,void,any>{yield '2';yield '-3';}for(const value of values())console.log(+value,value|0);",
            "2 2\n-3 -3\n", "main.ts"
        },
        // Preserve the existing compatibility limitation while changing metadata ownership.
        new object[]
        {
            "cjs", "const N=Number;console.log(Number('2'),N('2'),N===Number);",
            "2 null true\n", "main.cjs"
        },
        new object[]
        {
            "minimal", "const value=1;",
            "", "main.ts"
        },
    ];

    [Theory]
    [MemberData(nameof(NumericCoercionMetadataPrograms))]
    public void Isolated_NumericCoercionMetadata_PreservesConversionAndCoercionOrder(string name, string source, string expected, string entryPoint)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile(entryPoint, source);
        var dllPath = tempDir.GetPath($"numeric-coercion_{name}.dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{tempDir.GetPath(entryPoint)}\" -o \"{dllPath}\" --verify --standalone", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> ObjectStorageMetadataPrograms =>
    [
        new object[]
        {
            "properties", "const value:any={a:1};value.b=2;console.log(value.a,value.b,value.missing);delete value.a;console.log('a' in value,'b' in value);",
            "1 2 undefined\nfalse true\n", true
        },
        new object[]
        {
            "accessors", "let trace='';const value:any={_value:1,get value(){trace+='g';return this._value;},set value(v:number){trace+='s';this._value=v;}};value.value=7;console.log(value.value,value._value,trace);",
            "7 7 sg\n", true
        },
        new object[]
        {
            "keys", "const value:any={a:1,get b(){return 2;},set c(v:number){}};console.log(Object.keys(value).join(','),Object.getOwnPropertyNames(value).join(','));",
            "a,b,c a,b,c\n", true
        },
        new object[]
        {
            "descriptor", "const value:any={a:1};Object.defineProperty(value,'b',{value:2,writable:false,enumerable:false,configurable:true});const d=Object.getOwnPropertyDescriptor(value,'b')!;console.log(value.b,d.value,d.writable,d.enumerable,d.configurable,Object.keys(value).join(','));",
            "2 2 false false true a\n", true
        },
        new object[]
        {
            "strict_set", "function run(){'use strict';const value:any={};Object.defineProperty(value,'a',{value:1,writable:false});try{value.a=2;}catch(e:any){console.log(e.name);}console.log(value.a);}run();",
            "TypeError\n1\n", true
        },
        new object[]
        {
            "strict_delete", "function run(){'use strict';const value:any={};Object.defineProperty(value,'a',{value:1,configurable:false});try{delete value.a;}catch(e:any){console.log(e.name);}console.log(value.a);}run();",
            "TypeError\n1\n", true
        },
        new object[]
        {
            "freeze", "function run(){'use strict';const value:any={a:1};Object.freeze(value);console.log(Object.isFrozen(value),Object.isSealed(value),Object.isExtensible(value));try{value.a=2;}catch(e:any){console.log(e.name);}console.log(value.a);}run();",
            "true true false\nTypeError\n1\n", true
        },
        new object[]
        {
            "seal", "function run(){'use strict';const value:any={a:1};Object.seal(value);value.a=2;console.log(value.a,Object.isSealed(value),Object.isFrozen(value),Object.isExtensible(value));try{value.b=3;}catch(e:any){console.log(e.name);}}run();",
            "2 true false false\nTypeError\n", true
        },
        new object[]
        {
            "prevent_extensions", "function run(){'use strict';const value:any={a:1};Object.preventExtensions(value);value.a=2;console.log(value.a,Object.isExtensible(value),Object.isSealed(value));try{value.b=3;}catch(e:any){console.log(e.name);}}run();",
            "2 false false\nTypeError\n", true
        },
        // Preserve the existing inherited-accessor limitation while changing storage ownership.
        new object[]
        {
            "prototype", "const proto:any={base:3,get value(){return (this as any).own+this.base;}};const value:any=Object.create(proto);value.own=4;console.log(value.value,'base' in value,Object.hasOwn(value,'base'),Object.getPrototypeOf(value)===proto);",
            "NaN true false true\n", true
        },
        // Preserve the existing inherited-accessor limitation while changing storage ownership.
        new object[]
        {
            "inherited_setter", "const proto:any={set value(v:number){(this as any).own=v;}};const value:any=Object.create(proto);value.value=9;console.log(value.own,Object.hasOwn(value,'own'),Object.hasOwn(proto,'own'));",
            "undefined false false\n", true
        },
        new object[]
        {
            "array_truncation", "const value:any[]=[1,2,3];Object.defineProperty(value,'2',{value:9,configurable:true});value.length=1;console.log(value.length,Object.getOwnPropertyDescriptor(value,'2')===undefined,Object.keys(value).join(','));",
            "1 true 0\n", true
        },
        new object[]
        {
            "compact", "interface Point{x:number;y:number;}function make(x:number):Point{return {x:x,y:2};}const p=make(3);const copy={...p,z:4};console.log(copy.x,copy.y,copy.z,Object.keys(copy).join(','));",
            "3 2 4 x,y,z\n", true
        },
        new object[]
        {
            "symbols", "const key=Symbol('key');const value:any={a:1,[key]:2};console.log(value[key],Object.keys(value).join(','),Object.getOwnPropertySymbols(value)[0]===key);",
            "2 a true\n", true
        },
        // Proxy invocation uses the existing managed runtime deployment.
        new object[]
        {
            "proxy", "const target:any={a:1};let trace='';const value:any=new Proxy(target,{get(t:any,k:any){trace+='g';return t[k];},set(t:any,k:any,v:any){trace+='s';t[k]=v;return true;}});value.a=2;console.log(value.a,target.a,trace);",
            "2 2 sg\n", false
        },
        new object[]
        {
            "async", "async function run(){const value:any=await Promise.resolve({_value:4,get value(){return this._value;}});console.log(value.value);}run();",
            "4\n", true
        },
        new object[]
        {
            "generator", "function* values():Generator<any,void,any>{yield {_value:5,get value(){return this._value;}};}for(const value of values())console.log(value.value);",
            "5\n", true
        },
        new object[]
        {
            "minimal", "const value=1;",
            "", true
        },
    ];

    [Theory]
    [MemberData(nameof(ObjectStorageMetadataPrograms))]
    public void Isolated_ObjectStorageMetadata_PreservesPropertiesAccessorsAndRestrictions(string name, string source, string expected, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"object-storage_{name}.dll");
        var deployment = standalone ? " --standalone" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{dllPath}\" --verify{deployment}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> DescriptorStorageMetadataPrograms =>
    [
        new object[]
        {
            "data", "const value:any={a:1};Object.defineProperty(value,'b',{value:2,writable:false,enumerable:false,configurable:true});const d=Object.getOwnPropertyDescriptor(value,'b')!;console.log(value.b,d.value,d.writable,d.enumerable,d.configurable,Object.keys(value).join(','));",
            "2 2 false false true a\n", true
        },
        new object[]
        {
            "accessors", "let trace='';let stored=1;const value:any={};Object.defineProperty(value,'a',{get(){trace+='g';return stored;},set(v:number){trace+='s';stored=v;},enumerable:true,configurable:true});value.a=7;console.log(value.a,stored,trace,Object.keys(value).join(','));",
            "7 7 sg a\n", true
        },
        new object[]
        {
            "setter_only", "let stored=0;const value:any={};Object.defineProperty(value,'a',{set(v:number){stored=v;},enumerable:true,configurable:true});value.a=8;console.log(value.a,stored,Object.getOwnPropertyDescriptor(value,'a')!.get===undefined);",
            "undefined 8 true\n", true
        },
        new object[]
        {
            "keys", "const value:any={a:1};Object.defineProperty(value,'hidden',{value:2});Object.defineProperty(value,'visible',{get(){return 3;},enumerable:true});console.log(Object.keys(value).join(','),Object.getOwnPropertyNames(value).join(','));",
            "a,visible a,hidden,visible\n", true
        },
        new object[]
        {
            "delete", "const value:any={};Object.defineProperty(value,'a',{value:2,configurable:true});console.log(value.a,delete value.a,Object.getOwnPropertyDescriptor(value,'a')===undefined);",
            "2 true true\n", true
        },
        new object[]
        {
            "redefine", "const value:any={};Object.defineProperty(value,'a',{value:2,writable:true,configurable:true,enumerable:true});Object.defineProperty(value,'a',{get(){return 3;},configurable:true,enumerable:true});console.log(value.a,Object.getOwnPropertyDescriptor(value,'a')!.get!==undefined);Object.defineProperty(value,'a',{value:4,writable:true});value.a=5;console.log(value.a);",
            "3 true\n5\n", true
        },
        new object[]
        {
            "function_keys", "function target(){return 1;}(target as any).a=2;console.log((target as any).a);Object.defineProperty(target,'b',{value:3,configurable:true});console.log((target as any).b,Object.getOwnPropertyDescriptor(target,'b')!.value);",
            "2\n3 3\n", true
        },
        new object[]
        {
            "static_shadow", "class Base{static value:number=1;}class Child extends Base{};(Child as any).value=2;console.log(Base.value,Child.value);Object.defineProperty(Child,'extra',{value:3,configurable:true});console.log((Child as any).extra,(Base as any).extra);",
            "1 2\n3 undefined\n", true
        },
        new object[]
        {
            "prototype_null", "const value:any=Object.create(null);Object.defineProperty(value,'a',{value:2,enumerable:true});console.log(Object.getPrototypeOf(value)===null,value.a,Object.keys(value).join(','));const proto:any={b:3};Object.setPrototypeOf(value,proto);console.log(Object.getPrototypeOf(value)===proto,value.b);",
            "true 2 a\ntrue 3\n", true
        },
        new object[]
        {
            "freeze", "function run(){'use strict';const value:any={a:1};Object.freeze(value);console.log(Object.isFrozen(value),Object.isSealed(value),Object.isExtensible(value));try{value.a=2;}catch(e:any){console.log(e.name);}console.log(value.a);}run();",
            "true true false\nTypeError\n1\n", true
        },
        new object[]
        {
            "seal", "function run(){'use strict';const value:any={a:1};Object.seal(value);value.a=2;console.log(value.a,Object.isSealed(value),Object.isFrozen(value),Object.isExtensible(value));try{value.b=3;}catch(e:any){console.log(e.name);}}run();",
            "2 true false false\nTypeError\n", true
        },
        new object[]
        {
            "prevent_extensions", "function run(){'use strict';const value:any={a:1};Object.preventExtensions(value);value.a=2;console.log(value.a,Object.isExtensible(value),Object.isSealed(value));try{value.b=3;}catch(e:any){console.log(e.name);}}run();",
            "2 false false\nTypeError\n", true
        },
        new object[]
        {
            "array_truncation", "const value:any[]=[1,2,3];Object.defineProperty(value,'2',{value:9,configurable:true});value.length=1;console.log(value.length,Object.getOwnPropertyDescriptor(value,'2')===undefined,Object.keys(value).join(','));",
            "1 true 0\n", true
        },
        new object[]
        {
            "compact", "interface Point{x:number;y:number;}function make(x:number):Point{return {x:x,y:2};}const p=make(3);const copy={...p,z:4};console.log(copy.x,copy.y,copy.z,Object.keys(copy).join(','));",
            "3 2 4 x,y,z\n", true
        },
        new object[]
        {
            "async", "async function run(){const value:any=await Promise.resolve({_value:4,get value(){return this._value;}});console.log(value.value);}run();",
            "4\n", true
        },
        new object[]
        {
            "generator", "function* values():Generator<any,void,any>{yield {_value:5,get value(){return this._value;}};}for(const value of values())console.log(value.value);",
            "5\n", true
        },
        new object[]
        {
            "minimal", "const value=1;",
            "", true
        },
    ];

    [Theory]
    [MemberData(nameof(DescriptorStorageMetadataPrograms))]
    public void Isolated_DescriptorStorageMetadata_PreservesDescriptorsKeysAndRestrictions(string name, string source, string expected, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"descriptor-storage_{name}.dll");
        var deployment = standalone ? " --standalone" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{dllPath}\" --verify{deployment}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> ReflectMetadataPrograms =>
    [
        new object[]
        {
            "has",
            "class Base {\n    baseMethod(): number { return 1; }\n}\nclass Child extends Base {\n    childProp: number = 42;\n}\nlet c: any = new Child();\nconsole.log(Reflect.has(c, \"childProp\"));\nconsole.log(Reflect.has(c, \"baseMethod\"));\nconsole.log(Reflect.has(c, \"missing\"));",
            "true\ntrue\nfalse\n",
            true,
            ""
        },
        new object[]
        {
            "get",
            "let obj: any = { name: \"hello\", value: 42 };\nconsole.log(Reflect.get(obj, \"name\"));\nconsole.log(Reflect.get(obj, \"value\"));",
            "hello\n42\n",
            true,
            ""
        },
        new object[]
        {
            "set",
            "let obj: any = {};\nlet result: boolean = Reflect.set(obj, \"x\", 42);\nconsole.log(result);\nconsole.log(obj.x);",
            "true\n42\n",
            true,
            ""
        },
        new object[]
        {
            "delete_frozen",
            "let obj: any = { x: 1 };\nObject.freeze(obj);\nconsole.log(Reflect.deleteProperty(obj, \"x\"));\nconsole.log(obj.x);",
            "false\n1\n",
            true,
            ""
        },
        new object[]
        {
            "own_keys",
            "let obj: any = { a: 1, b: 2, c: 3 };\nlet keys: any = Reflect.ownKeys(obj);\nconsole.log(keys.length);\nconsole.log(keys[0]);\nconsole.log(keys[1]);\nconsole.log(keys[2]);",
            "3\na\nb\nc\n",
            true,
            ""
        },
        new object[]
        {
            "prototype_validation",
            "const object: any = {};\nObject.preventExtensions(object);\nconsole.log(Reflect.setPrototypeOf(object, Object.prototype));\ntry {\n    Reflect.setPrototypeOf({}, 1 as any);\n} catch (error) {\n    console.log(error instanceof TypeError);\n}",
            "true\ntrue\n",
            true,
            ""
        },
        new object[]
        {
            "define",
            "let obj: any = {};\nlet result: boolean = Reflect.defineProperty(obj, \"x\", { value: 42, writable: true, enumerable: true, configurable: true });\nconsole.log(result);\nconsole.log(obj.x);",
            "true\n42\n",
            true,
            ""
        },
        new object[]
        {
            "apply",
            "function add(a: number, b: number): number {\n    return a + b;\n}\nlet result: any = Reflect.apply(add, undefined, [3, 4]);\nconsole.log(result);",
            "7\n",
            true,
            ""
        },
        new object[]
        {
            "construct",
            "class Point {\n    x: number;\n    y: number;\n    constructor(x: number, y: number) {\n        this.x = x;\n        this.y = y;\n    }\n}\nlet p: any = Reflect.construct(Point, [10, 20]);\nconsole.log(p.x);\nconsole.log(p.y);",
            "10\n20\n",
            true,
            ""
        },
        new object[]
        {
            "aliases",
            "const target: any = { x: 1 };\nconst get: any = Reflect.get;\nconst set: any = Reflect.set;\nconsole.log(typeof Reflect.apply);\nconsole.log(Reflect.apply.name);\nconsole.log(Reflect.apply.length);\nconsole.log(get(target, \"x\"));\nconsole.log(set(target, \"y\", 2));\nconsole.log(target.y);",
            "function\napply\n3\n1\ntrue\n2\n",
            true,
            ""
        },
        new object[]
        {
            "receiver_descriptor",
            "const target: any = {};\nconst receiver: any = {};\nObject.defineProperty(receiver, \"p\", { get() { return 1; } });\nconsole.log(Reflect.set(target, \"p\", 2, receiver));\nconsole.log(receiver.p);\nconsole.log(target.hasOwnProperty(\"p\"));",
            "false\n1\nfalse\n",
            true,
            ""
        },
        new object[]
        {
            "metadata_decorator",
            "@Reflect.metadata(\"role\", \"admin\")\nclass MyClass {}\n\nconsole.log(Reflect.getMetadata(\"role\", MyClass));",
            "null\n",
            true,
            "--experimentalDecorators"
        },
        new object[]
        {
            "proxy_forwarding",
            "const log: string[] = [];\nconst target: any = { x: 1 };\nconst proxy: any = new Proxy(target, {\n    set(t: any, key: string, value: any, receiver: any) {\n        log.push(\"set\");\n        if (receiver !== proxy) throw new Error(\"receiver was not proxy\");\n        return Reflect.set(t, key, value, receiver);\n    },\n    getOwnPropertyDescriptor(t: any, key: string) {\n        log.push(\"getOwnPropertyDescriptor\");\n        return Reflect.getOwnPropertyDescriptor(t, key);\n    },\n    defineProperty(t: any, key: string, descriptor: any) {\n        log.push(\"defineProperty\");\n        return Reflect.defineProperty(t, key, descriptor);\n    }\n});\n\nconsole.log(Object.getOwnPropertyDescriptor(proxy, \"x\").value);\nlog.length = 0;\nReflect.set(proxy, \"x\", 2, proxy);\nconsole.log(log.join(\",\"));\nconsole.log(target.x);",
            "1\nset,getOwnPropertyDescriptor,defineProperty\n2\n",
            false,
            ""
        },
        new object[]
        {
            "metadata",
            "const target:any={};console.log(Reflect.hasMetadata('a',target));Reflect.defineMetadata('a',1,target);Reflect.defineMetadata('b',2,target,'x');console.log(Reflect.getMetadata('a',target),Reflect.getMetadata('b',target,'x'),Reflect.getMetadataKeys(target).join(','));console.log(Reflect.deleteMetadata('a',target),Reflect.hasMetadata('a',target),Reflect.getMetadata('b',target,'x'));",
            "false\n1 2 a\ntrue false 2\n",
            true,
            ""
        },
        new object[]
        {
            "proxy_only",
            "const target:any={x:1};const p:any=new Proxy(target,{});p.x=4;console.log(p.x,target.x);",
            "4 4\n",
            false,
            ""
        },
        new object[]
        {
            "mutable_namespace",
            "const ns:any=Reflect;const original=ns.get;ns.get=function(){return 9;};const alias:any=Reflect.get;console.log(ns.get({},'x'),alias({},'x'));ns.get=original;console.log(ns.get({x:3},'x'));delete ns.get;const removed:any=Reflect.get;console.log(typeof removed);",
            "9 9\n3\nundefined\n",
            true,
            ""
        },
        new object[]
        {
            "receiver",
            "const target:any={get x(){return (this as any).marker;},set x(v:number){(this as any).marker=v;}};const receiver:any={marker:7};console.log(Reflect.get(target,'x',receiver));console.log(Reflect.set(target,'x',9,receiver),receiver.marker,target.marker);",
            "7\ntrue 9 undefined\n",
            true,
            ""
        },
        new object[]
        {
            "minimal",
            "const value=1;",
            "",
            true,
            ""
        },
    ];

    [Theory]
    [MemberData(nameof(ReflectMetadataPrograms))]
    public void Isolated_ReflectMetadata_PreservesOperationsReceiversMetadataAndProxyDeployment(
        string name, string source, string expected, bool standalone, string options)
    {
        // The CLI decorator case preserves the baseline null output; direct
        // metadata and native decorator closure behavior have separate coverage.
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"reflect-metadata_{name}.dll");
        var deployment = standalone ? " --standalone" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{dllPath}\" --verify {options}{deployment}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        if (standalone) Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> SuperMethodPrograms =>
    [
        new object[]
        {
            "super_direct",
            "class A { value(x:number){return \"A\"+x;} } class B extends A { value(x:number){return super.value(x)+\"B\";} } console.log(new B().value(3),new A().value(4));\n",
            "A3B A4\n",
            false,
            "",
        },
        new object[]
        {
            "super_receiver",
            "class A { label=\"parent\";value(){return this.label;} } class B extends A {label=\"child\";read(){return super.value();}} console.log(new B().read());\n",
            "child\n",
            false,
            "",
        },
        new object[]
        {
            "super_arguments",
            "let log=\"\";function arg(n:number){log+=n;return n;}class A {sum(a:number,b:number){return a+b;}}class B extends A {read(){return super.sum(arg(1),arg(2));}}console.log(new B().read(),log);\n",
            "3 12\n",
            false,
            "",
        },
        new object[]
        {
            "super_method_value",
            "class A {value(x:number){return x+2;}} class B extends A {read(){const fn=super.value;return fn(4);}}console.log(new B().read());\n",
            "6\n",
            false,
            "",
        },
        new object[]
        {
            "super_optional_features",
            "class A {value(){return 5;}}class B extends A {read(){return super.value();}}console.log(new B().read(),new Set([1,2]).size,Buffer.from(\"ok\").toString());\n",
            "5 2 ok\n",
            false,
            "",
        },
        new object[]
        {
            "super_async_value_control",
            "class A {value(x:number){return x+2;}}class B extends A {async read(){const first=super.value;await Promise.resolve(0);const second=super.value;return first(3)+second(4);}}new B().read().then(v=>console.log(v));\n",
            "11\n",
            false,
            "",
        },
        new object[]
        {
            "super_generator_value_control",
            "class A {value(x:number){return x+2;}}class B extends A {*read(){const first=super.value;yield first(1);const second=super.value;yield second(2);return second(3);}}const g=new B().read();console.log(g.next().value,g.next().value,g.next().value);\n",
            "3 4 5\n",
            false,
            "",
        },
        new object[]
        {
            "super_async_generator_value_control",
            "class A {value(x:number){return x+2;}}class B extends A {async *read(){await Promise.resolve(0);const method=super.value;yield method(1);yield method(2);}}(async()=>{for await(const v of new B().read())console.log(v);})();\n",
            "3\n4\n",
            false,
            "",
        },
        new object[]
        {
            "super_declared_parent_control",
            "class A {value(){return \"grandparent\";}}class B extends A {value(){return super.value();}}class C extends B {read(){return super.value();}}console.log(new C().read());\n",
            "grandparent\n",
            false,
            "",
        },
    ];

    [Theory]
    [MemberData(nameof(SuperMethodPrograms))]
    public void Isolated_SuperMethods_PreserveParentLookupAndCapturedReceivers(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"super_method_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> ArrayDestructuringPrograms =>
    [
        new object[]
        {
            "destructure_array_tuple",
            "const [a,,b=9,...rest]=[1,2];const tuple:[number,string]=[4,\"x\"];const [n,s]=tuple;console.log(a,b,rest.length,n,s);\n",
            "1 9 0 4 x\n",
            false,
            "",
        },
        new object[]
        {
            "destructure_dynamic_array",
            "const values:any=[2,3,4];const [first,...rest]=values;console.log(first,rest.join(\",\"),rest===values);\n",
            "2 3,4 false\n",
            false,
            "",
        },
        new object[]
        {
            "destructure_string",
            "const [a,...rest]=\"abc\";console.log(a,Array.isArray(rest),rest.join(\",\"));\n",
            "a true b,c\n",
            false,
            "",
        },
        new object[]
        {
            "destructure_unicode_units",
            "const [a,b,...rest]=\"A\\u{1F600}B\";console.log(a,b.length,b.charCodeAt(0),b.charCodeAt(1),rest.join(\",\"));\n",
            "A 2 55357 56832 B\n",
            false,
            "",
        },
        new object[]
        {
            "destructure_set",
            "const [a,b=8,...rest]=new Set([2,3,4]);console.log(a,b,rest.join(\",\"));\n",
            "2 3 4\n",
            false,
            "",
        },
        new object[]
        {
            "destructure_map",
            "const [[key,value],...rest]=new Map([[\"a\",1],[\"b\",2]]);console.log(key,value,rest.length,rest[0][0],rest[0][1]);\n",
            "a 1 1 b 2\n",
            false,
            "",
        },
        new object[]
        {
            "destructure_generator_rest",
            "function* g(){yield 2;yield 4;yield 6;}const [a,,...rest]=g();console.log(a,rest.join(\",\"));\n",
            "2 6\n",
            false,
            "",
        },
        new object[]
        {
            "destructure_empty_defaults",
            "function* g(){}const [a=7,...rest]=g();const [b=8,...tail]=\"\";console.log(a,rest.length,b,tail.length);\n",
            "7 0 8 0\n",
            false,
            "",
        },
        new object[]
        {
            "destructure_nested",
            "function* g(){yield [1,2];yield [3,4];}const [[a,...b],...rest]=g();console.log(a,b.join(\",\"),rest[0].join(\",\"));\n",
            "1 2 3,4\n",
            false,
            "",
        },
        new object[]
        {
            "destructure_custom_rest",
            "const source:any={i:0,[Symbol.iterator](){return this;},next(){this.i++;return {value:this.i*3,done:this.i>3};}};const [a,...rest]=source;console.log(a,rest.join(\",\"),source.i);\n",
            "3 6,9 4\n",
            false,
            "",
        },
        new object[]
        {
            "destructure_override_alias_control",
            "const source=[1,2];const alias:any=source;alias[Symbol.iterator]=function*(){yield 8;yield 9;};const [a,...rest]=source;console.log(a,rest.join(\",\"));\n",
            "8 9\n",
            false,
            "",
        },
    ];

    [Theory]
    [MemberData(nameof(ArrayDestructuringPrograms))]
    public void Isolated_ArrayDestructuring_PreservesSourcesDefaultsAndRest(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"array_destructuring_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> IteratorHelpersPrograms =>
    [
        new object[]
        {
            "helpers_lazy_pipeline",
            "let calls=0;const it=Iterator.from([1,2,3,4]).map((x:any)=>{calls++;return x*3;}).filter((x:any)=>x>3).take(2);console.log(calls);console.log(it.toArray().join(\",\"),calls);\n",
            "0\n6,9 3\n",
            false,
            "",
        },
        new object[]
        {
            "helpers_callback_indices",
            "const seen:any[]=[];console.log(Iterator.from([4,5,6]).map((x:any,i:any)=>x+i).filter((x:any,i:any)=>{seen.push(i);return x>4;}).toArray().join(\",\"),seen.join(\",\"));\n",
            "6,8 0,1,2\n",
            false,
            "",
        },
        new object[]
        {
            "helpers_flat_map",
            "console.log(Iterator.from([1,2,3]).flatMap((x:any)=>x===2?[]:[x,x+10]).toArray().join(\",\"));\n",
            "1,11,3,13\n",
            false,
            "",
        },
        new object[]
        {
            "helpers_reduce",
            "console.log(Iterator.from([1,2,3]).reduce((a:any,b:any)=>a+b),Iterator.from([1,2,3]).reduce((a:any,b:any)=>a+b,10));try{Iterator.from([]).reduce((a:any,b:any)=>a+b);}catch(e){console.log(e.name);}\n",
            "6 16\nTypeError\n",
            false,
            "",
        },
        new object[]
        {
            "helpers_short_circuit",
            "let calls=0;console.log(Iterator.from([1,2,3]).some((x:any)=>{calls++;return x===2;}),calls);calls=0;console.log(Iterator.from([1,2,3]).every((x:any)=>{calls++;return x<2;}),calls);console.log(Iterator.from([1,2,3]).find((x:any)=>x>1));\n",
            "true 2\nfalse 2\n2\n",
            false,
            "",
        },
        new object[]
        {
            "helpers_next_sent",
            "function* g(){const x=yield 1;yield x;return 9;}const it:any=Iterator.from(g());const a=it.next();const b=it.next(7);const c=it.next();console.log(a.value,b.value,c.value,c.done);\n",
            "1 7 9 true\n",
            false,
            "",
        },
        new object[]
        {
            "helpers_live_array",
            "const a=[1,2];console.log(a.values().map((x:any,i:any)=>{if(i===0)a.push(3);return x*2;}).toArray().join(\",\"));\n",
            "2,4,6\n",
            false,
            "",
        },
        new object[]
        {
            "helpers_callback_validation",
            "try{const it:any=Iterator.from([1]);it.map(null);console.log(\"accepted\");}catch(e){console.log(e.name);}\n",
            "TypeError\n",
            false,
            "",
        },
        new object[]
        {
            "helpers_for_each_values_control",
            "const seen:any[]=[];Iterator.from([4,5]).forEach((x:any,i:any)=>seen.push(x+i));console.log(seen.join(\",\"));\n",
            "4,6\n",
            false,
            "",
        },
        new object[]
        {
            "helpers_positive_limits_control",
            "console.log(Iterator.from([1,2,3]).take(0).toArray().length,Iterator.from([1,2,3]).drop(0).toArray().join(\",\"));\n",
            "0 1,2,3\n",
            false,
            "",
        },
        new object[]
        {
            "helpers_direct_close_control",
            "const source:any={closed:0,return(){this.closed++;return {done:true};}};console.log(source.return().done,source.closed);\n",
            "true 1\n",
            false,
            "",
        },
    ];

    [Theory]
    [MemberData(nameof(IteratorHelpersPrograms))]
    public void Isolated_IteratorHelpers_PreservesLazyAndEagerIteration(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"iterator_helpers_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> IteratorCollectionPrograms =>
    [
        new object[]
        {
            "collection_sparse_spread",
            "const a=[1,,3];const b=[0,...a,4];console.log(b.length,b.join(\",\"),b[2]===undefined);\n",
            "5 0,1,,3,4 true\n",
            false,
            "",
        },
        new object[]
        {
            "collection_array_override",
            "const a:any=[1,2];a[Symbol.iterator]=function*(){yield 8;yield 9;};console.log([...a].join(\",\"));function collect(...xs:any[]){console.log(xs.join(\",\"));}collect(0,...a,3);\n",
            "8,9\n0,8,9,3\n",
            false,
            "",
        },
        new object[]
        {
            "collection_live_array_iterator",
            "const a=[1,2];const it:any=a.values();const x=it.next();a.push(3);const y=it.next();const z=it.next();console.log(x.value,y.value,z.value,it.next().done);\n",
            "1 2 3 true\n",
            false,
            "",
        },
        new object[]
        {
            "collection_map_null",
            "const m:any=new Map();m.set(null,1);m.set(\"x\",2);console.log([...m].map((p:any)=>String(p[0])+\":\"+p[1]).join(\",\"));\n",
            "null:1,x:2\n",
            false,
            "",
        },
        new object[]
        {
            "collection_typed_buffer",
            "console.log([...new Uint8Array([3,4])].join(\",\"),[...Buffer.from([5,6])].join(\",\"));\n",
            "3,4 5,6\n",
            false,
            "",
        },
        new object[]
        {
            "collection_captured_next",
            "let reads=0;let calls=0;const it:any={[Symbol.iterator](){return this;},get next(){reads++;return function(){calls++;return {value:calls,done:calls>2};};}};console.log([...it].join(\",\"),reads,calls);\n",
            "1,2 1 3\n",
            false,
            "",
        },
        new object[]
        {
            "collection_completion_value",
            "const it:any={i:0,[Symbol.iterator](){return this;},next(){this.i++;if(this.i>1)return {done:true,get value(){throw new Error(\"terminal\");}};return {value:4,done:false};}};console.log([...it].join(\",\"));\n",
            "4\n",
            false,
            "",
        },
        new object[]
        {
            "collection_argument_append",
            "function collect(...xs:any[]){console.log(xs.length,xs.join(\",\"));}const a=[1,2];const b=[3,4];collect(0,...a,9,...b,8);console.log(a.join(\",\"),b.join(\",\"));\n",
            "7 0,1,2,9,3,4,8\n1,2 3,4\n",
            false,
            "",
        },
        new object[]
        {
            "collection_hole_direct_control",
            "const a:any[]=[1,,3];let reads=0;Object.defineProperty(a,\"1\",{get(){reads++;return 8;}});console.log(a[1],reads);\n",
            "8 1\n",
            false,
            "",
        },
        new object[]
        {
            "collection_hole_values_control",
            "const a:any[]=[1,,3];let reads=0;Object.defineProperty(a,\"1\",{get(){reads++;return 8;}});console.log(Array.from(a.values()).join(\",\"),reads);\n",
            "1,8,3 1\n",
            false,
            "",
        },
        new object[]
        {
            "collection_unicode_units_control",
            "const a=[...\"A\ud83d\ude00B\"];console.log(a.length,a[1].length,a[1].charCodeAt(0),a[1].charCodeAt(1));function collect(...xs:any[]){console.log(xs.length,xs[1].length,xs[1].charCodeAt(0),xs[1].charCodeAt(1));}collect(0,...\"\ud83d\ude00B\",9);\n",
            "3 2 55357 56832\n4 2 55357 56832\n",
            false,
            "",
        },
    ];

    [Theory]
    [MemberData(nameof(IteratorCollectionPrograms))]
    public void Isolated_IteratorCollection_PreservesSpreadAppendAndLiveArrayIteration(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"iterator_collection_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> IteratorProtocolPrograms =>
    [
        new object[]
        {
            "protocol_result_accessors",
            "let trace=\"\";const it:any={i:0,[Symbol.iterator](){return this;},next(){this.i++;const n=this.i;return {get done(){trace+=\"d\"+n+\",\";return n>2;},get value(){trace+=\"v\"+n+\",\";return n;}};}};let sum=0;for(const n of it)sum+=n;console.log(sum,trace);\n",
            "3 d1,v1,d2,v2,d3,\n",
            false,
            "",
        },
        new object[]
        {
            "protocol_done_coercion",
            "const it:any={i:0,[Symbol.iterator](){return this;},next(){this.i++;return {value:this.i,done:this.i===1?0:\"yes\"};}};let sum=0;for(const n of it)sum+=n;console.log(sum);\n",
            "1\n",
            false,
            "",
        },
        new object[]
        {
            "protocol_symbol_getter",
            "let reads=0;const it:any={i:0,next(){this.i++;return {value:this.i,done:this.i>2};}};Object.defineProperty(it,Symbol.iterator,{get(){reads++;return function(){return this;};}});console.log([...it].join(\",\"),reads);\n",
            "1,2 1\n",
            false,
            "",
        },
        new object[]
        {
            "protocol_close_break",
            "const it:any={[Symbol.iterator](){return this;},next(){return {value:3,done:false};},return(){console.log(\"closed\",this===it,arguments.length);return {};}};for(const n of it){console.log(n);break;}\n",
            "3\nclosed true 0\n",
            false,
            "",
        },
        new object[]
        {
            "protocol_close_throw_precedence",
            "const it:any={[Symbol.iterator](){return this;},next(){return {value:3,done:false};},return(){throw new Error(\"close\");}};try{for(const n of it)throw new Error(\"body\");}catch(e:any){console.log(e.message);}\n",
            "body\n",
            false,
            "",
        },
        new object[]
        {
            "protocol_close_invalid_result",
            "const it:any={[Symbol.iterator](){return this;},next(){return {value:3,done:false};},return(){return 3;}};try{for(const n of it)break;console.log(\"accepted\");}catch(e:any){console.log(e.name);}\n",
            "TypeError\n",
            false,
            "",
        },
        new object[]
        {
            "protocol_dynamic_array_next",
            "const it:any=[4,5].values();const a=it.next();const b=it.next();const c=it.next();console.log(a.value,a.done,b.value,b.done,c.value===undefined,c.done);\n",
            "4 false 5 false true true\n",
            false,
            "",
        },
        new object[]
        {
            "protocol_dynamic_generator",
            "function* values(){const v=yield 1;console.log(v===undefined);return 2;}const it:any=values();const a=it.next();console.log(a.value,a.done);const b=it.next();console.log(b.value,b.done);\n",
            "1 false\ntrue\n2 true\n",
            false,
            "",
        },
        new object[]
        {
            "protocol_bare_generator_return",
            "function* values(){try{yield 4;}finally{console.log(\"closed\");}}const it:any=values();it.next();const result=it.return();console.log(result.value===undefined,result.done);\n",
            "closed\ntrue true\n",
            false,
            "",
        },
        new object[]
        {
            "protocol_descriptor_overlay",
            "const it:any={i:0,[Symbol.iterator](){return this;},next(){this.i++;const r={value:this.i,done:this.i>2};if(this.i===1)Object.defineProperty(r,\"value\",{value:8});return r;}};let text=\"\";for(const n of it)text+=n+\",\";console.log(text);\n",
            "8,2,\n",
            false,
            "",
        },
        new object[]
        {
            "protocol_null_iterator_validation",
            "const value:any={[Symbol.iterator]:null};try{for(const n of value)console.log(n);console.log(\"accepted\");}catch(e:any){console.log(e.name);}\n",
            "TypeError\n",
            false,
            "",
        },
        new object[]
        {
            "protocol_stream_alias_control",
            "import {Readable} from \"stream\";async function run(){const stream=Readable.from([1,2,3]);let total=0;for await(const n of stream)total+=n;console.log(total);}run();\n",
            "6\n",
            false,
            "",
        },
    ];

    [Theory]
    [MemberData(nameof(IteratorProtocolPrograms))]
    public void Isolated_IteratorProtocol_PreservesLookupResultsClosingAndDynamicCalls(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"iterator_protocol_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> IteratorWrapperPrograms =>
    [
        new object[]
        {
            "wrapper_array_from",
            "const it:any={i:0,[Symbol.iterator](){return this;},next(){this.i++;return {value:this.i,done:this.i>3};}};console.log(Array.from(it,(x:any,i:number)=>x*2+i).join(\",\"));\n",
            "2,5,8\n",
            false,
            "",
        },
        new object[]
        {
            "wrapper_array_from_capture",
            "let reads=0;const it:any={i:0,[Symbol.iterator](){return this;},get next(){reads++;return function(){this.i++;return {value:this.i,done:this.i>3};};}};console.log(Array.from(it).join(\",\"),reads);\n",
            "1,2,3 1\n",
            false,
            "",
        },
        new object[]
        {
            "wrapper_promise_all",
            "const it:any={i:0,[Symbol.iterator](){return this;},next(){this.i++;return {value:Promise.resolve(this.i),done:this.i>3};}};Promise.all(it).then((values:any)=>console.log(values.join(\",\")));\n",
            "1,2,3\n",
            false,
            "",
        },
        new object[]
        {
            "wrapper_promise_race",
            "const it:any={i:3,[Symbol.iterator](){return this;},next(){this.i++;return {value:Promise.resolve(this.i),done:this.i>5};}};Promise.race(it).then((value:any)=>console.log(value));\n",
            "4\n",
            false,
            "",
        },
        new object[]
        {
            "wrapper_helper_chain",
            "const it:any={i:0,next(){this.i++;return {value:this.i,done:this.i>4};}};console.log(Iterator.from(it).map((x:any)=>x*3).take(2).toArray().join(\",\"));\n",
            "3,6\n",
            false,
            "",
        },
        new object[]
        {
            "wrapper_delegate_completion",
            "const it:any={i:0,[Symbol.iterator](){return this;},next(sent:any){this.i++;return {value:this.i===1?5:sent+2,done:this.i>1};}};function* values(){const n=yield* it;return n+1;}const g:any=values();const a=g.next();const b=g.next(10);console.log(a.value,a.done,b.value,b.done);\n",
            "5 false 13 true\n",
            false,
            "",
        },
    ];

    [Theory]
    [MemberData(nameof(IteratorWrapperPrograms))]
    public void Isolated_IteratorWrapper_PreservesConsumersAndDelegatedCompletion(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"iterator_wrapper_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> IteratorRecordPrograms =>
    [
        new object[]
        {
            "record_capture_getter",
            "let reads=0;const it:any={i:0,[Symbol.iterator](){return this;},get next(){reads++;return function(){this.i++;return {value:this.i,done:this.i>3};};}};console.log([...it].join(\",\"),reads);\n",
            "1,2,3 1\n",
            false,
            "",
        },
        new object[]
        {
            "record_capture_mutation",
            "const it:any={i:0,[Symbol.iterator](){return this;},next(){this.i++;if(this.i===1)this.next=()=>({value:99,done:true});return {value:this.i,done:this.i>3};}};console.log([...it].join(\",\"));\n",
            "1,2,3\n",
            false,
            "",
        },
        new object[]
        {
            "record_sent_forwarding",
            "let i=0;const it:any={[Symbol.iterator](){return this;},next(sent:any){console.log(arguments.length,sent===undefined,this===it);if(i++===0)return {value:2,done:false};return {value:sent+1,done:true};}};function* values(){return yield* it;}const g:any=values();const a=g.next();const b=g.next(8);console.log(a.value,a.done,b.value,b.done);\n",
            "1 true true\n1 false true\n2 false 9 true\n",
            false,
            "",
        },
        new object[]
        {
            "record_iterator_object_validation",
            "const value:any={[Symbol.iterator](){return 3;}};try{for(const x of value)console.log(x);}catch(e:any){console.log(e.name);}\n",
            "TypeError\n",
            false,
            "",
        },
        new object[]
        {
            "record_result_object_validation",
            "const value:any={[Symbol.iterator](){return this;},next(){return 3;}};try{for(const x of value)console.log(x);}catch(e:any){console.log(e.name);}\n",
            "TypeError\n",
            false,
            "",
        },
        new object[]
        {
            "record_next_callable_validation",
            "const value:any={[Symbol.iterator](){return this;},next:3};try{for(const x of value)console.log(x);}catch(e:any){console.log(e.name);}\n",
            "TypeError\n",
            false,
            "",
        },
        new object[]
        {
            "record_async_capture",
            "const it:any={i:0,[Symbol.iterator](){return this;},next(){this.i++;if(this.i===1)this.next=()=>({value:99,done:true});return {value:this.i,done:this.i>3};}};async function run(){let s=\"\";for await(const n of it)s+=n+\",\";console.log(s);}run();\n",
            "1,2,3,\n",
            false,
            "",
        },
        new object[]
        {
            "record_destructure_cleanup",
            "function* values(){try{yield 2;yield 3;}finally{console.log(\"closed\");}}const [a]=values();console.log(a);\n",
            "closed\n2\n",
            false,
            "",
        },
    ];

    [Theory]
    [MemberData(nameof(IteratorRecordPrograms))]
    public void Isolated_IteratorRecord_PreservesCapturedNextValidationAndSentValues(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"iterator_record_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> AsyncGeneratorPrograms =>
    [
        new object[]
        {
            "async_next_sent",
            "async function* values(){const n:any=yield 2;yield await Promise.resolve(n+3);return 9;}async function run(){const g=values();const a=await g.next();const b=await g.next(4);const c=await g.next();console.log(a.value,a.done,b.value,b.done,c.value,c.done);}run();\n",
            "2 false 7 false 9 true\n",
            false,
            "",
        },
        new object[]
        {
            "async_pending_next",
            "async function* values(){yield await new Promise<number>(resolve=>setTimeout(()=>resolve(6),5));}async function run(){const g=values();const pending=g.next();console.log(\"pending\");const a=await pending;console.log(a.value,a.done);console.log((await g.next()).done);}run();\n",
            "pending\n6 false\ntrue\n",
            false,
            "",
        },
        new object[]
        {
            "async_return_finally",
            "async function* values(){try{yield 1;yield 2;}finally{console.log(\"closed\");}}async function run(){const g=values();console.log((await g.next()).value);const r=await g.return(8);console.log(r.value,r.done);}run();\n",
            "1\nclosed\n8 true\n",
            false,
            "",
        },
        new object[]
        {
            "async_from_sync_unicode",
            "async function run(){let count=0;let lengths=\"\";for await(const c of \"a\ud83d\ude00\"){count++;lengths+=c.length+\",\";}console.log(count,lengths);}run();\n",
            "2 1,2,\n",
            false,
            "",
        },
        new object[]
        {
            "async_from_sync_set",
            "async function run(){let total=0;for await(const n of new Set([2,3,2]))total+=n;console.log(total);}run();\n",
            "5\n",
            false,
            "",
        },
        new object[]
        {
            "async_from_sync_close",
            "const values:any={[Symbol.iterator](){let n=0;return {next(){return {value:Promise.resolve(++n),done:false};},return(){console.log(\"closed\");return {value:Promise.resolve(8),done:true};}};}};async function run(){for await(const n of values){console.log(n);break;}console.log(\"done\");}run();\n",
            "1\nclosed\ndone\n",
            false,
            "",
        },
        new object[]
        {
            "async_rejected_await",
            "async function* values(){yield 1;await Promise.reject(\"bad\");yield 2;}async function run(){try{for await(const n of values())console.log(n);}catch(e){console.log(e);}}run();\n",
            "1\nbad\n",
            false,
            "",
        },
        new object[]
        {
            "async_generator_break",
            "async function* values(){try{yield await Promise.resolve(3);yield 4;}finally{console.log(\"closed\");}}async function run(){for await(const n of values()){console.log(n);break;}console.log(\"done\");}run();\n",
            "3\nclosed\ndone\n",
            false,
            "",
        },
        new object[]
        {
            "async_delegate_values_control",
            "async function* inner(){yield 2;yield 3;}async function* outer(){yield* inner();}async function run(){let total=0;for await(const n of outer())total+=n;console.log(total);}run();\n",
            "5\n",
            false,
            "",
        },
        new object[]
        {
            "async_custom_external_counter_control",
            "let n=0;const values:any={[Symbol.asyncIterator](){return {async next(){return {value:++n,done:n>3};}};}};async function run(){let total=0;for await(const v of values)total+=v;console.log(total);}run();\n",
            "6\n",
            false,
            "",
        },
        new object[]
        {
            "async_promise_array_any_control",
            "async function run(){const values:any=[Promise.resolve(2),Promise.resolve(4)];let total=0;for await(const n of values)total+=n;console.log(total);}run();\n",
            "6\n",
            false,
            "",
        },
        new object[]
        {
            "async_nested_array_any_control",
            "async function* values(){const items:any=[Promise.resolve(2),Promise.resolve(3)];for await(const n of items)yield n+1;}async function run(){let total=0;for await(const n of values())total+=n;console.log(total);}run();\n",
            "7\n",
            false,
            "",
        },
    ];

    [Theory]
    [MemberData(nameof(AsyncGeneratorPrograms))]
    public void Isolated_AsyncGenerator_PreservesAwaitingDelegationAndCleanup(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"async_generator_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> GeneratorProtocolPrograms =>
    [
        new object[]
        {
            "generator_throw_return",
            "function* values(){try{yield 3;}catch(e:any){yield e.tag;}finally{console.log(\"finally\");}}const g:any=values();console.log(g.next().value);console.log(g.throw({tag:7}).value);const r=g.return(9);console.log(r.value,r.done);\n",
            "3\n7\nfinally\n9 true\n",
            false,
            "",
        },
        new object[]
        {
            "generator_delegated_sent",
            "function* inner(){const n:any=yield 4;return n+1;}function* outer(){const n:any=yield* inner();return n+2;}const g:any=outer();const a=g.next();const b=g.next(8);console.log(a.value,a.done,b.value,b.done);\n",
            "4 false 11 true\n",
            false,
            "",
        },
        new object[]
        {
            "generator_numeric_direct",
            "function* range(n:number):Generator<number>{for(let i:number=0;i<n;i++)yield i;}function sum(n:number):number{let s:number=0;for(const x of range(n))s=s+x;return s;}console.log(sum(10),sum(100));\n",
            "45 4950\n",
            false,
            "",
        },
        new object[]
        {
            "generator_numeric_alias",
            "function* range(n:number):Generator<number>{for(let i:number=0;i<n;i++)yield i;}const makeRange=range;function sum(n:number):number{let s:number=0;for(const x of makeRange(n))s=s+x;return s;}console.log(sum(10));\n",
            "45\n",
            false,
            "",
        },
        new object[]
        {
            "iterator_numeric_result",
            "function iterate(n:number):number{let current:number=0;const iterable={[Symbol.iterator](){return this;},next(){if(current<n){const value:number=current;current=current+1;return {value,done:false};}return {value:0,done:true};}};let total:number=0;for(const value of iterable)total=total+value;return total;}console.log(iterate(10));\n",
            "45\n",
            false,
            "",
        },
        new object[]
        {
            "iterator_numeric_break",
            "let current:number=0;let closes:number=0;const iterable={[Symbol.iterator](){return this;},next(){const value:number=current++;return {value,done:false};},return(){closes=closes+1;return {value:0,done:true};}};for(const value of iterable){console.log(value);break;}console.log(\"closes=\"+closes);\n",
            "0\ncloses=1\n",
            false,
            "",
        },
        new object[]
        {
            "iterator_numeric_escaped",
            "let current:number=0;const iterable:any={[Symbol.iterator](){return this;},next(){return {value:current++,done:current>3};}};const alias:any=iterable;alias.next=()=>({value:9,done:true});let total:number=0;for(const value of iterable)total=total+value;console.log(total);\n",
            "0\n",
            false,
            "",
        },
        new object[]
        {
            "generator_async_from_sync",
            "function* values(){try{yield 1;yield 2;}finally{console.log(\"closed\");}}async function run(){for await(const value of values()){console.log(value);break;}console.log(\"done\");}run();\n",
            "1\nclosed\ndone\n",
            false,
            "",
        },
        new object[]
        {
            "generator_async_protocol",
            "async function* values(){yield 1;yield await Promise.resolve(2);}async function run(){const g=values();const a=await g.next();const b=await g.next();const c=await g.next();console.log(a.value,a.done,b.value,b.done,c.value===undefined,c.done);}run();\n",
            "1 false 2 false true true\n",
            false,
            "",
        },
        new object[]
        {
            "generator_return_finally_yield",
            "function* values(){try{yield 1;}finally{yield 2;}}const g:any=values();const a=g.next();const b=g.return(9);const c=g.next();console.log(a.value,a.done,b.value,b.done,c.value,c.done);\n",
            "1 false 2 false 9 true\n",
            false,
            "",
        },
        new object[]
        {
            "generator_public_next_control",
            "function* values(){yield 1;const sent:any=yield 2;return sent;}const g:any=values();const a=g.next();const b=g.next();const c=g.next(9);console.log(a.value,a.done,b.value,b.done,c.value,c.done);\n",
            "1 false 2 false 9 true\n",
            false,
            "",
        },
        new object[]
        {
            "generator_array_delegation_control",
            "function* values(){yield* [1,2];}console.log([...values()].join(\",\"));\n",
            "1,2\n",
            false,
            "",
        },
        new object[]
        {
            "generator_plain_string_control",
            "const result:any=[...\"a\ud83d\ude00\"];console.log(result.length,result[1].length,result[1].charCodeAt(0));\n",
            "2 2 55357\n",
            false,
            "",
        },
    ];

    [Theory]
    [MemberData(nameof(GeneratorProtocolPrograms))]
    public void Isolated_GeneratorProtocol_PreservesIterationAndNumericResults(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"generator_protocol_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> CallArgumentsPrograms =>
    [
        new object[]
        {
            "arguments_arities",
            "const o:any={f:function(...x:any[]){return x.length+\":\"+x.join(\",\");}};console.log(o.f(),o.f(1),o.f(1,2),o.f(1,2,3),o.f(1,2,3,4),o.f(1,2,3,4,5));\n",
            "0: 1:1 2:1,2 3:1,2,3 4:1,2,3,4 5:1,2,3,4,5\n",
            false,
            "",
        },
        new object[]
        {
            "arguments_spread_iterable",
            "const iterable:any={[Symbol.iterator]:function(){let i=0;return{next:function(){i++;return{value:i,done:i>2};}};}};const o:any={tag:\"ok\",f:function(...x:any[]){console.log(this.tag,x.join(\"|\"));}};o.f(0,...[1,2],...\"ab\",...iterable,9);\n",
            "ok 0|1|2|a|b|1|2|9\n",
            false,
            "",
        },
        new object[]
        {
            "arguments_spread_order",
            "let order=\"\";function mark(n:number){order+=n;return n;}const o:any={f:function(...x:any[]){console.log(x.join(\",\"));}};o.f(mark(1),...[mark(2),mark(3)],mark(4));console.log(order);\n",
            "1,2,3,4\n1234\n",
            false,
            "",
        },
        new object[]
        {
            "arguments_nested_materialized",
            "const o:any={f:function(a:any,b:any){return a*10+b;}};const left=o.f(1,2);const right=o.f(3,4);console.log(o.f(left,right));\n",
            "154\n",
            false,
            "",
        },
        new object[]
        {
            "arguments_nested_spread",
            "const o:any={f:function(a:any,b:any){return a*10+b;}};console.log(o.f(...[o.f(1,2),o.f(3,4)]));\n",
            "154\n",
            false,
            "",
        },
        new object[]
        {
            "arguments_nested_value",
            "const o:any={f:function(a:any,b:any){return a*10+b;}};const f:any=o.f;console.log(f(f(1,2),f(3,4)));\n",
            "154\n",
            false,
            "",
        },
    ];

    [Theory]
    [MemberData(nameof(CallArgumentsPrograms))]
    public void Isolated_CallArguments_PreservesAritySpreadAndEvaluationOrder(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"call_arguments_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> DynamicConstructionPrograms =>
    [
        new object[]
        {
            "dynamic_new_returns",
            "function F(this:any,x:number){this.x=x;}const C:any=F;const a:any=new C(7);console.log(a.x,a instanceof C);function R(this:any){this.x=1;return {x:9};}const D:any=R;console.log(new D().x);function P(this:any){this.x=4;return 3;}const E:any=P;console.log(new E().x);\n",
            "7 true\n9\n4\n",
            false,
            "",
        },
        new object[]
        {
            "dynamic_new_aliases",
            "const S:any=String;const s:any=new S(\"abc\");console.log(s.length,s.valueOf());const R:any=RegExp;const r:any=new R(\"a\",\"g\");console.log(r.source,r.flags,r.test(\"cat\"));const choose:any=()=>R;const q:any=new (choose())(\"b\",\"i\");console.log(q.source,q.flags,q.test(\"B\"));\n",
            "3 abc\na g true\nb i true\n",
            false,
            "",
        },
        new object[]
        {
            "dynamic_nested_control",
            "function Inner(this:any){this.y=2;}function Outer(this:any){this.x=1;const C:any=Inner;const child:any=new C();this.x+=child.y;}const C:any=Outer;console.log(new C().x);\n",
            "3\n",
            false,
            "",
        },
    ];

    [Theory]
    [MemberData(nameof(DynamicConstructionPrograms))]
    public void Isolated_DynamicConstruction_PreservesReturnsAliasesAndNestedReceivers(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"dynamic_construction_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> InvocationDispatchPrograms =>
    [
        new object[]
        {
            "dispatch_receivers",
            "function read(this:any,x:number){return this.n+x;}const a:any={n:7,read};const b:any={n:11,read:a.read};console.log(a[\"read\"](3),b.read(2));const echo:any=(x:number)=>x+1;console.log(echo(4));const s:any=String;console.log(s(23));\n",
            "10 13\n5\n23\n",
            false,
            "",
        },
        new object[]
        {
            "dispatch_zero_arguments",
            "const o:any={n:7,read:function(this:any){return this.n;}};const count:any=(...xs:any[])=>xs.length;console.log(o[\"read\"](),count());const a:any={f:count};console.log(a.f(),a.f(1,2));try{const bad:any=null;bad();}catch(e:any){console.log(e instanceof TypeError);}try{const p:any={f:null};p.f();}catch(e:any){console.log(e instanceof TypeError);}\n",
            "7 0\n0 2\ntrue\ntrue\n",
            false,
            "",
        },
        new object[]
        {
            "dispatch_map_get",
            "const m:any=new Map([[\"x\",7]]);const get:any=m.get;console.log(get.call(m,\"x\"));\n",
            "7\n",
            false,
            "",
        },
        new object[]
        {
            "dispatch_set_has",
            "const s:any=new Set([1,2]);const has:any=s.has;console.log(has.call(s,2));\n",
            "true\n",
            false,
            "",
        },
        new object[]
        {
            "dispatch_promise_resolve",
            "new Promise((resolve:any)=>{const f:any=resolve;f(9);}).then((n:any)=>console.log(n));\n",
            "9\n",
            false,
            "",
        },
    ];

    [Theory]
    [MemberData(nameof(InvocationDispatchPrograms))]
    public void Isolated_InvocationDispatch_PreservesReceiversAndOptionalWrappers(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"invocation_dispatch_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> ReflectedMethodPrograms =>
    [
        new object[]
        {
            "reflection_hash_cache",
            "import {createHash} from \"node:crypto\";const h:any=createHash(\"sha256\");const update:any=h.update;console.log(update===h.update);update.call(h,\"abc\");console.log(h.digest(\"hex\"));\n",
            "true\nba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad\n",
            false,
            "",
        },
        new object[]
        {
            "reflection_crypto_callable",
            "import {getDiffieHellman} from \"node:crypto\";const d:any=getDiffieHellman(\"modp14\");console.log(d.getPrime(\"hex\").length,d.getGenerator(\"hex\"));\n",
            "512 02\n",
            false,
            "",
        },
        new object[]
        {
            "reflection_event_callable",
            "import {EventEmitter} from \"node:events\";const e:any=new EventEmitter();const emit:any=e.emit;console.log(emit===e.emit);e.on(\"x\",(n:number)=>console.log(n));emit.call(e,\"x\",7);\n",
            "true\n7\n",
            false,
            "",
        },
    ];

    [Theory]
    [MemberData(nameof(ReflectedMethodPrograms))]
    public void Isolated_ReflectedMethods_PreserveCachedWrappersAndCallableDispatch(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"reflected_method_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> FunctionIntrospectionPrograms =>
    [
        new object[]
        {
            "introspection_properties",
            "function sample(a:number,b:number){}const f:any=sample;console.log(f.name,f.length,f.missing===undefined);f.tag=7;console.log(f.tag,f.name,f.length);Object.defineProperty(f,\"name\",{value:\"renamed\",configurable:true});Object.defineProperty(f,\"length\",{value:4,configurable:true});console.log(f.name,f.length);\n",
            "sample 2 true\n7 sample 2\nrenamed 4\n",
            false,
            "",
        },
        new object[]
        {
            "introspection_prototype_identity",
            "function First(){}function Second(){}const a:any=First;const b:any=Second;console.log(a.prototype===a.prototype,a.prototype!==b.prototype,a.prototype.constructor===a);a.prototype.tag=9;console.log(First.prototype.tag,b.prototype.tag===undefined);\n",
            "true true true\n9 true\n",
            false,
            "",
        },
        new object[]
        {
            "introspection_dynamic_construct",
            "class Point{x:number;constructor(x:number){this.x=x;}}const p:any=Reflect.construct(Point as any,[7]);console.log(p.x,p instanceof Point);for(const value of [null,undefined,{},Function.prototype.call]){try{Reflect.construct(value as any,[]);console.log(false);}catch(error){console.log(error instanceof TypeError);}}\n",
            "7 true\ntrue\ntrue\ntrue\ntrue\n",
            false,
            "",
        },
    ];

    [Theory]
    [MemberData(nameof(FunctionIntrospectionPrograms))]
    public void Isolated_FunctionIntrospection_PreservesPropertiesAndConstructorCapabilities(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"function_introspection_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> FunctionPrototypePrograms =>
    [
        new object[]
        {
            "prototype_descriptors",
            "const p:any=Function.prototype;for(const name of [\"call\",\"apply\",\"bind\",\"toString\",\"constructor\"]){const d:any=Object.getOwnPropertyDescriptor(p,name);console.log(name,d.writable,d.enumerable,d.configurable,d.value===p[name]);}console.log(Object.getPrototypeOf(p)===Object.prototype);\n",
            "call true false true true\napply true false true true\nbind true false true true\ntoString true false true true\nconstructor true false true true\ntrue\n",
            false,
            "",
        },
        new object[]
        {
            "prototype_borrowed_call",
            "function f(this:any,a:number,b:number){return this.x+a+b;}const c:any=Function.prototype.call;const a:any=Function.prototype.apply;console.log(c.call(f,{x:1},2,3),a.call(f,{x:4},[5,6]));\n",
            "6 15\n",
            false,
            "",
        },
        new object[]
        {
            "prototype_borrowed_bind",
            "function f(this:any,a:number,b:number){return this.x+a+b;}const bind:any=Function.prototype.bind;const g:any=bind.call(f,{x:1},2);console.log(g(3),g.length,Object.getPrototypeOf(g)===Function.prototype);\n",
            "6 1 true\n",
            false,
            "",
        },
        new object[]
        {
            "prototype_property_helper",
            "const has:any=Function.prototype.call.bind(Object.prototype.hasOwnProperty);console.log(has({x:1},\"x\"),has({},\"x\"));function f(a:number){}console.log(Object.getPrototypeOf(f)===Function.prototype,Function.prototype.constructor===Function,typeof Function.prototype.toString.call(f));\n",
            "true false\ntrue true string\n",
            false,
            "",
        },
    ];

    [Theory]
    [MemberData(nameof(FunctionPrototypePrograms))]
    public void Isolated_FunctionPrototype_PreservesDescriptorsAndBorrowedMethods(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"function_prototype_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> FunctionInvocationPrograms =>
    [
        new object[]
        {
            "nested_arguments",
            "function inner(x:number){console.log(arguments.length,arguments[1]);}function outer(a:number){const saved=()=>arguments[2];const i:any=inner;i(8,9);console.log(arguments.length,arguments[0],saved());}const f:any=outer;f(1,2,3);\n",
            "2 9\n3 1 3\n",
            false,
            "",
        },
        new object[]
        {
            "arguments_brand_length",
            "function f(a:number){console.log(Array.isArray(arguments),Object.prototype.toString.call(arguments),arguments.length);arguments[4]=9;console.log(arguments.length,arguments[4]);}const g:any=f;g(1,2);\n",
            "false [object Arguments] 2\n2 9\n",
            false,
            "",
        },
        new object[]
        {
            "arguments_throw",
            "function bad(a:number){throw new Error(\"bad\");}function outer(a:number){try{const b:any=bad;b(8,9);}catch(e){console.log(arguments.length,arguments[1]);}}const f:any=outer;f(1,2,3);\n",
            "3 2\n",
            false,
            "",
        },
        new object[]
        {
            "receiver_nested_throw",
            "function inner(this:any){console.log(this.name);throw new Error(\"bad\");}function outer(this:any){try{inner.call({name:\"inner\"});}catch(e){}console.log(this.name);}outer.call({name:\"outer\"});\n",
            "inner\nouter\n",
            false,
            "",
        },
        new object[]
        {
            "strict_sloppy",
            "function strict(this:any){\"use strict\";return this;}function sloppy(this:any){return this;}const a:any=strict;const b:any=sloppy;console.log(a.call(null)===null,a.call(undefined)===undefined,b.call(null)===globalThis,b.call(undefined)===globalThis);\n",
            "true true true true\n",
            false,
            "",
        },
        new object[]
        {
            "zero_receiver",
            "const f:any=function(this:any){return this.x;};const x:any={x:7,f:f};console.log(x.f(),f.call({x:9}),f.bind({x:11})());\n",
            "7 9 11\n",
            false,
            "",
        },
        new object[]
        {
            "zero_arguments_capture",
            "const f:any=function(this:any){return arguments.length+this.x;};const x:any={x:7,f:f};console.log(x.f(),x.f(1,2),f.call({x:8}));\n",
            "7 9 8\n",
            false,
            "",
        },
        new object[]
        {
            "bound_receiver_chain",
            "function f(this:any,a:number,b:number,c:number){return this.x+a+b+c;}const a:any=f.bind({x:10},1);const b:any=a.bind({x:99},2);console.log(b(3),b.call({x:100},4),b.apply({x:101},[5]),b.length);\n",
            "16 17 18 1\n",
            false,
            "",
        },
        new object[]
        {
            "bound_array",
            "const a:number[]=[];const push:any=a.push;push.call(a,1);push.apply(a,[2,3]);const b:any=push.bind(a,4);console.log(b(5),a.join(\",\"),typeof b);\n",
            "5 1,2,3,4,5 function\n",
            false,
            "",
        },
        new object[]
        {
            "bound_map",
            "const m=new Map<string,number>();m.set(\"a\",1);m.set(\"b\",2);const g:any=m.get;const b:any=g.bind(m,\"b\");console.log(g.call(m,\"a\"),g.apply(m,[\"b\"]),b());\n",
            "1 2 2\n",
            false,
            "",
        },
        new object[]
        {
            "bound_set",
            "const s=new Set<number>();const add:any=s.add.bind(s);add(1);add(2);const h:any=s.has;console.log(h.call(s,1),h.apply(s,[9]),h.bind(s,2)(),s.size);\n",
            "true false true 2\n",
            false,
            "",
        },
        new object[]
        {
            "bind_noncallable",
            "const value:any=Object.create(Function.prototype);try{value.bind();console.log(false);}catch(error){console.log(error instanceof TypeError);}\n",
            "true\n",
            false,
            "",
        },
        new object[]
        {
            "hosted_bound",
            "export function run(x:number){function f(this:any,a:number){return this.x+a;}const b:any=f.bind({x:x},2);return b();}\n",
            "",
            true,
            "",
        },
    ];

    [Theory]
    [MemberData(nameof(FunctionInvocationPrograms))]
    public void Isolated_FunctionInvocation_PreservesArgumentsBindingAndDeployment(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"function_invocation_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> FunctionConstructionPrograms =>
    [
        new object[]
        {
            "identity",
            "function f(x:number){return x+1;}const a:any=f;const b:any=f;a.tag=7;console.log(a===b,b.tag,a.name,a.length);console.log(String.prototype.toString===String.prototype.valueOf);\n",
            "true 7 f 1\nfalse\n",
            false,
            "",
        },
        new object[]
        {
            "prototypes",
            "function F(this:any,x:number){this.x=x;}const a:any=F;const b:any=F;a.prototype.tag=3;const x:any=new a(7);console.log(a.prototype===b.prototype,x.tag,x.x,x instanceof b);\n",
            "true 3 7 true\n",
            false,
            "",
        },
        new object[]
        {
            "function_constructor",
            "const ctor:any=Function;const empty:any=ctor();const self:any=ctor(\"return this;\");console.log(empty.name,empty.length,empty()===undefined,self()===globalThis,self.call({x:7}).x);\n",
            "anonymous 0 true true 7\n",
            false,
            "",
        },
        new object[]
        {
            "closures_bind",
            "function make(x:number){return function(this:any,y:number){return x+this.base+y;};}const a:any=make(1);const b:any=make(5);console.log(a.call({base:2},3),b.apply({base:4},[6]),a.bind({base:7},8)());\n",
            "6 15 16\n",
            false,
            "",
        },
        new object[]
        {
            "default_rest",
            "function f(a:number=4,...xs:number[]){return a+xs.length+arguments.length;}const g:any=f;console.log(g(),g(undefined,2,3),g(1,2,3,4));\n",
            "4 9 8\n",
            false,
            "",
        },
        new object[]
        {
            "numeric_rest_selection",
            "function first(...v:number[]):number{return v[0]+v[1]+v[2]+v[3];}function second(...v:number[]):number{return v[0]+v[1]+v[2]+v[3]+100;}let fn:(...v:number[])=>number=first;let trace=\"\";function argument(value:number):number{trace=trace+value;fn=second;return value;}function run():number{return fn(argument(1),2,3,4);}console.log(run(),fn(1,2,3,4),trace);\n",
            "10 110 1\n",
            false,
            "",
        },
        new object[]
        {
            "numeric_rest_fallback",
            "function add(...v:number[]):number{return v[0]+v[1]+v[2]+v[3];}function observe(...v:number[]):number{return arguments.length+v.length;}function defaults(prefix:number=2,...v:number[]):number{return prefix+v.length;}function fixed(a:number,b:number,c:number,d:number):number{return a*b+c*d;}function choose(fn:(...v:number[])=>number):number{return fn(1,2,3,4);}function capture(value:number):(...v:number[])=>number{return (...v:number[]):number=>value+v[0];}const bound=add.bind(null,10);console.log(choose(observe),choose(defaults),choose(fixed),choose(capture(5)),choose(bound));\n",
            "8 4 14 6 16\n",
            false,
            "",
        },
        new object[]
        {
            "receiver_ref",
            "const f:any=function(this:any,x:number){return this.base+x;};console.log(f.call({base:7},2),f.apply({base:5},[4]));const b:any=f.bind({base:10},3);console.log(b());\n",
            "9 9\n13\n",
            false,
            "--ref-asm",
        },
        new object[]
        {
            "async_name",
            "async function compute(x:number=3){return x+2;}const fn:any=compute;compute().then(v=>console.log(fn.name,fn.length,v));\n",
            "compute 0 5\n",
            false,
            "",
        },
        new object[]
        {
            "generator_name",
            "function* values(start:number=2){yield start;yield start+1;}const g:any=values;console.log(g.name,g.length);for(const v of values()){console.log(v);}\n",
            "values 0\n2\n3\n",
            false,
            "",
        },
        new object[]
        {
            "hosted_functions",
            "export function run(x:number){function f(a:number){return arguments.length+a;}const g:any=f;return g(x,2,3);}\n",
            "",
            true,
            "",
        },
        new object[]
        {
            "hosted_receivers",
            "export function run(x:number){const f:any=function(this:any){return this.x;};return f.call({x:x});}\n",
            "",
            true,
            "",
        },
    ];

    [Theory]
    [MemberData(nameof(FunctionConstructionPrograms))]
    public void Isolated_FunctionConstruction_PreservesCreationAndDeployment(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"function_construction_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> FunctionAttributePrograms =>
    [
        new object[]
        {
            "name_length",
            "function f(a:number,b:number=4,...tail:number[]){return a+b+tail.length;}function g(a:number,b:number){return a+b;}const x:any=f;const y:any=g;console.log(x.name,x.length);console.log(y.name,y.length);\n",
            "f 1\ng 2\n",
            false,
            "",
        },
        new object[]
        {
            "undefined_padding",
            "function f(a:any,b:any){console.log(a===undefined,b===undefined);}const g:any=f;g(1);g();\n",
            "false true\ntrue true\n",
            false,
            "",
        },
        new object[]
        {
            "arguments_extra",
            "function collect(a:number){console.log(arguments.length,arguments[2]);}const v:any=collect;v(1,2,3);\n",
            "3 3\n",
            false,
            "",
        },
        new object[]
        {
            "receiver_bind",
            "const f:any=function(this:any,x:number){return this.base+x;};console.log(f.call({base:7},2),f.apply({base:5},[4]));const b:any=f.bind({base:10},3);console.log(b());\n",
            "9 9\n13\n",
            false,
            "",
        },
        new object[]
        {
            "numeric_rest_selection",
            "function first(...v:number[]):number{return v[0]+v[1]+v[2]+v[3];}function second(...v:number[]):number{return v[0]+v[1]+v[2]+v[3]+100;}let fn:(...v:number[])=>number=first;let trace=\"\";function argument(value:number):number{trace=trace+value;fn=second;return value;}function run():number{return fn(argument(1),2,3,4);}console.log(run(),fn(1,2,3,4),trace);\n",
            "10 110 1\n",
            false,
            "",
        },
        new object[]
        {
            "numeric_rest_fallback",
            "function add(...v:number[]):number{return v[0]+v[1]+v[2]+v[3];}function observe(...v:number[]):number{return arguments.length+v.length;}function defaults(prefix:number=2,...v:number[]):number{return prefix+v.length;}function fixed(a:number,b:number,c:number,d:number):number{return a*b+c*d;}function choose(fn:(...v:number[])=>number):number{return fn(1,2,3,4);}function capture(value:number):(...v:number[])=>number{return (...v:number[]):number=>value+v[0];}const bound=add.bind(null,10);console.log(choose(observe),choose(defaults),choose(fixed),choose(capture(5)),choose(bound));\n",
            "8 4 14 6 16\n",
            false,
            "",
        },
        new object[]
        {
            "class_method",
            "class Example{value=7;read(a:number,b:number=3){return this.value+a+b;}}const x=new Example();const fn:any=x.read;console.log(fn.name,fn.length,fn.call(x,2));\n",
            "read 1 12\n",
            false,
            "",
        },
        new object[]
        {
            "async_name",
            "async function compute(x:number=3){return x+2;}const fn:any=compute;compute().then(v=>console.log(fn.name,fn.length,v));\n",
            "compute 0 5\n",
            false,
            "",
        },
        new object[]
        {
            "generator_name",
            "function* values(start:number=2){yield start;yield start+1;}const g:any=values;console.log(g.name,g.length);for(const v of values()){console.log(v);}\n",
            "values 0\n2\n3\n",
            false,
            "",
        },
        new object[]
        {
            "hosted_minimal",
            "export function label(value:number=3){return value+2;}\n",
            "",
            true,
            "",
        },
        new object[]
        {
            "hosted_numeric_rest",
            "export function add(...v:number[]):number{return v[0]+v[1]+v[2]+v[3];}export function inspect(){const f:any=add;return [f.name,f.length,f(1,2,3,4)];}\n",
            "",
            true,
            "",
        },
        new object[]
        {
            "receiver_ref",
            "const f:any=function(this:any,x:number){return this.base+x;};console.log(f.call({base:7},2),f.apply({base:5},[4]));const b:any=f.bind({base:10},3);console.log(b());\n",
            "9 9\n13\n",
            false,
            "--ref-asm",
        },
    ];

    [Theory]
    [MemberData(nameof(FunctionAttributePrograms))]
    public void Isolated_FunctionAttributes_PreserveInvocationMetadataAndDeployment(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"function_attributes_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> ScopedFeatureGatePrograms =>
    [
        new object[]
        {
            "combined_standalone",
            "import {createInterface} from \"readline\";const rl=createInterface({prompt:\"gate> \"});const c=new AbortController();c.abort(\"plain\");console.log(rl.getPrompt(),c.signal.aborted,c.signal.reason);rl.close();",
            "gate>  true plain\n",
            "",
            false,
            true
        },
        new object[]
        {
            "combined_any_input",
            "import {questionSync,createInterface} from \"readline\";const c=new AbortController();const s=AbortSignal.any([c.signal]);console.log(questionSync(\"input> \"));const rl=createInterface({prompt:\"both> \"});c.abort(\"combined\");console.log(rl.getPrompt(),s.aborted,s.reason);rl.close();",
            "input> supplied\nboth>  true combined\n",
            "supplied\n",
            false,
            false
        },
        new object[]
        {
            "hosted_combined",
            "import {createInterface} from \"readline\";export function inspect(){const r=createInterface({prompt:\"host> \"});const c=new AbortController();const s=AbortSignal.any([c.signal]);c.abort(\"hosted\");r.close();return [r.getPrompt(),s.aborted,s.reason];}",
            "",
            "",
            true,
            false
        },
    ];

    [Theory]
    [MemberData(nameof(ScopedFeatureGatePrograms))]
    public void Isolated_ScopedFeatureGates_PreserveCombinedInputAndDeployment(
        string name, string source, string expected, string input, bool hosted, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"scoped-feature-gates_{name}.dll");
        var deployment = standalone ? " --standalone" : "";
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify{deployment}{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error), standardInput: input));
    }

    public static IEnumerable<object[]> OperatorMetadataPrograms =>
    [
        new object[]
        {
            "minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(1);" },
            "1\n",
            false,
            true
        },
        new object[]
        {
            "updates",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let n:any=\"4\";console.log(n++,n,++n,n--,--n);let b:any=4n;console.log(b++,b,++b,b--,--b);" },
            "4 5 6 6 4\n4n 5n 6n 6n 4n\n",
            false,
            true
        },
        new object[]
        {
            "update_order",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let count=0;const o:any={x:\"2\"};function key(){count++;return \"x\";}console.log(o[key()]++,o.x,count);" },
            "2 3 1\n",
            false,
            true
        },
        new object[]
        {
            "relational",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a:any=\"10\";const b:any=\"2\";console.log(a<b,a<=b,a>b,a>=b);console.log(a<2,a<=10,2>a,10>=a);" },
            "true true false false\nfalse true false true\n",
            false,
            true
        },
        new object[]
        {
            "unordered",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const n:any=NaN;console.log(n<1,n<=1,n>1,n>=1,n==n,n===n);" },
            "false false false false false false\n",
            false,
            true
        },
        new object[]
        {
            "addition",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a:any=2;const b:any=\"3\";console.log(a+3,a+b,b+a,2n+3n);" },
            "5 23 32 5n\n",
            false,
            true
        },
        new object[]
        {
            "mixed_addition",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a:any=2n;try{console.log(a+1);}catch(e:any){console.log(e instanceof TypeError);}" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "addition_hooks",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let trace=\"\";const a:any={valueOf(){trace+=\"a\";return 2;}};const b:any={valueOf(){trace+=\"b\";return 3;}};console.log(a+b,trace);" },
            "5 ab\n",
            false,
            true
        },
        new object[]
        {
            "nullish",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const n:any=null;const u:any=undefined;console.log(n==u,n===u,n==0,u==false);" },
            "true false false false\n",
            false,
            true
        },
        new object[]
        {
            "identity",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let count=0;const a:any={valueOf(){count++;return 1;}};const b:any={};console.log(a==a,a==b,a==null,a===a,count);" },
            "true false false true 0\n",
            false,
            true
        },
        new object[]
        {
            "boxed_equality",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const s:any=new String(\"hello\");const n:any=new Number(2);console.log(s==\"hello\",n==2,s==s,s===s);" },
            "true true true true\n",
            false,
            true
        },
        new object[]
        {
            "typeof_primitives",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(typeof null,typeof undefined,typeof true,typeof 1,typeof \"s\",typeof 1n,typeof Symbol(\"s\"));" },
            "object undefined boolean number string bigint symbol\n",
            false,
            true
        },
        new object[]
        {
            "typeof_callables",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function f(){}const g:any=f;console.log(typeof g,typeof g.bind(null),typeof g.call,typeof g.apply,typeof g.bind);" },
            "function function function function function\n",
            false,
            true
        },
        new object[]
        {
            "typeof_collections",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(typeof [],typeof new Map(),typeof new Set(),typeof /x/,typeof new Date(0));" },
            "object object object object object\n",
            false,
            true
        },
        new object[]
        {
            "promise_callbacks",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "new Promise((resolve:any,reject:any)=>{console.log(typeof resolve,typeof reject);resolve(1);});" },
            "function function\n",
            false,
            true
        },
        new object[]
        {
            "instance_classes",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class A{}class B extends A{}const b:any=new B();console.log(b instanceof B,b instanceof A,b instanceof Object);" },
            "true true true\n",
            false,
            true
        },
        new object[]
        {
            "instance_functions",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function A(){}function B(){}const a:any=new (A as any)();console.log(a instanceof A,a instanceof B,a instanceof Object);" },
            "true false true\n",
            false,
            true
        },
        new object[]
        {
            "instance_primitives",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const n:any=2;const s:any=Symbol(\"s\");console.log(n instanceof Object,s instanceof Object,new Number(2) instanceof Number,Object(s) instanceof Symbol);" },
            "false false true true\n",
            false,
            true
        },
        new object[]
        {
            "instance_promise",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const p:any=Promise.resolve(1);console.log(p instanceof Promise,p instanceof Object);" },
            "true true\n",
            false,
            true
        },
        new object[]
        {
            "membership",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const p:any={x:1};const o:any=Object.create(p);o.y=2;console.log(\"x\" in o,\"y\" in o,\"z\" in o);" },
            "true true false\n",
            false,
            true
        },
        new object[]
        {
            "membership_symbol",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const s=Symbol(\"s\");const o:any={[s]:1};console.log(s in o,Symbol(\"s\") in o);" },
            "true false\n",
            false,
            true
        },
        new object[]
        {
            "membership_array",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a:any=[1,,3];console.log(0 in a,1 in a,2 in a,\"length\" in a);" },
            "true false true true\n",
            false,
            true
        },
        new object[]
        {
            "membership_invalid",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "for(const x of [null,undefined,1,\"x\",true]){try{console.log(\"x\" in (x as any));}catch(e:any){console.log(e instanceof TypeError);}}" },
            "true\ntrue\ntrue\ntrue\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "proxy_has",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let count=0;const p:any=new Proxy({x:1},{has(t:any,k:any){count++;return Reflect.has(t,k);}});console.log(\"x\" in p,\"y\" in p,count);" },
            "true false 2\n",
            false,
            false
        },
        new object[]
        {
            "proxy_ordinary",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const p:any=new Proxy({x:1},{});console.log(\"x\" in p,\"y\" in p);" },
            "true false\n",
            false,
            false
        },
        new object[]
        {
            "proxy_invariant",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const t:any={};Object.defineProperty(t,\"x\",{value:1,configurable:false});const p:any=new Proxy(t,{has(){return false;}});try{console.log(\"x\" in p);}catch(e:any){console.log(e instanceof TypeError);}" },
            "true\n",
            false,
            false
        },
        new object[]
        {
            "generator",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function* values(){const a:any=\"10\";const b:any=\"2\";yield a<b;yield a<=b;yield a>b;yield a>=b;yield typeof a;yield a+2;}console.log([...values()].join(\",\"));" },
            "true,true,false,false,string,102\n",
            false,
            true
        },
        new object[]
        {
            "async",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "async function run(){const a:any=await Promise.resolve(\"10\");const b:any=\"2\";console.log(a<b,a<=b,a>b,a>=b,typeof a,a+2);}run();" },
            "true true false false string 102\n",
            false,
            true
        },
        new object[]
        {
            "hosted_operators",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export function inspect(a:any,b:any){return [a+b,a<b,a<=b,a==b,a===b,typeof a];}" },
            "",
            true,
            true
        },
        new object[]
        {
            "hosted_minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export const value=1;" },
            "",
            true,
            true
        },
    ];

    [Theory]
    [MemberData(nameof(OperatorMetadataPrograms))]
    public void Isolated_OperatorMetadata_PreservesOperatorsAndDeployment(
        string name, string entry, string[] paths, string[] sources, string expected, bool hosted, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        for (int i = 0; i < paths.Length; i++) tempDir.CreateFile(paths[i], sources[i]);
        var sourcePath = tempDir.GetPath(entry);
        var dllPath = tempDir.GetPath($"operators-metadata_{name}.dll");
        var deployment = standalone ? " --standalone" : "";
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{dllPath}\" --verify{deployment}{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }


    public static IEnumerable<object[]> ObjectWriteMetadataPrograms =>
    [
        new object[]
        {
            "minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(1);" },
            "1\n",
            false,
            true
        },
        new object[]
        {
            "named",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const o:any={x:1}; o.x=2; o.y=3; console.log(o.x,o.y);" },
            "2 3\n",
            false,
            true
        },
        new object[]
        {
            "computed",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const o:any={}; const k:any=\"x\"; o[k]=4; o[2]=5; console.log(o.x,o[\"2\"]);" },
            "4 5\n",
            false,
            true
        },
        new object[]
        {
            "fields",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Item { x=1; } const o:any=new Item(); o.x=2; o[\"x\"]=3; o.extra=4; console.log(o.x,o.extra);" },
            "3 4\n",
            false,
            true
        },
        new object[]
        {
            "field_setter",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Item { x=1; set value(v:number){this.x=v*2;} } const o:any=new Item(); o.value=3; console.log(o.x); o[\"value\"]=4; console.log(o.x);" },
            "6\n8\n",
            false,
            true
        },
        new object[]
        {
            "own_setter",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const o:any={y:0,set x(v:any){(this as any).y=v;}}; o.x=3; console.log(o.y); o[\"x\"]=4; console.log(o.y);" },
            "3\n4\n",
            false,
            true
        },
        new object[]
        {
            "frozen",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const o:any=Object.freeze({x:1}); o.x=2; o[\"x\"]=3; o.y=4; console.log(o.x,o.y);" },
            "1 undefined\n",
            false,
            true
        },
        new object[]
        {
            "sealed",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const o:any=Object.seal({x:1}); o.x=2; o[\"x\"]=3; o.y=4; console.log(o.x,o.y);" },
            "3 undefined\n",
            false,
            true
        },
        new object[]
        {
            "nonextensible",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const o:any=Object.preventExtensions({x:1}); o[\"x\"]=2; o[\"y\"]=3; console.log(o.x,o.y);" },
            "2 undefined\n",
            false,
            true
        },
        new object[]
        {
            "strict_named",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function run(){\"use strict\";const o:any=Object.freeze({x:1});try{o.x=2;}catch(e:any){console.log(e instanceof TypeError);}console.log(o.x);}run();" },
            "true\n1\n",
            false,
            true
        },
        new object[]
        {
            "strict_index",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function run(){\"use strict\";const o:any=Object.freeze({x:1});const key:any=\"x\";try{o[key]=2;}catch(e:any){console.log(e instanceof TypeError);}console.log(o.x);}run();" },
            "true\n1\n",
            false,
            true
        },
        new object[]
        {
            "strict_fields",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Item{x=1;}function run(){\"use strict\";const o:any=new Item();Object.freeze(o);try{o.x=2;}catch(e:any){console.log(e instanceof TypeError);}console.log(o.x);}run();" },
            "true\n1\n",
            false,
            true
        },
        new object[]
        {
            "strict_symbol",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function run(){\"use strict\";const s=Symbol(\"x\");const o:any={[s]:1};Object.freeze(o);try{o[s]=2;}catch(e:any){console.log(e instanceof TypeError);}console.log(o[s]);}run();" },
            "true\n1\n",
            false,
            true
        },
        new object[]
        {
            "strict_new",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function run(){\"use strict\";const o:any=Object.preventExtensions({x:1});try{o.y=2;}catch(e:any){console.log(e instanceof TypeError);}console.log(o.y);}run();" },
            "true\nundefined\n",
            false,
            true
        },
        new object[]
        {
            "symbol",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const s=Symbol(\"x\");const o:any={};o[s]=1;o[s]=2;console.log(o[s],Object.getOwnPropertySymbols(o).length);" },
            "2 1\n",
            false,
            true
        },
        new object[]
        {
            "sparse",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a:any=[1];a[3]=4;console.log(a.length,a.join(\",\"));" },
            "4 1,,,4\n",
            false,
            true
        },
        new object[]
        {
            "array_length",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a:any=[1,2,3];a.length=1;console.log(a.length,a[1]);a.length=3;console.log(a.length,a.join(\",\"));" },
            "1 undefined\n3 1,,\n",
            false,
            true
        },
        new object[]
        {
            "array_named",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a:any=[1,2];a[\"label\"]=\"x\";a[4294967295]=7;console.log(a.length,a.label,a[4294967295]);" },
            "2 x 7\n",
            false,
            true
        },
        new object[]
        {
            "arguments",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function f(){const a:any=arguments;a.length=7;console.log(a.length);a[0]=9;console.log(a[0]);}f();" },
            "7\n9\n",
            false,
            true
        },
        new object[]
        {
            "function_property",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function f(){}const a:any=f;a.x=3;a[\"y\"]=4;console.log(a.x,a.y);" },
            "3 4\n",
            false,
            true
        },
        new object[]
        {
            "constructor_property",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a:any=Object;a.writeProbe=5;console.log(a.writeProbe);delete a.writeProbe;" },
            "5\n",
            false,
            true
        },
        new object[]
        {
            "date_property",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a:any=new Date(0);a.x=6;a[\"y\"]=7;console.log(a.x,a.y,a.getTime());" },
            "6 7 0\n",
            false,
            true
        },
        new object[]
        {
            "regexp_lastindex",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const r:any=/a/g;r.lastIndex=\"1\";console.log(r.test(\"ba\"),r.lastIndex);r[\"lastIndex\"]=0;console.log(r.lastIndex);" },
            "true 2\n0\n",
            false,
            true
        },
        new object[]
        {
            "promise_property",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const p:any=Promise.resolve(1);p.x=3;p[\"y\"]=4;console.log(p.x,p.y);" },
            "3 4\n",
            false,
            true
        },
        new object[]
        {
            "globalthis",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const g:any=globalThis;g.writeProbe=7;g[\"writeProbe\"]=8;console.log(g.writeProbe);delete g.writeProbe;" },
            "8\n",
            false,
            true
        },
        new object[]
        {
            "buffer",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const b:any=Buffer.from([1,2]);b[0]=257;b[1]=3;console.log(b[0],b[1],b.length);" },
            "1 3 2\n",
            false,
            true
        },
        new object[]
        {
            "typedarray",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a:any=new Uint8Array([1,2]);a[0]=258;a[1]=3;console.log(a[0],a[1],a.length);" },
            "2 3 2\n",
            false,
            true
        },
        new object[]
        {
            "abort_onabort",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const c=new AbortController();const s:any=c.signal;let n=0;s.onabort=()=>{n++;};c.abort();console.log(n,s.aborted);" },
            "1 true\n",
            false,
            true
        },
        new object[]
        {
            "commonjs",
            "main.cjs",
            new string[] { "dep.cjs", "main.cjs" },
            new string[] { "exports.x=1;exports.x=9;", "const d=require(\"./dep.cjs\");console.log(d.x);" },
            "9\n",
            false,
            true
        },
        new object[]
        {
            "module_exports",
            "main.cjs",
            new string[] { "dep.cjs", "main.cjs" },
            new string[] { "module.exports={x:8};", "const d=require(\"./dep.cjs\");console.log(d.x);" },
            "8\n",
            false,
            true
        },
        new object[]
        {
            "proxy_set",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let n=0;const target:any={x:1};const p:any=new Proxy(target,{set(t:any,k:any,v:any,r:any){n++;return Reflect.set(t,k,v,r);}});p.x=2;p[\"x\"]=3;console.log(target.x,n);" },
            "3 2\n",
            false,
            false
        },
        new object[]
        {
            "proxy_forward",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const target:any={x:1};const p:any=new Proxy(target,{});p.x=4;p[\"y\"]=5;console.log(target.x,target.y);" },
            "4 5\n",
            false,
            false
        },
        new object[]
        {
            "proxy_inherited",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let n=0;const p:any=new Proxy({},{set(t:any,k:any,v:any,r:any){n++;return Reflect.set(t,k,v,r);}});const o:any=Object.create(p);o.x=6;console.log(o.x,n,Object.hasOwn(o,\"x\"));" },
            "6 1 true\n",
            false,
            false
        },
        new object[]
        {
            "proxy_strict",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function run(){\"use strict\";const p:any=new Proxy({x:1},{set(){return false;}});try{p.x=2;}catch(e:any){console.log(e instanceof TypeError);}console.log(p.x);}run();" },
            "true\n1\n",
            false,
            false
        },
        new object[]
        {
            "key_coercion",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let n=0;const k:any={toString(){n++;return \"x\";}};const o:any={};o[k]=5;console.log(o.x,n);" },
            "5 1\n",
            false,
            true
        },
        new object[]
        {
            "hosted_write",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export function write(value:any,key:any,next:any){value[key]=next;return value[key];}" },
            "",
            true,
            true
        },
        new object[]
        {
            "hosted_minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export const value=1;" },
            "",
            true,
            true
        },
    ];

    [Theory]
    [MemberData(nameof(ObjectWriteMetadataPrograms))]
    public void Isolated_ObjectWriteMetadata_PreservesWritingAndDeployment(
        string name, string entry, string[] paths, string[] sources, string expected, bool hosted, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        for (int i = 0; i < paths.Length; i++) tempDir.CreateFile(paths[i], sources[i]);
        var sourcePath = tempDir.GetPath(entry);
        var dllPath = tempDir.GetPath($"object-write-metadata_{name}.dll");
        var deployment = standalone ? " --standalone" : "";
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{dllPath}\" --verify{deployment}{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }


    public static IEnumerable<object[]> ObjectReadMetadataPrograms =>
    [
        new object[]
        {
            "minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(1);" },
            "1\n",
            false,
            true
        },
        new object[]
        {
            "named",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const o:any={x:1}; console.log(o.x,o.missing); o.x=2; console.log(o.x);" },
            "1 undefined\n2\n",
            false,
            true
        },
        new object[]
        {
            "computed",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const o:any={x:3}; const key:any=\"x\"; console.log(o[key],o[\"missing\"]);" },
            "3 undefined\n",
            false,
            true
        },
        new object[]
        {
            "fields",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Item { x=4; get doubled(){return this.x*2;} } const o:any=new Item(); console.log(o.x,o[\"doubled\"]); o.x=5; console.log(o.doubled);" },
            "4 8\n10\n",
            false,
            true
        },
        new object[]
        {
            "getter",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let n=0; const o:any={get x(){n++;return n;}}; console.log(o.x,o[\"x\"],n);" },
            "1 2 2\n",
            false,
            true
        },
        new object[]
        {
            "getter_receiver",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const p:any={get x(){return (this as any).y;}}; const o:any=Object.create(p); o.y=7; console.log(o.x,o[\"x\"]);" },
            "7 7\n",
            false,
            true
        },
        new object[]
        {
            "symbol",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const s=Symbol(\"read\"); const o:any={[s]:8}; console.log(o[s]); o[s]=9; console.log(o[s]);" },
            "8\n9\n",
            false,
            true
        },
        new object[]
        {
            "list",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a:any=[1,2,3]; const get=(o:any,k:any)=>o[k]; console.log(get(a,\"length\"),get(a,1),get(a,\"join\").call(a,\"-\"));" },
            "3 2 1-2-3\n",
            false,
            true
        },
        new object[]
        {
            "sparse",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a:any=new Array(4); a[2]=6; console.log(a.length,a[0],a[2],a[3]); console.log([...a].join(\",\"));" },
            "4 undefined 6 undefined\n,,6,\n",
            false,
            true
        },
        new object[]
        {
            "list_mutation",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a:any=[1,2]; const old=Array.prototype.join; Array.prototype.join=function(){return \"custom\";}; console.log(a[\"join\"]()); Array.prototype.join=old; console.log(a.join(\",\"));" },
            "custom\n1,2\n",
            false,
            true
        },
        new object[]
        {
            "list_descriptor",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a:any=[1,2]; Object.defineProperty(a,\"join\",{value:function(){return \"own\";},configurable:true}); console.log(a[\"join\"]());" },
            "own\n",
            false,
            true
        },
        new object[]
        {
            "string",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const s:any=\"abc\"; console.log(s.length,s[1],s[\"toUpperCase\"]());" },
            "3 b ABC\n",
            false,
            true
        },
        new object[]
        {
            "boxed",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const s:any=new String(\"abc\"); const n:any=new Number(4); const b:any=new Boolean(true); console.log(s.length,s[1],n.valueOf(),b.valueOf());" },
            "3 b 4 true\n",
            false,
            true
        },
        new object[]
        {
            "map",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const m:any=new Map([[\"x\",1]]); console.log(m.size,m[\"get\"](\"x\")); m.set(\"y\",2); console.log(m.size);" },
            "1 1\n2\n",
            false,
            true
        },
        new object[]
        {
            "set",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const s:any=new Set([1,2]); console.log(s.size,s[\"has\"](2));" },
            "2 true\n",
            false,
            true
        },
        new object[]
        {
            "weak",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const key={}; const m:any=new WeakMap(); const s:any=new WeakSet(); m.set(key,3); s.add(key); console.log(m[\"get\"](key),s[\"has\"](key)); const r:any=new WeakRef(key); console.log(r[\"deref\"]()===key);" },
            "3 true\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "bigint",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const x:any=123n; console.log(x[\"toString\"](),x[\"valueOf\"]()===123n);" },
            "123 true\n",
            false,
            true
        },
        new object[]
        {
            "regexp",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const r:any=/a/gi; console.log(r.source,r.flags,r.global,r.ignoreCase,r[\"test\"](\"A\"));" },
            "a gi true true true\n",
            false,
            true
        },
        new object[]
        {
            "regexp_symbol",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const r:any=/a/g; const m=r[Symbol.match](\"aba\"); console.log(m.join(\",\"),r.lastIndex); console.log(/a/[Symbol.search](\"ba\"));" },
            "a,a 0\n1\n",
            false,
            true
        },
        new object[]
        {
            "arraybuffer",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a:any=new ArrayBuffer(8); const b:any=new SharedArrayBuffer(4); console.log(a[\"byteLength\"],b.byteLength);" },
            "8 4\n",
            false,
            true
        },
        new object[]
        {
            "dataview",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a:any=new ArrayBuffer(8); const v:any=new DataView(a,2,4); v.setUint8(0,9); console.log(v.byteLength,v.byteOffset,v.buffer===a,v[\"getUint8\"](0));" },
            "4 2 true 9\n",
            false,
            true
        },
        new object[]
        {
            "arguments",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function f(a:any,b:any){const v:any=arguments; console.log(v.length,v[0],v[1]);} f(3,4);" },
            "2 3 4\n",
            false,
            true
        },
        new object[]
        {
            "functions",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function named(a:any,b:any){return a+b;} const f:any=named; console.log(f.name,f[\"length\"],f[\"call\"](null,2,3));" },
            "named 2 5\n",
            false,
            true
        },
        new object[]
        {
            "namespace",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "namespace N {export const value=6;} const n:any=N; console.log(n.value,n[\"value\"]);" },
            "6 6\n",
            false,
            true
        },
        new object[]
        {
            "globalthis",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const g:any=globalThis; g.readProbe=7; console.log(g.readProbe,g[\"readProbe\"]); delete g.readProbe;" },
            "7 7\n",
            false,
            true
        },
        new object[]
        {
            "esm",
            "main.ts",
            new string[] { "dep.ts", "main.ts" },
            new string[] { "export const value=8;", "import * as ns from \"./dep\"; const n:any=ns; console.log(n.value,n[\"value\"]);" },
            "8 8\n",
            false,
            true
        },
        new object[]
        {
            "commonjs",
            "main.cjs",
            new string[] { "dep.cjs", "main.cjs" },
            new string[] { "exports.value=9;", "const n=require(\"./dep.cjs\"); console.log(n.value,n[\"value\"]);" },
            "9 9\n",
            false,
            true
        },
        new object[]
        {
            "proxy_get",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const p:any=new Proxy({x:3},{get(target:any,key:any,receiver:any){return key===\"x\"?target.x+1:Reflect.get(target,key,receiver);}}); console.log(p.x,p[\"x\"]);" },
            "4 4\n",
            false,
            false
        },
        new object[]
        {
            "proxy_receiver",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const target:any={get x(){return (this as any).y;}}; const p:any=new Proxy(target,{get(t:any,k:any,r:any){if(k===\"y\")return 5;return Reflect.get(t,k,r);}}); console.log(p.x,p[\"x\"]);" },
            "5 5\n",
            false,
            false
        },
        new object[]
        {
            "hosted_read",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export function read(value:any,key:any){return value[key];} export function length(value:any){return value.length;}" },
            "",
            true,
            true
        },
        new object[]
        {
            "hosted_minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export const value=1;" },
            "",
            true,
            true
        },
        new object[]
        {
            "date_named",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const d:any=new Date(0); console.log(d.getUTCFullYear(),d.toISOString());" },
            "1970 1970-01-01T00:00:00.000Z\n",
            false,
            true
        },
        new object[]
        {
            "promise_reads",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "new Promise((resolve:any)=>{console.log(resolve[\"name\"],resolve[\"length\"]); resolve(1);}); const p:any=Promise.resolve(5); p[\"then\"]((x:any)=>console.log(x));" },
            " 1\n5\n",
            false,
            true
        },
        new object[]
        {
            "buffer_named",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const b:any=Buffer.from(\"abc\"); console.log(b.length,b[1],b.toString());" },
            "3 98 abc\n",
            false,
            true
        },
        new object[]
        {
            "stats_module",
            "main.cjs",
            new string[] { "main.cjs" },
            new string[] { "const fs=require(\"fs\"); const s=fs.statSync(\".\"); console.log(s.isDirectory(),typeof s.size);" },
            "true number\n",
            false,
            true
        },
        new object[]
        {
            "typedarray_named",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a:any=new Uint8Array([2,4]); console.log(a.length,a[0],a[1],a.byteLength,a.byteOffset); console.log(a.join(\"-\"));" },
            "2 2 4 2 0\n2-4\n",
            false,
            true
        },
        new object[]
        {
            "abort_after",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const c=new AbortController(); const s:any=c.signal; console.log(s.aborted); c.abort(\"stop\"); console.log(s.aborted,s.reason);" },
            "false\ntrue stop\n",
            false,
            true
        },
    ];

    [Theory]
    [MemberData(nameof(ObjectReadMetadataPrograms))]
    public void Isolated_ObjectReadMetadata_PreservesReadingAndDeployment(
        string name, string entry, string[] paths, string[] sources, string expected, bool hosted, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        for (int i = 0; i < paths.Length; i++) tempDir.CreateFile(paths[i], sources[i]);
        var sourcePath = tempDir.GetPath(entry);
        var dllPath = tempDir.GetPath($"object-read-metadata_{name}.dll");
        var deployment = standalone ? " --standalone" : "";
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{dllPath}\" --verify{deployment}{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }


    public static IEnumerable<object[]> ObjectDeletionMetadataPrograms =>
    [
        new object[]
        {
            "minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(1);" },
            "1\n",
            false,
            true
        },
        new object[]
        {
            "named",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: { name?: string } = { name: \"test\" };\nlet result: boolean = delete obj.name;\nconsole.log(result);\nconsole.log(obj.name === null || obj.name === undefined);" },
            "true\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "computed",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: { [key: string]: any } = { key: \"value\" };\nlet result: boolean = delete obj[\"key\"];\nconsole.log(result);\nconsole.log(obj[\"key\"] === null || obj[\"key\"] === undefined);" },
            "true\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "existing",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: { foo?: string } = { foo: \"bar\" };\nconsole.log(obj.foo);\nlet result: boolean = delete obj.foo;\nconsole.log(result);\nconsole.log(obj.foo === null || obj.foo === undefined);" },
            "bar\ntrue\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "frozen",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj = { name: \"test\" };\nObject.freeze(obj);\nlet result: boolean = delete obj.name;\nconsole.log(result);\nconsole.log(obj.name);" },
            "false\ntest\n",
            false,
            true
        },
        new object[]
        {
            "sealed",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj = { name: \"test\" };\nObject.seal(obj);\nlet result: boolean = delete obj.name;\nconsole.log(result);\nconsole.log(obj.name);" },
            "false\ntest\n",
            false,
            true
        },
        new object[]
        {
            "canonical_array",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const aliases: string[] = [\"01\", \"+1\", \" 1 \"];\nconst values: any = [\"zero\", \"one\"];\nfor (const key of aliases) {\n    Object.defineProperty(values, key, {\n        value: key,\n        writable: true,\n        enumerable: true,\n        configurable: true\n    });\n}\n\nconsole.log(values[1]);\nfor (const key of aliases) {\n    const descriptor = Object.getOwnPropertyDescriptor(values, key)!;\n    console.log(values[key] === key,\n        descriptor.value === key,\n        descriptor.enumerable,\n        descriptor.configurable);\n}\n\nconst sealed: any = [\"zero\", \"one\"];\nObject.seal(sealed);\nfor (const key of aliases) {\n    console.log(\n        Object.getOwnPropertyDescriptor(sealed, key) === undefined,\n        delete sealed[key],\n        sealed[1]);\n}" },
            "one\ntrue true true true\ntrue true true true\ntrue true true true\ntrue true one\ntrue true one\ntrue true one\n",
            false,
            true
        },
        new object[]
        {
            "multiple",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = { a: 1, b: 2, c: 3 };\ndelete obj.a;\ndelete obj.c;\nconsole.log(obj.a === null || obj.a === undefined);\nconsole.log(obj.b);\nconsole.log(obj.c === null || obj.c === undefined);" },
            "true\n2\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "expression",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: { prop?: string } = { prop: \"value\" };\nif (delete obj.prop) {\n    console.log(\"deleted\");\n}" },
            "deleted\n",
            false,
            true
        },
        new object[]
        {
            "symbol",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let sym = Symbol(\"key\");\nlet obj: { [key: symbol]: string } = {};\nobj[sym] = \"value\";\nconsole.log(obj[sym]);\ndelete obj[sym];\nconsole.log(obj[sym]);" },
            "value\nundefined\n",
            false,
            true
        },
        new object[]
        {
            "proxy_delete",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const target: any = { x: 1, y: 2 };\nlet deletedProp = \"\";\nconst handler = {\n    deleteProperty(target: any, prop: string) {\n        deletedProp = prop;\n        delete target[prop];\n        return true;\n    }\n};\nconst p: any = new Proxy(target, handler);\ndelete p.x;\nconsole.log(deletedProp);\nconsole.log(target.x);" },
            "x\nundefined\n",
            false,
            false
        },
        new object[]
        {
            "proxy_forward",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const target: any = { attr: 1 };\nconst proxy: any = new Proxy(target, {});\nproxy.attr = \"changed\";\nconsole.log(proxy.attr);\nconsole.log(target.attr);\nproxy.attr = 1;\nconsole.log(delete proxy.attr);\nconsole.log(proxy.hasOwnProperty(\"attr\"));\nconsole.log(target.hasOwnProperty(\"attr\"));" },
            "changed\nchanged\ntrue\nfalse\nfalse\n",
            false,
            false
        },
        new object[]
        {
            "reflect",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = { x: 1, y: 2 };\nlet result: boolean = Reflect.deleteProperty(obj, \"x\");\nconsole.log(result);\nconsole.log(Reflect.has(obj, \"x\"));" },
            "true\nfalse\n",
            false,
            true
        },
        new object[]
        {
            "reflect_missing",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = { x: 1 };\nlet result: boolean = Reflect.deleteProperty(obj, \"missing\");\nconsole.log(result);" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "reflect_frozen",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = { x: 1 };\nObject.freeze(obj);\nconsole.log(Reflect.deleteProperty(obj, \"x\"));\nconsole.log(obj.x);" },
            "false\n1\n",
            false,
            true
        },
        new object[]
        {
            "packed",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a: any[] = [];\na[0] = 1; a[1] = 2; a[2] = 3;\ndelete a[1];\na.length = 2;\na.length = 4;\nconsole.log([a[0], 1 in a, a[1], 2 in a, 3 in a, a.length].join(\"|\"));" },
            "1|false||false|false|4\n",
            false,
            true
        },
        new object[]
        {
            "spread",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function retain(value: any): any { return value; }\nfunction work(n: number): void {\n    const removed = { a: n, b: n + 1 };\n    delete removed.a;\n    const deletedResult = { ...removed };\n    console.log(Object.keys(retain(deletedResult)).join(\",\"));\n    const incremented = { a: n };\n    console.log(incremented.a++);\n    const postResult = { ...incremented };\n    console.log(retain(postResult).a);\n    console.log(++incremented.a);\n    const preResult = { ...incremented };\n    console.log(retain(preResult).a);\n}\nwork(1);" },
            "b\n1\n2\n3\n3\n",
            false,
            true
        },
        new object[]
        {
            "getter_snapshot",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const proto: any = { b: 9 };\nconst obj: any = Object.create(proto);\nobj.a = {\n    toJSON: function (): number {\n        delete obj.b;\n        return 1;\n    }\n};\nobj.b = 2;\nconsole.log(JSON.stringify(obj));" },
            "{\"a\":1,\"b\":9}\n",
            false,
            true
        },
        new object[]
        {
            "builtin",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const prototypes: any[] = [Object.prototype, Array.prototype, String.prototype,\n    Number.prototype, Boolean.prototype, BigInt.prototype, Symbol.prototype,\n    Function.prototype, Error.prototype, Promise.prototype];\nconst names = [\"valueOf\", \"map\", \"trim\", \"toFixed\", \"valueOf\", \"valueOf\",\n    \"valueOf\", \"bind\", \"toString\", \"then\"];\nfor (let i = 0; i < prototypes.length; i++) {\n    const p: any = prototypes[i];\n    const name = names[i];\n    console.log(delete p[name]);\n    console.log(Object.prototype.hasOwnProperty.call(p, name));\n    Object.defineProperty(p, name, {\n        value: 17, writable: true, enumerable: false, configurable: true\n    });\n    console.log(p[name]);\n    console.log(delete p[name]);\n    console.log(Object.prototype.hasOwnProperty.call(p, name));\n    p[name] = 23;\n    console.log(p[name]);\n    Object.defineProperty(p, name, { value: 23, writable: false, configurable: false });\n    console.log(delete p[name]);\n    let rejected = false;\n    try { Object.defineProperty(p, name, { value: 99 }); }\n    catch (e) { rejected = true; }\n    console.log(rejected);\n    console.log(p[name]);\n}" },
            "true\nfalse\n17\ntrue\nfalse\n23\nfalse\ntrue\n23\ntrue\nfalse\n17\ntrue\nfalse\n23\nfalse\ntrue\n23\ntrue\nfalse\n17\ntrue\nfalse\n23\nfalse\ntrue\n23\ntrue\nfalse\n17\ntrue\nfalse\n23\nfalse\ntrue\n23\ntrue\nfalse\n17\ntrue\nfalse\n23\nfalse\ntrue\n23\ntrue\nfalse\n17\ntrue\nfalse\n23\nfalse\ntrue\n23\ntrue\nfalse\n17\ntrue\nfalse\n23\nfalse\ntrue\n23\ntrue\nfalse\n17\ntrue\nfalse\n23\nfalse\ntrue\n23\ntrue\nfalse\n17\ntrue\nfalse\n23\nfalse\ntrue\n23\ntrue\nfalse\n17\ntrue\nfalse\n23\nfalse\ntrue\n23\n",
            false,
            true
        },
        new object[]
        {
            "reinsert",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const o:any={a:1,b:2,c:3}; console.log(delete o.b); o.d=4; o.b=5; console.log(Object.keys(o).join(','),o.b);" },
            "true\na,c,d,b 5\n",
            false,
            true
        },
        new object[]
        {
            "promise_callbacks",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "new Promise((resolve:any,reject:any)=>{for(const f of [resolve,reject]){console.log(delete f['name'],Object.hasOwn(f,'name'));console.log(delete f['length'],Object.hasOwn(f,'length'));} resolve(1);});" },
            "true false\ntrue false\ntrue false\ntrue false\n",
            false,
            true
        },
        new object[]
        {
            "esm",
            "main.ts",
            new string[] { "dep.ts", "main.ts" },
            new string[] { "export const value:any={a:1,b:2};", "import {value} from './dep'; console.log(delete value.a,Object.keys(value).join(','));" },
            "true b\n",
            false,
            true
        },
        new object[]
        {
            "commonjs",
            "main.cjs",
            new string[] { "dep.cjs", "main.cjs" },
            new string[] { "exports.value={a:1,b:2};", "const dep=require('./dep.cjs'); console.log(delete dep.value.a,Object.keys(dep.value).join(','));" },
            "true b\n",
            false,
            true
        },
        new object[]
        {
            "hosted_deletion",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export function remove(value:any,key:any){return delete value[key];} export function strict(value:any){'use strict';return delete value.x;}" },
            "",
            true,
            true
        },
        new object[]
        {
            "hosted_minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export const value=1;" },
            "",
            true,
            true
        },
        new object[]
        {
            "accessor_delete",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let calls=0; const o:any={}; Object.defineProperty(o,'x',{get(){calls++;return 1;},configurable:true,enumerable:true}); console.log(delete o.x,Object.hasOwn(o,'x'),calls);" },
            "true false 0\n",
            false,
            true
        },
        new object[]
        {
            "strict_frozen_function",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function check(){'use strict'; const o:any=Object.freeze({x:1}); try {delete o.x;} catch(e:any) {console.log(e instanceof TypeError,e.message.includes('Cannot delete property'));} console.log(o.x);} check();" },
            "true true\n1\n",
            false,
            true
        },
        new object[]
        {
            "strict_index_function",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function check(){'use strict'; const o:any=Object.freeze({x:1}); const k:any='x'; try {delete o[k];} catch(e:any) {console.log(e instanceof TypeError,e.message.includes('Cannot delete property'));} console.log(o.x);} check();" },
            "true true\n1\n",
            false,
            true
        },
        new object[]
        {
            "strict_set_function",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function check(){'use strict'; const o:any=Object.freeze({x:1}); try {o.x=2;} catch(e:any) {console.log(e instanceof TypeError,e.message.includes('Cannot assign to read only property'));} console.log(o.x);} check();" },
            "true true\n1\n",
            false,
            true
        },
        new object[]
        {
            "strict_extend_function",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function check(){'use strict'; const o:any=Object.seal({x:1}); try {o.y=2;} catch(e:any) {console.log(e instanceof TypeError,e.message.includes('Cannot add property'));} console.log(o.x);} check();" },
            "true true\n1\n",
            false,
            true
        },
    ];

    [Theory]
    [MemberData(nameof(ObjectDeletionMetadataPrograms))]
    public void Isolated_ObjectDeletionMetadata_PreservesDeletionAndDeployment(
        string name, string entry, string[] paths, string[] sources, string expected, bool hosted, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        for (int i = 0; i < paths.Length; i++) tempDir.CreateFile(paths[i], sources[i]);
        var sourcePath = tempDir.GetPath(entry);
        var dllPath = tempDir.GetPath($"object-deletion-metadata_{name}.dll");
        var deployment = standalone ? " --standalone" : "";
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{dllPath}\" --verify{deployment}{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }


    public static IEnumerable<object[]> ObjectConstructionMetadataPrograms =>
    [
        new object[]
        {
            "minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(1);" },
            "1\n",
            false,
            true
        },
        new object[]
        {
            "plain",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function copy(first: any, second: any): any {\n    return { before: 0, ...first, b: 8, ...second, after: 9 };\n}\nconst first: any = { a: 1, b: 2, \"\": 3 };\nconst second: any = { b: 4, c: 5 };\nconst result: any = copy(first, second);\nconsole.log(Object.keys(result).join(\"|\"));\nconsole.log(result.a + result.b + result.c + result[\"\"]);\nresult.a = 10;\nfirst.b = 20;\nconsole.log(first.a);\nconsole.log(result.b);\nconsole.log(result === first);" },
            "before|a|b||c|after\n13\n1\n4\nfalse\n",
            false,
            true
        },
        new object[]
        {
            "numeric",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function copy(value: any): any { return { prefix: 0, ...value, suffix: 1 }; }\nconst value: any = { a: 1, \"10\": 10, \"2\": 2, \"01\": 1, \"4294967295\": 5 };\nconst result: any = copy(value);\nconsole.log(Object.keys(result).join(\",\"));\nconsole.log(result[\"10\"] + result[\"2\"] + result[\"01\"]);" },
            "2,10,prefix,a,01,4294967295,suffix\n13\n",
            false,
            true
        },
        new object[]
        {
            "descriptor",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function copy(value: any): any { return { ...value }; }\nconst value: any = { a: 1, b: 2 };\nlet calls: number = 0;\nObject.defineProperty(value, \"a\", {\n    enumerable: true,\n    get: function(): number {\n        calls = calls + 1;\n        value.b = 20;\n        value.late = 30;\n        return 10;\n    }\n});\nObject.defineProperty(value, \"hidden\", { value: 99, enumerable: false });\nconst result: any = copy(value);\nconsole.log(Object.keys(result).join(\",\"));\nconsole.log(result.a + result.b);\nconsole.log(calls);\nconsole.log(result.late === undefined);\nconsole.log(result.hidden === undefined);\nresult.a = 40;\nconsole.log(result.a);" },
            "a,b\n30\n1\ntrue\ntrue\n40\n",
            false,
            true
        },
        new object[]
        {
            "symbols",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function copy(value: any): any { return { ...value }; }\nconst value: any = { a: 1 };\nconst before: any = Object.getOwnPropertySymbols(value);\nconst again: any = Object.getOwnPropertySymbols(value);\nconsole.log(before === again);\nconsole.log(Object.keys(copy(value)).join(\",\"));\nconst visible: symbol = Symbol(\"visible\");\nconst hidden: symbol = Symbol(\"hidden\");\nvalue[visible] = 7;\nObject.defineProperty(value, hidden, { value: 8, enumerable: false });\nconst result: any = copy(value);\nconsole.log(before.length);\nconsole.log(Object.getOwnPropertySymbols(value).length);\nconsole.log(Object.getOwnPropertySymbols(result).length);\nconsole.log(result[visible]);\nconsole.log(result[hidden] === undefined);" },
            "false\na\n0\n2\n1\n7\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "wide",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function copy(value: { a: number, b: number, c: number }): any {\n    return { ...value, d: 4 };\n}\nconst wider = { a: 1, b: 2, c: 3, extra: 5 };\nconst result: any = copy(wider);\nconsole.log(Object.keys(result).join(\",\"));\nconsole.log(result.extra);" },
            "a,b,c,extra,d\n5\n",
            false,
            true
        },
        new object[]
        {
            "mutation",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function mutate(value: any): number { value.a = 7; value.c = 8; return 9; }\nfunction copy(value: any): any { return { ...value, middle: mutate(value), ...value }; }\nconst value: any = { a: 1, b: 2 };\nconst result: any = copy(value);\nconsole.log(Object.keys(result).join(\",\"));\nconsole.log(result.a + result.b + result.middle + result.c);" },
            "a,b,middle,c\n26\n",
            false,
            true
        },
        new object[]
        {
            "getter",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let base = { a: 1, b: 2 };\nlet obj = {\n    ...base,\n    _val: 10,\n    get doubled(): number {\n        return this._val * 2;\n    }\n};\nconsole.log(obj.a);\nconsole.log(obj.b);\nconsole.log(obj.doubled);" },
            "1\n2\n20\n",
            false,
            true
        },
        new object[]
        {
            "setter",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let base = { x: 100, y: 200 };\nlet obj = {\n    ...base,\n    _offset: 10,\n    get adjusted(): number {\n        return this.x + this._offset;\n    },\n    set offset(v: number) {\n        this._offset = v;\n    }\n};\nconsole.log(obj.x);\nconsole.log(obj.y);\nconsole.log(obj.adjusted);\nobj.offset = 50;\nconsole.log(obj.adjusted);" },
            "100\n200\n110\n150\n",
            false,
            true
        },
        new object[]
        {
            "object_fields",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let source = {\n    data: 42,\n    get computed(): number {\n        return this.data * 2;\n    }\n};\nlet target = {\n    ...source,\n    get tripled(): number {\n        return this.data * 3;\n    }\n};\nconsole.log(target.data);\nconsole.log(target.tripled);" },
            "42\n126\n",
            false,
            true
        },
        new object[]
        {
            "symbol_snapshot",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const removed = Symbol(\"removed\");\nconst watched = Symbol(\"watched\");\nconst hidden = Symbol(\"hidden\");\nconst added = Symbol(\"added\");\nconst events: string[] = [];\nconst source: any = {\n    get first(): number {\n        events.push(\"string\");\n        delete source[removed];\n        source[added] = 3;\n        return 1;\n    }\n};\nsource[removed] = 0;\nObject.defineProperty(source, watched, {\n    get(): number {\n        events.push(\"symbol\");\n        return 2;\n    },\n    enumerable: true,\n    configurable: true\n});\nObject.defineProperty(source, hidden, {\n    value: 4,\n    enumerable: false,\n    configurable: true\n});\n\nconst copy: any = { ...source };\nconsole.log(events.join(\",\"));\nconsole.log(copy.first, copy[watched], copy[hidden]);\nconsole.log(\n    Object.prototype.hasOwnProperty.call(copy, removed),\n    Object.prototype.hasOwnProperty.call(copy, added),\n    Object.prototype.hasOwnProperty.call(copy, watched),\n    Object.prototype.hasOwnProperty.call(copy, hidden));" },
            "string,symbol\n1 2 undefined\nfalse false true false\n",
            false,
            true
        },
        new object[]
        {
            "rest",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: { x: number, y: number, z: number } = { x: 1, y: 2, z: 3 };\nlet { x, ...rest }: { x: number, y: number, z: number } = obj;\nconsole.log(x);\nconsole.log(rest.y);\nconsole.log(rest.z);" },
            "1\n2\n3\n",
            false,
            true
        },
        new object[]
        {
            "rest_multiple",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let data: { id: number, name: string, age: number, city: string } = { id: 1, name: \"Alice\", age: 30, city: \"NYC\" };\nlet { id, name, ...others }: { id: number, name: string, age: number, city: string } = data;\nconsole.log(id);\nconsole.log(name);\nconsole.log(others.age);\nconsole.log(others.city);" },
            "1\nAlice\n30\nNYC\n",
            false,
            true
        },
        new object[]
        {
            "computed",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let key: string = \"added\";\nlet base: { x: number } = { x: 1 };\nlet obj: any = { ...base, [key]: 2 };\nconsole.log(obj.x);\nconsole.log(obj[\"added\"]);" },
            "1\n2\n",
            false,
            true
        },
        new object[]
        {
            "rest_assignment",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let pa: number, pb: number, rr: any;\n({ a: pa, b: pb, ...rr } = { a: 1, b: 2, z: 9 });\nconsole.log(pa, pb, JSON.stringify(rr));\nconst src: any = { p: 3 };\nlet ox, oy;\n({ p: ox, q: oy = 4 } = src);\nconsole.log(ox, oy);" },
            "1 2 {\"z\":9}\n3 4\n",
            false,
            true
        },
        new object[]
        {
            "proxy_snapshot",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const target: any = { a: 1, b: 2 };\nlet ownKeysCount: number = 0;\nlet descriptorCount: number = 0;\nlet getCount: number = 0;\nconst proxy: any = new Proxy(target, {\n    ownKeys(inner: any): any[] {\n        ownKeysCount = ownKeysCount + 1;\n        return Reflect.ownKeys(inner);\n    },\n    getOwnPropertyDescriptor(inner: any, key: any): any {\n        descriptorCount = descriptorCount + 1;\n        return Reflect.getOwnPropertyDescriptor(inner, key);\n    },\n    get(inner: any, key: any): any {\n        getCount = getCount + 1;\n        return inner[key];\n    }\n});\nconst result: any = { ...proxy };\nconsole.log(result.a);\nconsole.log(result.b);\nconsole.log(ownKeysCount);\nconsole.log(descriptorCount);\nconsole.log(getCount);" },
            "1\n2\n1\n2\n2\n",
            false,
            false
        },
        new object[]
        {
            "json_class",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Person {\n    name: string;\n    age: number;\n    constructor(name: string, age: number) {\n        this.name = name;\n        this.age = age;\n    }\n}\nlet p: Person = new Person(\"Bob\", 25);\nlet result: string = JSON.stringify(p);\nconsole.log(result);" },
            "{\"name\":\"Bob\",\"age\":25}\n",
            false,
            true
        },
        new object[]
        {
            "json_descriptors",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const obj: any = { first: 1 };\nObject.defineProperty(obj, \"hidden\", {\n    value: 2,\n    enumerable: false,\n    configurable: true\n});\nObject.defineProperty(obj, \"computed\", {\n    get: function (): number { return 3; },\n    enumerable: true,\n    configurable: true\n});\nObject.defineProperty(obj, \"setterOnly\", {\n    set: function (_value: any): void {},\n    enumerable: true,\n    configurable: true\n});\nobj.last = 4;\nconsole.log(JSON.stringify(obj));" },
            "{\"first\":1,\"computed\":3,\"last\":4}\n",
            false,
            true
        },
        new object[]
        {
            "json_record",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const record: { a: number; b: string; c: boolean; d: null } = {\n    a: 1, b: \"x\", c: true, d: null\n};\nconsole.log(record.a, record.b, record.c, record.d === null);\nrecord.a = 8;\nconst dynamic: any = record;\ndelete dynamic.b;\nObject.defineProperty(dynamic, \"e\", {\n    value: 5, enumerable: true, configurable: true\n});\nconsole.log(Object.keys(record).join(\",\"));\nconsole.log(JSON.stringify(record));" },
            "1 x true true\na,c,d,e\n{\"a\":8,\"c\":true,\"d\":null,\"e\":5}\n",
            false,
            true
        },
        new object[]
        {
            "nullish",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function copy(v:any){return {...v};} console.log(Object.keys(copy(null)).length,Object.keys(copy(undefined)).length);" },
            "0 0\n",
            false,
            true
        },
        new object[]
        {
            "rest_class",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Item {a=1;b=2;c=3;} const item:any=new Item(); const {a,...rest}=item; console.log(a,Object.keys(rest).join(','),rest.b+rest.c); rest.b=9; console.log(item.b);" },
            "1 b,c 5\n2\n",
            false,
            true
        },
        new object[]
        {
            "rest_primitive",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const n:any=3; const {...rest}=n; console.log(Object.keys(rest).length);" },
            "0\n",
            false,
            true
        },
        new object[]
        {
            "json_getter",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let calls=0; const o:any={a:1,get b(){calls++;return 2;}}; console.log(JSON.stringify(o)); console.log(calls);" },
            "{\"a\":1,\"b\":2}\n1\n",
            false,
            true
        },
        new object[]
        {
            "proxy_order",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let trace=''; const p:any=new Proxy({x:1,y:2},{ownKeys(){trace+='k';return ['y','x'];},getOwnPropertyDescriptor(t:any,k:any){trace+='d'+k;return {value:t[k],enumerable:true,configurable:true};},get(t:any,k:any){trace+='g'+k;return t[k];}}); function copy(v:any){return {...v};} const r:any=copy(p); console.log(Object.keys(r).join(','),r.y,r.x,trace);" },
            "y,x 2 1 kdygydxgx\n",
            false,
            false
        },
        new object[]
        {
            "esm",
            "main.ts",
            new string[] { "dep.ts", "main.ts" },
            new string[] { "export const value={a:1,b:2};", "import {value} from './dep'; const copy:any={...value}; const {a,...rest}=copy; console.log(a,Object.keys(rest).join(','),rest.b);" },
            "1 b 2\n",
            false,
            true
        },
        new object[]
        {
            "commonjs",
            "main.cjs",
            new string[] { "dep.cjs", "main.cjs" },
            new string[] { "exports.value={a:1,b:2};", "const dep=require('./dep.cjs'); const copy={...dep.value}; console.log(copy.a,copy.b);" },
            "1 2\n",
            false,
            true
        },
        new object[]
        {
            "hosted_construction",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export function copy(v:any){return {...v};} export function rest(v:any){const {a,...r}=v;return r;} export function text(v:any){return JSON.stringify(v);}" },
            "",
            true,
            true
        },
        new object[]
        {
            "hosted_minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export const value=1;" },
            "",
            true,
            true
        },
        new object[]
        {
            "symbol_getter",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const s=Symbol('s'); let value=3; const o:any={get [s](){return value;}}; console.log(o[s]); value=8; console.log(o[s]);" },
            "3\n8\n",
            false,
            true
        },
        new object[]
        {
            "symbol_setter",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const s=Symbol('s'); let value=3; const o:any={set [s](v:any){value=v;}}; o[s]=8; console.log(value);" },
            "8\n",
            false,
            true
        },
    ];

    [Theory]
    [MemberData(nameof(ObjectConstructionMetadataPrograms))]
    public void Isolated_ObjectConstructionMetadata_PreservesConstructionAndDeployment(
        string name, string entry, string[] paths, string[] sources, string expected, bool hosted, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        for (int i = 0; i < paths.Length; i++) tempDir.CreateFile(paths[i], sources[i]);
        var sourcePath = tempDir.GetPath(entry);
        var dllPath = tempDir.GetPath($"object-construction-metadata_{name}.dll");
        var deployment = standalone ? " --standalone" : "";
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{dllPath}\" --verify{deployment}{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }


    public static IEnumerable<object[]> ObjectOperationsMetadataPrograms =>
    [
        new object[]
        {
            "minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(1);" },
            "1\n",
            false,
            true
        },
        new object[]
        {
            "values",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: { a: number, b: number, c: number } = { a: 1, b: 2, c: 3 };\nlet values: any[] = Object.values(obj);\nconsole.log(values.length);\nconsole.log(values[0]);\nconsole.log(values[1]);\nconsole.log(values[2]);" },
            "3\n1\n2\n3\n",
            false,
            true
        },
        new object[]
        {
            "mixed",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: { name: string, age: number, active: boolean } = { name: \"Alice\", age: 30, active: true };\nlet values: any[] = Object.values(obj);\nconsole.log(values.length);" },
            "3\n",
            false,
            true
        },
        new object[]
        {
            "entries",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: { a: number, b: number } = { a: 1, b: 2 };\nlet entries: any[] = Object.entries(obj);\nconsole.log(entries.length);\nconsole.log(entries[0][0]);\nconsole.log(entries[0][1]);\nconsole.log(entries[1][0]);\nconsole.log(entries[1][1]);" },
            "2\na\n1\nb\n2\n",
            false,
            true
        },
        new object[]
        {
            "class_values",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Person {\n    name: string;\n    age: number;\n    constructor(n: string, a: number) {\n        this.name = n;\n        this.age = a;\n    }\n}\nlet p = new Person(\"Alice\", 30);\nlet values: any[] = Object.values(p);\nconsole.log(values.length);" },
            "2\n",
            false,
            true
        },
        new object[]
        {
            "class_entries",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Person {\n    name: string;\n    age: number;\n    constructor(n: string, a: number) {\n        this.name = n;\n        this.age = a;\n    }\n}\nlet p = new Person(\"Alice\", 30);\nlet entries: any[] = Object.entries(p);\nconsole.log(entries.length);" },
            "2\n",
            false,
            true
        },
        new object[]
        {
            "from_entries",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let entries: any[] = [[\"a\", 1], [\"b\", 2], [\"c\", 3]];\nlet obj = Object.fromEntries(entries);\nconsole.log(obj.a);\nconsole.log(obj.b);\nconsole.log(obj.c);" },
            "1\n2\n3\n",
            false,
            true
        },
        new object[]
        {
            "duplicate_keys",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let entries: any[] = [[\"a\", 1], [\"a\", 2], [\"a\", 3]];\nlet obj = Object.fromEntries(entries);\nconsole.log(obj.a);" },
            "3\n",
            false,
            true
        },
        new object[]
        {
            "round_trip",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let original: { x: number, y: number, z: number } = { x: 1, y: 2, z: 3 };\nlet entries: any[] = Object.entries(original);\nlet restored = Object.fromEntries(entries);\nconsole.log(restored.x);\nconsole.log(restored.y);\nconsole.log(restored.z);" },
            "1\n2\n3\n",
            false,
            true
        },
        new object[]
        {
            "map_entries",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let map = new Map<string, number>();\nmap.set(\"x\", 10);\nmap.set(\"y\", 20);\nlet obj = Object.fromEntries(map.entries());\nconsole.log(obj.x);\nconsole.log(obj.y);" },
            "10\n20\n",
            false,
            true
        },
        new object[]
        {
            "assign",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let target: { a: number, b?: number } = { a: 1 };\nlet source: { b: number } = { b: 2 };\nlet result = Object.assign(target, source);\nconsole.log(result.a);\nconsole.log(result.b);" },
            "1\n2\n",
            false,
            true
        },
        new object[]
        {
            "assign_identity",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let target: { a: number } = { a: 1 };\nlet result = Object.assign(target, { b: 2 });\nconsole.log(result === target);" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "assign_nested",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let target: any = { a: 1 };\nObject.assign(target, { nested: { x: 10 } });\nconsole.log(target.a);\nconsole.log(target.nested.x);" },
            "1\n10\n",
            false,
            true
        },
        new object[]
        {
            "nan",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(Object.is(NaN, NaN));\nconsole.log(Object.is(NaN, 0));\nconsole.log(Object.is(NaN, \"NaN\"));" },
            "true\nfalse\nfalse\n",
            false,
            true
        },
        new object[]
        {
            "signed_zero",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(Object.is(0, -0));\nconsole.log(Object.is(-0, 0));\nconsole.log(0 === -0);" },
            "false\nfalse\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "reference",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj1: { x: number } = { x: 1 };\nlet obj2: { x: number } = { x: 1 };\nlet obj3 = obj1;\nconsole.log(Object.is(obj1, obj1));\nconsole.log(Object.is(obj1, obj2));\nconsole.log(Object.is(obj1, obj3));" },
            "true\nfalse\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "infinity",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(Object.is(Infinity, Infinity));\nconsole.log(Object.is(-Infinity, -Infinity));\nconsole.log(Object.is(Infinity, -Infinity));" },
            "true\ntrue\nfalse\n",
            false,
            true
        },
        new object[]
        {
            "group",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const inventory = [\n    { name: \"asparagus\", type: \"vegetables\" },\n    { name: \"bananas\", type: \"fruit\" },\n    { name: \"goat\", type: \"meat\" },\n    { name: \"cherries\", type: \"fruit\" },\n    { name: \"fish\", type: \"meat\" }\n];\nconst result: any = Object.groupBy(inventory, (item: any) => item.type);\nconsole.log(Object.keys(result).length);\nconsole.log(result.vegetables.length);\nconsole.log(result.fruit.length);\nconsole.log(result.meat.length);\nconsole.log(result.fruit[0].name);\nconsole.log(result.fruit[1].name);" },
            "3\n1\n2\n2\nbananas\ncherries\n",
            false,
            true
        },
        new object[]
        {
            "group_empty",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const result: any = Object.groupBy([], (_: any) => \"key\");\nconsole.log(Object.keys(result).length);" },
            "0\n",
            false,
            true
        },
        new object[]
        {
            "group_numeric",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const nums = [1, 2, 3, 4, 5, 6];\nconst result: any = Object.groupBy(nums, (n: any) => n % 2 === 0 ? \"even\" : \"odd\");\nconsole.log(result.odd.length);\nconsole.log(result.even.length);" },
            "3\n3\n",
            false,
            true
        },
        new object[]
        {
            "group_index",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const arr = [\"a\", \"b\", \"c\", \"d\"];\nconst result: any = Object.groupBy(arr, (_: any, i: number) => i < 2 ? \"first\" : \"second\");\nconsole.log(result.first.length);\nconsole.log(result.second.length);\nconsole.log(result.first[0]);\nconsole.log(result.second[0]);" },
            "2\n2\na\nc\n",
            false,
            true
        },
        new object[]
        {
            "descriptors",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const o: any = {a:1}; Object.defineProperty(o,'hidden',{value:2}); Object.defineProperty(o,'last',{get:()=>3,enumerable:true}); console.log(Object.values(o).join(',')); console.log(Object.entries(o).map((v:any)=>v.join(':')).join(','));" },
            "1,3\na:1,last:3\n",
            false,
            true
        },
        new object[]
        {
            "symbol_assign",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const s=Symbol('s'); const source: any = {a:1}; source[s]=7; const target: any = Object.assign({},source); console.log(target[s],Object.values(target).join(','),Object.getOwnPropertySymbols(target).length);" },
            "7 1 1\n",
            false,
            true
        },
        new object[]
        {
            "getter_order",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let trace=''; const source: any = {}; Object.defineProperty(source,'a',{get(){trace+='a';return 1;},enumerable:true}); Object.defineProperty(source,'b',{get(){trace+='b';return 2;},enumerable:true}); const target: any = {}; Object.defineProperty(target,'a',{set(v:any){trace+='s'+v;},configurable:true}); Object.assign(target,source); console.log(trace,target.b);" },
            "as1b 2\n",
            false,
            true
        },
        new object[]
        {
            "array_holes",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a: any = [1,2,3]; delete a[1]; console.log(Object.values(a).join(',')); console.log(Object.entries(a).map((v:any)=>v.join(':')).join(','));" },
            "1,3\n0:1,2:3\n",
            false,
            true
        },
        new object[]
        {
            "nullish",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "for (const v of [null,undefined]) { try { Object.fromEntries(v); } catch(e:any) { console.log(e instanceof TypeError); } try { Object.assign(v,{a:1}); } catch(e:any) { console.log(e instanceof TypeError); } }" },
            "true\ntrue\ntrue\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "promise_values",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "new Promise((resolve:any,reject:any)=>{ console.log(Object.values(resolve).length,Object.entries(reject).length); resolve(1); });" },
            "0 0\n",
            false,
            true
        },
        new object[]
        {
            "proxy_values",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let trace=''; const p:any=new Proxy({x:1,y:2},{ownKeys(){trace+='k';return ['y','x'];},getOwnPropertyDescriptor(t:any,k:any){trace+='d'+k;return {value:t[k],enumerable:true,configurable:true};},get(t:any,k:any){trace+='g'+k;return t[k];}}); console.log(Object.values(p).join(',')); console.log(trace);" },
            "2,1\nkdygydxgx\n",
            false,
            false
        },
        new object[]
        {
            "proxy_assign",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let trace=''; const p:any=new Proxy({x:1},{ownKeys(){trace+='k';return ['x'];},getOwnPropertyDescriptor(t:any,k:any){trace+='d';return {value:1,enumerable:true,configurable:true};},get(t:any,k:any){trace+='g';return t[k];}}); const target:any=Object.assign({},p); console.log(target.x,trace);" },
            "1 kdg\n",
            false,
            false
        },
        new object[]
        {
            "esm",
            "main.ts",
            new string[] { "dep.ts", "main.ts" },
            new string[] { "export const value = {b:2,a:1};", "import {value} from './dep'; console.log(Object.values(value).join(','));" },
            "2,1\n",
            false,
            true
        },
        new object[]
        {
            "commonjs",
            "main.cjs",
            new string[] { "dep.cjs", "main.cjs" },
            new string[] { "exports.value={a:1};", "const dep=require('./dep.cjs'); console.log(Object.entries(dep.value).map(v=>v.join(':')).join(','));" },
            "a:1\n",
            false,
            true
        },
        new object[]
        {
            "hosted_operations",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export function values(value:any){return Object.values(value);} export function assign(target:any,source:any){return Object.assign(target,source);}" },
            "",
            true,
            true
        },
        new object[]
        {
            "hosted_minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export const value=1;" },
            "",
            true,
            true
        },
    ];

    [Theory]
    [MemberData(nameof(ObjectOperationsMetadataPrograms))]
    public void Isolated_ObjectOperationsMetadata_PreservesOperationsAndDeployment(
        string name, string entry, string[] paths, string[] sources, string expected, bool hosted, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        for (int i = 0; i < paths.Length; i++) tempDir.CreateFile(paths[i], sources[i]);
        var sourcePath = tempDir.GetPath(entry);
        var dllPath = tempDir.GetPath($"object-operations-metadata_{name}.dll");
        var deployment = standalone ? " --standalone" : "";
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{dllPath}\" --verify{deployment}{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }


    public static IEnumerable<object[]> ObjectOwnPropertiesMetadataPrograms =>
    [
        new object[]
        {
            "minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(1);" },
            "1\n",
            false,
            true
        },
        new object[]
        {
            "own",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: { a: number, b: number } = { a: 1, b: 2 };\nconsole.log(Object.hasOwn(obj, \"a\"));\nconsole.log(Object.hasOwn(obj, \"b\"));" },
            "true\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "missing",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: { a: number } = { a: 1 };\nconsole.log(Object.hasOwn(obj, \"b\"));\nconsole.log(Object.hasOwn(obj, \"c\"));" },
            "false\nfalse\n",
            false,
            true
        },
        new object[]
        {
            "empty",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: {} = {};\nconsole.log(Object.hasOwn(obj, \"a\"));" },
            "false\n",
            false,
            true
        },
        new object[]
        {
            "class_field",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Person {\n    name: string;\n    age: number;\n    constructor(n: string, a: number) {\n        this.name = n;\n        this.age = a;\n    }\n    greet(): string {\n        return \"Hello\";\n    }\n}\nlet p = new Person(\"Alice\", 30);\nconsole.log(Object.hasOwn(p, \"name\"));\nconsole.log(Object.hasOwn(p, \"age\"));" },
            "true\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "class_method",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Person {\n    name: string;\n    constructor(n: string) {\n        this.name = n;\n    }\n    greet(): string {\n        return \"Hello\";\n    }\n}\nlet p = new Person(\"Alice\");\nconsole.log(Object.hasOwn(p, \"greet\"));" },
            "false\n",
            false,
            true
        },
        new object[]
        {
            "number_key",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = { \"123\": \"value\" };\nconsole.log(Object.hasOwn(obj, \"123\"));" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "legacy_identity",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const getter = function (): number { return 1; };\nconst setter = function (value: number): void {};\nconst descriptors: any = {\n    get: getter,\n    set: setter,\n    configurable: true\n};\nconst prototype: any = {};\nObject.defineProperty(prototype, 'value', descriptors);\nconst subject: any = Object.create(prototype);\nconsole.log(subject.__lookupGetter__('value') === descriptors.get);\nconsole.log(subject.__lookupSetter__('value') === descriptors.set);" },
            "true\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "getter",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj = {\n    _value: 42,\n    get value(): number {\n        return this._value;\n    }\n};\nconsole.log(obj.value);" },
            "42\n",
            false,
            true
        },
        new object[]
        {
            "setter",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj = {\n    _value: 0,\n    get value(): number {\n        return this._value;\n    },\n    set value(v: number) {\n        this._value = v;\n    }\n};\nobj.value = 100;\nconsole.log(obj.value);\nconsole.log(obj._value);" },
            "100\n100\n",
            false,
            true
        },
        new object[]
        {
            "getter_this",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj = {\n    name: \"test\",\n    get greeting(): string {\n        return \"Hello, \" + this.name;\n    }\n};\nconsole.log(obj.greeting);" },
            "Hello, test\n",
            false,
            true
        },
        new object[]
        {
            "setter_this",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj = {\n    _firstName: \"\",\n    _lastName: \"\",\n    set fullName(name: string) {\n        let parts = name.split(\" \");\n        this._firstName = parts[0];\n        this._lastName = parts[1];\n    }\n};\nobj.fullName = \"John Doe\";\nconsole.log(obj._firstName);\nconsole.log(obj._lastName);" },
            "John\nDoe\n",
            false,
            true
        },
        new object[]
        {
            "mixed_accessors",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj = {\n    regularProp: \"regular\",\n    _hidden: 0,\n    get accessorProp(): number {\n        return this._hidden * 2;\n    },\n    set accessorProp(v: number) {\n        this._hidden = v;\n    }\n};\nconsole.log(obj.regularProp);\nobj.accessorProp = 5;\nconsole.log(obj.accessorProp);\nconsole.log(obj._hidden);" },
            "regular\n10\n5\n",
            false,
            true
        },
        new object[]
        {
            "builtin_mutation",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const prototypes: any[] = [Object.prototype, Array.prototype, String.prototype,\n    Number.prototype, Boolean.prototype, BigInt.prototype, Symbol.prototype,\n    Function.prototype, Error.prototype, Promise.prototype];\nconst names = [\"valueOf\", \"map\", \"trim\", \"toFixed\", \"valueOf\", \"valueOf\",\n    \"valueOf\", \"bind\", \"toString\", \"then\"];\nfor (let i = 0; i < prototypes.length; i++) {\n    const p: any = prototypes[i];\n    const name = names[i];\n    console.log(delete p[name]);\n    console.log(Object.prototype.hasOwnProperty.call(p, name));\n    Object.defineProperty(p, name, {\n        value: 17, writable: true, enumerable: false, configurable: true\n    });\n    console.log(p[name]);\n    console.log(delete p[name]);\n    console.log(Object.prototype.hasOwnProperty.call(p, name));\n    p[name] = 23;\n    console.log(p[name]);\n    Object.defineProperty(p, name, { value: 23, writable: false, configurable: false });\n    console.log(delete p[name]);\n    let rejected = false;\n    try { Object.defineProperty(p, name, { value: 99 }); }\n    catch (e) { rejected = true; }\n    console.log(rejected);\n    console.log(p[name]);\n}" },
            "true\nfalse\n17\ntrue\nfalse\n23\nfalse\ntrue\n23\ntrue\nfalse\n17\ntrue\nfalse\n23\nfalse\ntrue\n23\ntrue\nfalse\n17\ntrue\nfalse\n23\nfalse\ntrue\n23\ntrue\nfalse\n17\ntrue\nfalse\n23\nfalse\ntrue\n23\ntrue\nfalse\n17\ntrue\nfalse\n23\nfalse\ntrue\n23\ntrue\nfalse\n17\ntrue\nfalse\n23\nfalse\ntrue\n23\ntrue\nfalse\n17\ntrue\nfalse\n23\nfalse\ntrue\n23\ntrue\nfalse\n17\ntrue\nfalse\n23\nfalse\ntrue\n23\ntrue\nfalse\n17\ntrue\nfalse\n23\nfalse\ntrue\n23\ntrue\nfalse\n17\ntrue\nfalse\n23\nfalse\ntrue\n23\n",
            false,
            true
        },
        new object[]
        {
            "extra_descriptors",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function overlayKeys(p: any): string {\n    const keys: string[] = [];\n    for (const key in p) {\n        if (key === \"2\" || key === \"9\" || key.startsWith(\"overlay\")) keys.push(key);\n    }\n    return keys.join(\",\");\n}\nconst prototypes: any[] = [Array.prototype, String.prototype, Number.prototype,\n    Boolean.prototype, BigInt.prototype, Symbol.prototype, Function.prototype,\n    Error.prototype, Promise.prototype, Object.prototype];\nfor (const p of prototypes) {\n    p.overlayFirst = null;\n    p[9] = 9;\n    p[2] = 2;\n    Object.defineProperty(p, \"overlayHidden\", { value: 1, configurable: true });\n    Object.defineProperty(p, \"overlayEmpty\", {\n        get: undefined, set: undefined, enumerable: true, configurable: true\n    });\n    console.log(Object.getOwnPropertyDescriptor(p, \"overlayFirst\").value === null);\n    console.log(p.overlayEmpty === undefined);\n    console.log(Object.getOwnPropertyDescriptor(p, \"overlayEmpty\") !== undefined);\n    const d = Object.getOwnPropertyDescriptor(p, \"overlayHidden\");\n    console.log(d.writable, d.enumerable, d.configurable);\n    console.log(overlayKeys(p));\n    delete p.overlayFirst;\n    p.overlayFirst = undefined;\n    console.log(overlayKeys(p));\n    delete p.overlayFirst;\n    delete p[9];\n    delete p[2];\n    delete p.overlayHidden;\n    delete p.overlayEmpty;\n}" },
            "true\ntrue\ntrue\nfalse false true\n2,9,overlayFirst,overlayEmpty\n2,9,overlayEmpty,overlayFirst\ntrue\ntrue\ntrue\nfalse false true\n2,9,overlayFirst,overlayEmpty\n2,9,overlayEmpty,overlayFirst\ntrue\ntrue\ntrue\nfalse false true\n2,9,overlayFirst,overlayEmpty\n2,9,overlayEmpty,overlayFirst\ntrue\ntrue\ntrue\nfalse false true\n2,9,overlayFirst,overlayEmpty\n2,9,overlayEmpty,overlayFirst\ntrue\ntrue\ntrue\nfalse false true\n2,9,overlayFirst,overlayEmpty\n2,9,overlayEmpty,overlayFirst\ntrue\ntrue\ntrue\nfalse false true\n2,9,overlayFirst,overlayEmpty\n2,9,overlayEmpty,overlayFirst\ntrue\ntrue\ntrue\nfalse false true\n2,9,overlayFirst,overlayEmpty\n2,9,overlayEmpty,overlayFirst\ntrue\ntrue\ntrue\nfalse false true\n2,9,overlayFirst,overlayEmpty\n2,9,overlayEmpty,overlayFirst\ntrue\ntrue\ntrue\nfalse false true\n2,9,overlayFirst,overlayEmpty\n2,9,overlayEmpty,overlayFirst\ntrue\ntrue\ntrue\nfalse false true\n2,9,overlayFirst,overlayEmpty\n2,9,overlayEmpty,overlayFirst\n",
            false,
            true
        },
        new object[]
        {
            "accessor_identity",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const prototypes: any[] = [Array.prototype, String.prototype, Number.prototype,\n    Boolean.prototype, BigInt.prototype, Symbol.prototype, Function.prototype,\n    Error.prototype, Object.prototype];\nfor (const p of prototypes) {\n    let reads = 0;\n    let writes = 0;\n    const getter = function(this: any): any { reads++; return this; };\n    const setter = function(this: any, value: any): void { writes++; };\n    Object.defineProperty(p, \"overlayAccessor\", {\n        get: getter, set: setter, enumerable: true, configurable: true\n    });\n    const d = Object.getOwnPropertyDescriptor(p, \"overlayAccessor\");\n    console.log(d.get === getter, d.set === setter, reads, writes);\n    delete p.overlayAccessor;\n}" },
            "true true 0 0\ntrue true 0 0\ntrue true 0 0\ntrue true 0 0\ntrue true 0 0\ntrue true 0 0\ntrue true 0 0\ntrue true 0 0\ntrue true 0 0\n",
            false,
            true
        },
        new object[]
        {
            "enumerability",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const o: any = {data:1}; Object.defineProperty(o, 'hidden', {value:2});\nconsole.log(o.hasOwnProperty('data'), o.propertyIsEnumerable('data'));\nconsole.log(o.hasOwnProperty('hidden'), o.propertyIsEnumerable('hidden'));\nconst child: any = Object.create(o); console.log(child.hasOwnProperty('data'), child.propertyIsEnumerable('data'));" },
            "true true\ntrue false\nfalse false\n",
            false,
            true
        },
        new object[]
        {
            "symbols",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const s = Symbol('s'); const o: any = {}; Object.defineProperty(o,s,{value:1,enumerable:true});\nconsole.log(Object.hasOwn(o,s),o.hasOwnProperty(s),o.propertyIsEnumerable(s)); console.log(Object.hasOwn(o,Symbol('s')));" },
            "true true true\nfalse\n",
            false,
            true
        },
        new object[]
        {
            "legacy_define",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const o: any = {}; let state = 2;\nconst getter = function() { return state; }; const setter = function(v: number) { state = v; };\nconsole.log(o.__defineGetter__('value',getter) === undefined); console.log(o.__defineSetter__('value',setter) === undefined);\nconsole.log(o.__lookupGetter__('value') === getter, o.__lookupSetter__('value') === setter);\no.value = 7; console.log(o.value,o.propertyIsEnumerable('value'));" },
            "true\ntrue\ntrue true\n7 true\n",
            false,
            true
        },
        new object[]
        {
            "shadow",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const p: any = {}; const getter = function() {return 3;}; p.__defineGetter__('value',getter);\nconst o: any = Object.create(p); console.log(o.__lookupGetter__('value') === getter, Object.hasOwn(o,'value'));\nObject.defineProperty(o,'value',{value:7}); console.log(o.__lookupGetter__('value') === undefined,o.__lookupSetter__('value') === undefined);" },
            "true false\ntrue true\n",
            false,
            true
        },
        new object[]
        {
            "nullish",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "for (const value of [null,undefined]) {\ntry { Object.hasOwn(value,'x'); } catch (e: any) { console.log(e instanceof TypeError); }\ntry { Object.prototype.__lookupGetter__.call(value,'x'); } catch (e: any) { console.log(e instanceof TypeError); }\n}" },
            "true\ntrue\ntrue\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "noncallable",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const o: any = {}; for (const method of ['__defineGetter__','__defineSetter__']) {\ntry { o[method]('x',7); } catch (e: any) { console.log(e instanceof TypeError); }\n} console.log(Object.hasOwn(o,'x'));" },
            "true\ntrue\nfalse\n",
            false,
            true
        },
        new object[]
        {
            "proxy_descriptor",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let trace = ''; const p: any = new Proxy({x:1}, {getOwnPropertyDescriptor(t: any,k: any) { trace += String(k); return {value:1,enumerable:true,configurable:true}; }});\nconsole.log(Object.hasOwn(p,'x')); console.log(Object.prototype.propertyIsEnumerable.call(p,'x')); console.log(trace);" },
            "true\ntrue\nxx\n",
            false,
            false
        },
        new object[]
        {
            "proxy_missing",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const p: any = new Proxy({}, {getOwnPropertyDescriptor() { return undefined; }});\nconsole.log(Object.hasOwn(p,'x'),Object.prototype.hasOwnProperty.call(p,'x'),Object.prototype.propertyIsEnumerable.call(p,'x'));" },
            "false false false\n",
            false,
            false
        },
        new object[]
        {
            "proxy_revoked",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const pair: any = Proxy.revocable({},{}); pair.revoke();\ntry { Object.hasOwn(pair.proxy,'x'); } catch (e: any) { console.log(e instanceof TypeError); }" },
            "true\n",
            false,
            false
        },
        new object[]
        {
            "esm",
            "main.ts",
            new string[] { "dep.ts", "main.ts" },
            new string[] { "const value: any = {}; value.__defineGetter__('x',function(){return 3;}); export { value };", "import {value} from './dep'; console.log(Object.hasOwn(value,'x'),Object.hasOwn(value,'missing')); console.log(value.x);" },
            "true false\n3\n",
            false,
            true
        },
        new object[]
        {
            "commonjs",
            "main.cjs",
            new string[] { "dep.cjs", "main.cjs" },
            new string[] { "exports.value = {x:1};", "const dep = require('./dep.cjs'); console.log(Object.hasOwn(dep.value,'x'));" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "hosted_own",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export function own(value: any,key: any) { return Object.hasOwn(value,key); } export function getter(value: any,key: any) { return value.__lookupGetter__(key); }" },
            "",
            true,
            true
        },
        new object[]
        {
            "hosted_minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export const value = 1;" },
            "",
            true,
            true
        },
        new object[]
        {
            "array_string_own",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a: any = [1,2]; delete a[1];\nconsole.log(a.hasOwnProperty('length'),a.hasOwnProperty('0'),a.hasOwnProperty('1'));\nconsole.log(Object.prototype.hasOwnProperty.call('ab','0'));" },
            "true true false\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "promise_callback_own",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "new Promise((resolve: any,reject: any) => {\nfor (const fn of [resolve,reject]) { console.log(Object.hasOwn(fn,'name'),Object.hasOwn(fn,'length'),Object.hasOwn(fn,'prototype'));\n} resolve(1);\n});" },
            "true true false\ntrue true false\n",
            false,
            true
        },
    ];

    [Theory]
    [MemberData(nameof(ObjectOwnPropertiesMetadataPrograms))]
    public void Isolated_ObjectOwnPropertiesMetadata_PreservesPredicatesAccessorsAndDeployment(
        string name, string entry, string[] paths, string[] sources, string expected, bool hosted, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        for (int i = 0; i < paths.Length; i++) tempDir.CreateFile(paths[i], sources[i]);
        var sourcePath = tempDir.GetPath(entry);
        var dllPath = tempDir.GetPath($"object-own-properties-metadata_{name}.dll");
        var deployment = standalone ? " --standalone" : "";
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{dllPath}\" --verify{deployment}{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }


    public static IEnumerable<object[]> OwnKeysMetadataPrograms =>
    [
        new object[]
        {
            "minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(1);" },
            "1\n",
            false,
            true
        },
        new object[]
        {
            "keys",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: { a: number, b: number, c: number } = { a: 1, b: 2, c: 3 };\nlet keys: string[] = Object.keys(obj);\nconsole.log(keys.length);" },
            "3\n",
            false,
            true
        },
        new object[]
        {
            "class_keys",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Person {\n    name: string;\n    age: number;\n    constructor(n: string, a: number) {\n        this.name = n;\n        this.age = a;\n    }\n}\nlet p = new Person(\"Alice\", 30);\nlet keys: string[] = Object.keys(p);\nconsole.log(keys.length);" },
            "2\n",
            false,
            true
        },
        new object[]
        {
            "names",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: { a: number, b: number, c: number } = { a: 1, b: 2, c: 3 };\nlet names: string[] = Object.getOwnPropertyNames(obj);\nconsole.log(names.length);" },
            "3\n",
            false,
            true
        },
        new object[]
        {
            "array_names",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let arr: number[] = [1, 2, 3];\nlet names: string[] = Object.getOwnPropertyNames(arr);\n// Should include \"0\", \"1\", \"2\", \"length\"\nconsole.log(names.includes(\"0\"));\nconsole.log(names.includes(\"1\"));\nconsole.log(names.includes(\"2\"));\nconsole.log(names.includes(\"length\"));" },
            "true\ntrue\ntrue\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "hidden",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = {};\nObject.defineProperty(obj, \"hidden\", { value: 42, enumerable: false });\nObject.defineProperty(obj, \"visible\", { value: 100, enumerable: true });\nlet names: string[] = Object.getOwnPropertyNames(obj);\nconsole.log(names.includes(\"hidden\"));\nconsole.log(names.includes(\"visible\"));" },
            "true\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "accessors",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const objectValue: any = {};\nObject.defineProperty(objectValue, \"accessor\", { get() { return 1; } });\nconst arrayValue: any[] = [1, 2];\narrayValue.data = 3;\nObject.defineProperty(arrayValue, \"accessor\", { get() { return 4; } });\nconsole.log(Object.getOwnPropertyNames(objectValue).includes(\"accessor\"));\nconsole.log(Object.getOwnPropertyNames(arrayValue).includes(\"data\"));\nconsole.log(Object.getOwnPropertyNames(arrayValue).includes(\"accessor\"));" },
            "true\ntrue\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "defined",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = { visible: 1 };\nObject.defineProperty(obj, \"hidden\", { value: 2, enumerable: false });\nlet names: string[] = Object.getOwnPropertyNames(obj);\nconsole.log(names.length);\nconsole.log(names.includes(\"visible\"));\nconsole.log(names.includes(\"hidden\"));" },
            "2\ntrue\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "class_names",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Person {\n    name: string;\n    constructor(n: string) {\n        this.name = n;\n    }\n    greet(): string {\n        return \"Hello\";\n    }\n}\nlet p = new Person(\"Alice\");\nlet names: string[] = Object.getOwnPropertyNames(p);\nconsole.log(names.includes(\"name\"));\nconsole.log(names.includes(\"greet\"));" },
            "true\nfalse\n",
            false,
            true
        },
        new object[]
        {
            "empty",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: {} = {};\nlet names: string[] = Object.getOwnPropertyNames(obj);\nconsole.log(names.length);" },
            "0\n",
            false,
            true
        },
        new object[]
        {
            "mixed",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: { name: string, age: number, active: boolean } = { name: \"Alice\", age: 30, active: true };\nlet names: string[] = Object.getOwnPropertyNames(obj);\nconsole.log(names.length);" },
            "3\n",
            false,
            true
        },
        new object[]
        {
            "symbol_use",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let sym = Symbol(\"key\");\nlet obj: any = { [sym]: 42 };\nlet symbols = Object.getOwnPropertySymbols(obj);\nconsole.log(obj[symbols[0]]);" },
            "42\n",
            false,
            true
        },
        new object[]
        {
            "class_symbols",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let sym = Symbol(\"prop\");\nclass MyClass {\n    x: number = 1;\n}\nlet obj: any = new MyClass();\nobj[sym] = \"symbol value\";\nlet symbols = Object.getOwnPropertySymbols(obj);\nconsole.log(symbols.length);" },
            "1\n",
            false,
            true
        },
        new object[]
        {
            "created",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let proto = { a: 1, b: 2 };\nlet obj = Object.create(proto);\n// ECMA-262 §20.1.2.16 Object.keys: own enumerable keys only.\n// Object.create(proto) returns a FRESH empty object with [[Prototype]]\n// = proto. proto's keys are reached via the prototype chain at\n// property-access time — they are NOT own keys of the created obj.\nlet keys = Object.keys(obj);\nconsole.log(keys.length);\n// Inherited access still works through the prototype chain.\nconsole.log(obj.a);\nconsole.log(obj.b);" },
            "0\n1\n2\n",
            false,
            true
        },
        new object[]
        {
            "proxy_keys",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let target: any = { a: 1, b: 2 };\nlet proxy: any = new Proxy(target, {\n    ownKeys: function(t: any): string[] { return [\"x\", \"y\"]; },\n    getOwnPropertyDescriptor: function(t: any, key: string): any {\n        return { configurable: true, enumerable: true, value: key };\n    }\n});\nconsole.log(Object.keys(proxy).join(\",\"));" },
            "x,y\n",
            false,
            false
        },
        new object[]
        {
            "proxy_names",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let target: any = { a: 1 };\nlet proxy: any = new Proxy(target, {\n    ownKeys: function(t: any): string[] { return [\"p\", \"q\", \"r\"]; }\n});\nconsole.log(Object.getOwnPropertyNames(proxy).join(\",\"));" },
            "p,q,r\n",
            false,
            false
        },
        new object[]
        {
            "proxy_revoked",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let target: any = { a: 1 };\nlet r: any = Proxy.revocable(target, {});\nr.revoke();\ntry {\n    Object.keys(r.proxy);\n    console.log(\"should not reach\");\n} catch (e) {\n    console.log(\"threw\");\n}" },
            "threw\n",
            false,
            false
        },
        new object[]
        {
            "shape_mutation",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const sym: symbol = Symbol(\"hidden\");\nconst record: any = {\n    10: \"ten\",\n    first: \"a\",\n    2: \"two\",\n    1: \"one\",\n    [sym]: \"symbol\"\n};\nrecord.last = \"z\";\nconsole.log(Object.keys(record).join(\",\"));" },
            "1,2,10,first,last\n",
            false,
            true
        },
        new object[]
        {
            "proxy_mutation",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let getterCalls: number = 0;\nconst accessor: any = {\n    a: 1,\n    get b(): number { getterCalls = getterCalls + 1; return 2; }\n};\nconsole.log(Object.keys(accessor).join(\",\"));\nconsole.log(getterCalls);\n\nlet ownKeysCalls: number = 0;\nconst proxy: any = new Proxy({ x: 1, y: 2 }, {\n    ownKeys(target: any): any[] {\n        ownKeysCalls = ownKeysCalls + 1;\n        return Reflect.ownKeys(target);\n    }\n});\nconsole.log(Object.keys(proxy).join(\",\"));\nconsole.log(ownKeysCalls);" },
            "a,b\n0\nx,y\n1\n",
            false,
            false
        },
        new object[]
        {
            "spread_order",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function copy(value: any): any { return { prefix: 0, ...value, suffix: 1 }; }\nconst value: any = { a: 1, \"10\": 10, \"2\": 2, \"01\": 1, \"4294967295\": 5 };\nconst result: any = copy(value);\nconsole.log(Object.keys(result).join(\",\"));\nconsole.log(result[\"10\"] + result[\"2\"] + result[\"01\"]);" },
            "2,10,prefix,a,01,4294967295,suffix\n13\n",
            false,
            true
        },
        new object[]
        {
            "index_names",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const targets: any[] = [{}, []];\nconst keys: string[] = [\"01\", \"+1\", \" 1 \", \"4294967295\"];\nfor (const target of targets) {\n    for (const key of keys) {\n        Object.defineProperty(target, key, {\n            value: key, enumerable: true, configurable: true\n        });\n    }\n    target[2] = \"2\";\n    target[0] = \"0\";\n    console.log(Object.keys(target).join(\",\"));\n    console.log(target[\"01\"], target[\"+1\"], target[\" 1 \"], target[\"4294967295\"]);\n}\nconsole.log(targets[1].length);" },
            "0,2,01,+1, 1 ,4294967295\n01 +1  1  4294967295\n0,2,01,+1, 1 ,4294967295\n01 +1  1  4294967295\n3\n",
            false,
            true
        },
        new object[]
        {
            "function_keys",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function f() {}\nconsole.log(Object.keys(f).length);\n(f as any).alpha = 1;\n(f as any).beta = 2;\nconsole.log(Object.keys(f).join(\",\"));\nconsole.log(Object.values(f).join(\",\"));" },
            "0\nalpha,beta\n1,2\n",
            false,
            true
        },
        new object[]
        {
            "reflect_keys",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = { a: 1, b: 2, c: 3 };\nlet keys: any = Reflect.ownKeys(obj);\nconsole.log(keys.length);\nconsole.log(keys[0]);\nconsole.log(keys[1]);\nconsole.log(keys[2]);" },
            "3\na\nb\nc\n",
            false,
            true
        },
        new object[]
        {
            "nullish",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "for (const method of [Object.keys, Object.getOwnPropertyNames, Object.getOwnPropertySymbols]) {\n for (const value of [null, undefined]) { try { method(value); } catch (e: any) { console.log(e instanceof TypeError); } }\n}" },
            "true\ntrue\ntrue\ntrue\ntrue\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "promise_callbacks",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "new Promise((resolve: any, reject: any) => {\n console.log(Object.getOwnPropertyNames(resolve).join(',')); console.log(Object.getOwnPropertyNames(reject).join(','));\n console.log(Object.keys(resolve).length, Object.getOwnPropertySymbols(reject).length); resolve(1);\n});" },
            "length,name\nlength,name\n0 0\n",
            false,
            true
        },
        new object[]
        {
            "sparse_array",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a: any = [1,2,3]; delete a[1]; a.extra = 4; Object.defineProperty(a, 'hidden', {value: 5});\nconsole.log(Object.keys(a).join(',')); console.log(Object.getOwnPropertyNames(a).join(','));" },
            "0,2,extra\n0,2,length,extra,hidden\n",
            false,
            true
        },
        new object[]
        {
            "index_boundaries",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const o: any = {}; o.b=1; o['4294967295']=2; o['2']=3; o['01']=4; o['4294967294']=5; o['0']=6;\nconsole.log(Object.keys(o).join(',')); console.log(Object.getOwnPropertyNames(o).join(','));" },
            "0,2,4294967294,b,4294967295,01\n0,2,4294967294,b,4294967295,01\n",
            false,
            true
        },
        new object[]
        {
            "proxy_mixed",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const s = Symbol('s'); const t: any = {a:1,b:2}; t[s]=3;\nconst p: any = new Proxy(t, {ownKeys() { return [s, 'b', 'a']; }});\nconsole.log(Object.keys(p).join(',')); console.log(Object.getOwnPropertyNames(p).join(',')); console.log(Object.getOwnPropertySymbols(p)[0]===s);" },
            "b,a\nb,a\ntrue\n",
            false,
            false
        },
        new object[]
        {
            "proxy_duplicate",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const p: any = new Proxy({a:1}, {ownKeys() { return ['a','a']; }});\nfor (const method of [Object.keys,Object.getOwnPropertyNames,Object.getOwnPropertySymbols]) { try { method(p); } catch (e: any) { console.log(e instanceof TypeError); } }" },
            "true\ntrue\ntrue\n",
            false,
            false
        },
        new object[]
        {
            "proxy_arraylike_order",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let trace = '';\nconst list: any = {get length() { trace += 'l'; return 2.9; }, get 0() { trace += '0'; return 'b'; }, get 1() { trace += '1'; return 'a'; }};\nconst p: any = new Proxy({a:1,b:2}, {ownKeys() { trace += 'k'; return list; }});\nconsole.log(Object.getOwnPropertyNames(p).join(',')); console.log(trace);" },
            "b,a\nkl01\n",
            false,
            false
        },
        new object[]
        {
            "esm",
            "main.ts",
            new string[] { "dep.ts", "main.ts" },
            new string[] { "export const s = Symbol('module'); export const value: any = {z:1, '2':2, '1':3}; value[s]=4;", "import {s,value} from './dep'; console.log(Object.keys(value).join(',')); console.log(Object.getOwnPropertySymbols(value)[0]===s);" },
            "1,2,z\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "commonjs",
            "main.cjs",
            new string[] { "dep.cjs", "main.cjs" },
            new string[] { "exports.value = {z:1, '2':2, '1':3};", "const dep = require('./dep.cjs'); console.log(Object.getOwnPropertyNames(dep.value).join(','));" },
            "1,2,z\n",
            false,
            true
        },
        new object[]
        {
            "hosted_keys",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export function keys(value: any) { return Object.keys(value); } export function names(value: any) { return Object.getOwnPropertyNames(value); } export function symbols(value: any) { return Object.getOwnPropertySymbols(value); }" },
            "",
            true,
            true
        },
        new object[]
        {
            "hosted_minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export const value = 1;" },
            "",
            true,
            true
        },
        new object[]
        {
            "symbol_redefinition",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let first = Symbol(\"first\");\nlet second = Symbol(\"second\");\nlet obj: any = {};\nobj[first] = 1;\nobj[second] = 2;\nObject.defineProperty(obj, first, { get: () => 3 });\nlet objectKeys = Object.getOwnPropertySymbols(obj);\nconsole.log(objectKeys[0] === first, objectKeys[1] === second);\n\nlet array: any = [];\narray[first] = 1;\narray[second] = 2;\nObject.defineProperty(array, first, { writable: false });\nlet arrayKeys = Object.getOwnPropertySymbols(array);\nconsole.log(arrayKeys[0] === first, arrayKeys[1] === second);\nconsole.log(Object.getOwnPropertyDescriptor(array, first)!.writable);" },
            "true true\ntrue true\nfalse\n",
            false,
            true
        },
    ];

    [Theory]
    [MemberData(nameof(OwnKeysMetadataPrograms))]
    public void Isolated_OwnKeysMetadata_PreservesEnumerationAndDeployment(
        string name, string entry, string[] paths, string[] sources, string expected, bool hosted, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        for (int i = 0; i < paths.Length; i++) tempDir.CreateFile(paths[i], sources[i]);
        var sourcePath = tempDir.GetPath(entry);
        var dllPath = tempDir.GetPath($"own-keys-metadata_{name}.dll");
        var deployment = standalone ? " --standalone" : "";
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{dllPath}\" --verify{deployment}{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }


    public static IEnumerable<object[]> ObjectPrototypeMetadataPrograms =>
    [
        new object[]
        {
            "minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(1);" },
            "1\n",
            false,
            true
        },
        new object[]
        {
            "get_created",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let proto = { x: 1 };\nlet obj = Object.create(proto);\nconsole.log(Object.getPrototypeOf(obj) === proto);" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "get_null",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj = Object.create(null);\nconsole.log(Object.getPrototypeOf(obj) === null);" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "set_changed",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let proto1 = { x: 1 };\nlet proto2 = { y: 2 };\nlet obj = Object.create(proto1);\nObject.setPrototypeOf(obj, proto2);\nconsole.log(Object.getPrototypeOf(obj) === proto2);" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "set_identity",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj = Object.create(null);\nlet result = Object.setPrototypeOf(obj, { x: 1 });\nconsole.log(result === obj);" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "set_inherited",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let proto = { x: 42, y: 100 };\nlet obj = Object.create(null);\nObject.setPrototypeOf(obj, proto);\nconsole.log(obj.x);\nconsole.log(obj.y);" },
            "42\n100\n",
            false,
            true
        },
        new object[]
        {
            "set_null",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let proto = { x: 1 };\nlet obj = Object.create(proto);\nObject.setPrototypeOf(obj, null);\nconsole.log(Object.getPrototypeOf(obj) === null);" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "nonextensible",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj = Object.create(null);\nObject.preventExtensions(obj);\nlet threw = false;\ntry {\n    Object.setPrototypeOf(obj, { x: 1 });\n} catch (e) {\n    threw = true;\n}\nconsole.log(threw);" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "class_set",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class MyClass {\n    x: number = 1;\n}\nlet obj = new MyClass();\nlet threw = false;\ntry {\n    Object.setPrototypeOf(obj, { y: 2 });\n} catch (e) {\n    threw = true;\n}\nconsole.log(threw);" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "chain",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function Base() {}\nconst b = new Base();\nconst d = Object.create(b);\nconst g = Object.create(d);\nconsole.log(b.isPrototypeOf(d));   // direct proto\nconsole.log(b.isPrototypeOf(g));    // transitive\nconsole.log(d.isPrototypeOf(b));    // reverse — false\nconsole.log(({}).isPrototypeOf(d)); // unrelated — false\nconsole.log(b.isPrototypeOf(5 as any)); // non-object arg — false" },
            "true\ntrue\nfalse\nfalse\nfalse\n",
            false,
            true
        },
        new object[]
        {
            "proxy_cycle",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let probed = false;\nconst prototype = new Proxy({}, {\n    getPrototypeOf() {\n        probed = true;\n        throw new Error(\"unexpected prototype probe\");\n    }\n});\nconst object = {};\nObject.setPrototypeOf(object, prototype);\nconsole.log(probed);\nconsole.log(Object.getPrototypeOf(object) === prototype);" },
            "false\ntrue\n",
            false,
            false
        },
        new object[]
        {
            "create_properties",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj = Object.create(null, {\n    x: { value: 42, writable: true, enumerable: true, configurable: true },\n    y: { value: 100, writable: true, enumerable: true, configurable: true }\n});\nconsole.log(obj.x);\nconsole.log(obj.y);" },
            "42\n100\n",
            false,
            true
        },
        new object[]
        {
            "create_nonwritable",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj = Object.create(null, {\n    readonly: { value: 42, writable: false, enumerable: true, configurable: true }\n});\nconsole.log(obj.readonly);\nobj.readonly = 100;\nconsole.log(obj.readonly);" },
            "42\n42\n",
            false,
            true
        },
        new object[]
        {
            "create_accessors",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj = Object.create(null, {\n    _value: { value: 10, writable: true, enumerable: true, configurable: true },\n    value: {\n        get: function() { return this._value; },\n        set: function(v: number) { this._value = v; },\n        enumerable: true,\n        configurable: true\n    }\n});\nconsole.log(obj.value);\nobj.value = 50;\nconsole.log(obj.value);" },
            "10\n50\n",
            false,
            true
        },
        new object[]
        {
            "create_nested",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let proto = { nested: { value: 42 } };\nlet obj = Object.create(proto);\nconsole.log(obj.nested.value);" },
            "42\n",
            false,
            true
        },
        new object[]
        {
            "create_class",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Point {\n    x: number;\n    y: number;\n    constructor(x: number, y: number) {\n        this.x = x;\n        this.y = y;\n    }\n}\nlet proto = new Point(10, 20);\nlet obj = Object.create(proto);\nconsole.log(obj.x);\nconsole.log(obj.y);" },
            "10\n20\n",
            false,
            true
        },
        new object[]
        {
            "create_keys",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let proto = { a: 1, b: 2 };\nlet obj = Object.create(proto);\n// ECMA-262 §20.1.2.16 Object.keys: own enumerable keys only.\n// Object.create(proto) returns a FRESH empty object with [[Prototype]]\n// = proto. proto's keys are reached via the prototype chain at\n// property-access time — they are NOT own keys of the created obj.\nlet keys = Object.keys(obj);\nconsole.log(keys.length);\n// Inherited access still works through the prototype chain.\nconsole.log(obj.a);\nconsole.log(obj.b);" },
            "0\n1\n2\n",
            false,
            true
        },
        new object[]
        {
            "create_invalid",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function attempt(label: string, fn: () => void) {\n    try { fn(); console.log(label, \"no throw\"); }\n    catch (e: any) { console.log(label, e instanceof TypeError); }\n}\nattempt(\"undefined\", () => Object.create(undefined as any));\nattempt(\"number\", () => Object.create(5 as any));\nattempt(\"string\", () => Object.create(\"x\" as any));\nattempt(\"bool\", () => Object.create(true as any));\n// null and objects are valid prototypes — must NOT throw.\nconsole.log(\"null\", typeof Object.create(null));\nconsole.log(\"obj\", typeof Object.create({}));" },
            "undefined true\nnumber true\nstring true\nbool true\nnull object\nobj object\n",
            false,
            true
        },
        new object[]
        {
            "create_value_form",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "var oc: any = Object.create;\nvar p = { x: 1 };\nvar o = oc(p);\nconsole.log(o.x);" },
            "1\n",
            false,
            true
        },
        new object[]
        {
            "class_methods",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class C {\n  method() { return \"plain\"; }\n  async asyncMethod() { return \"async\"; }\n  *generatorMethod() { yield \"generator\"; }\n}\nconst first: any = C.prototype;\nconst second: any = C.prototype;\nconsole.log(first === second);\nconsole.log(first.method());\nfirst.asyncMethod().then((value: any) => console.log(value));\nconsole.log(first.generatorMethod().next().value);" },
            "true\nplain\ngenerator\nasync\n",
            false,
            true
        },
        new object[]
        {
            "class_constructor",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let calls = 0;\nclass Base { base() { return \"base\"; } }\nclass Derived extends Base {\n  constructor() { super(); calls++; }\n  own() { return \"own\"; }\n}\nconst prototype: any = Derived.prototype;\nconsole.log(calls);\nconsole.log(prototype.own());\nconsole.log(prototype.base());\nconsole.log(Object.getPrototypeOf(prototype) === Base.prototype);" },
            "0\nown\nbase\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "class_fields",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let effects = 0;\nclass Base {\n  field: number = effects++;\n  constructor() { effects += 10; }\n  base() { return \"base\"; }\n}\nclass Derived extends Base {\n  derivedField: number = effects++;\n  constructor() { super(); effects += 100; }\n  own() { return \"own\"; }\n}\nconst prototype: any = Derived.prototype;\nconsole.log(effects);\nconsole.log(prototype.base(), prototype.own());" },
            "0\nbase own\n",
            false,
            true
        },
        new object[]
        {
            "class_static",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let constructorCalls = 0;\nclass C {\n  static observed: any = C.prototype.method();\n  constructor() { constructorCalls++; }\n  method() { return \"prototype\"; }\n}\nconsole.log(C.observed);\nconsole.log(constructorCalls);" },
            "prototype\n0\n",
            false,
            true
        },
        new object[]
        {
            "class_expression",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let constructorCalls = 0;\nconst C: any = class {\n  value: string = \"instance\";\n  constructor() { constructorCalls++; }\n  method() { return \"prototype\"; }\n};\nconsole.log(C.prototype.method());\nconsole.log(constructorCalls);" },
            "prototype\n0\n",
            false,
            true
        },
        new object[]
        {
            "reflect_current",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const object: any = {};\nObject.preventExtensions(object);\nconsole.log(Reflect.setPrototypeOf(object, Object.prototype));\ntry {\n    Reflect.setPrototypeOf({}, 1 as any);\n} catch (error) {\n    console.log(error instanceof TypeError);\n}" },
            "true\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "proxy_callable_brand",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function fn() { return 1; } const proxy: any = new Proxy(fn, {}); console.log(Object.prototype.toString.call(proxy));" },
            "[object Function]\n",
            false,
            false
        },
        new object[]
        {
            "locale_value",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const target: any = {toString() { return 'live'; }};\nconsole.log(Object.prototype.toLocaleString.call(target)); console.log(Object.prototype.valueOf.call(target) === target);\nfor (const method of [Object.prototype.valueOf, Object.prototype.toLocaleString]) {\n for (const value of [null, undefined]) { try { method.call(value); } catch (e: any) { console.log(e instanceof TypeError); } }\n}" },
            "live\ntrue\ntrue\ntrue\ntrue\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "promise_prototypes",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const p: any = Promise.resolve(1); console.log(Object.getPrototypeOf(p) === Promise.prototype);\nnew Promise((resolve: any, reject: any) => { console.log(Object.getPrototypeOf(resolve) === Function.prototype, Object.getPrototypeOf(reject) === Function.prototype); resolve(1); });" },
            "true\ntrue true\n",
            false,
            true
        },
        new object[]
        {
            "create_null_properties",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "try { Object.create({}, null); } catch (e: any) { console.log(e instanceof TypeError); }\nconst make: any = Object.create; const prototype = {x: 2}; const value = make(prototype);\nconsole.log(Object.getPrototypeOf(value) === prototype, value.x, Object.keys(value).length);" },
            "true\ntrue 2 0\n",
            false,
            true
        },
        new object[]
        {
            "esm",
            "main.ts",
            new string[] { "dep.ts", "main.ts" },
            new string[] { "export let calls = 0; export class Base { value() { return 'base'; } } export class Derived extends Base { constructor() { super(); calls++; } }", "import {Base, Derived, calls} from './dep'; const p: any = Derived.prototype; console.log(calls, Object.getPrototypeOf(p) === Base.prototype, p.value());" },
            "0 true base\n",
            false,
            true
        },
        new object[]
        {
            "commonjs",
            "main.cjs",
            new string[] { "dep.cjs", "main.cjs" },
            new string[] { "exports.prototype = {x: 3}; exports.value = Object.create(exports.prototype);", "const dep = require('./dep.cjs'); console.log(Object.getPrototypeOf(dep.value) === dep.prototype, dep.value.x);" },
            "true 3\n",
            false,
            true
        },
        new object[]
        {
            "hosted_prototypes",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export class Base { value() { return 2; } } export class Derived extends Base {} export function prototype() { return Object.getPrototypeOf(Derived.prototype); }" },
            "",
            true,
            true
        },
        new object[]
        {
            "hosted_minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export const value = 1;" },
            "",
            true,
            true
        },
        new object[]
        {
            "builtin_brands",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const brand: any = Object.prototype.toString;\nfor (const value of [null, undefined, true, 2, 's', [], {}, new Date(0), /x/, new Error('x'), Promise.resolve(1), JSON, Math]) console.log(brand.call(value));\n" },
            "[object Null]\n[object Undefined]\n[object Boolean]\n[object Number]\n[object String]\n[object Array]\n[object Object]\n[object Date]\n[object RegExp]\n[object Error]\n[object Promise]\n[object JSON]\n[object Math]\n",
            false,
            true
        },
    ];

    [Theory]
    [MemberData(nameof(ObjectPrototypeMetadataPrograms))]
    public void Isolated_ObjectPrototypeMetadata_PreservesPrototypesAndDeployment(
        string name, string entry, string[] paths, string[] sources, string expected, bool hosted, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        for (int i = 0; i < paths.Length; i++) tempDir.CreateFile(paths[i], sources[i]);
        var sourcePath = tempDir.GetPath(entry);
        var dllPath = tempDir.GetPath($"object-prototype-metadata_{name}.dll");
        var deployment = standalone ? " --standalone" : "";
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{dllPath}\" --verify{deployment}{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }


    public static IEnumerable<object[]> ObjectDescriptorMetadataPrograms =>
    [
        new object[]
        {
            "minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(1);" },
            "1\n",
            false,
            true
        },
        new object[]
        {
            "data",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = {};\nObject.defineProperty(obj, \"x\", { value: 42, writable: true, enumerable: true, configurable: true });\nconsole.log(obj.x);" },
            "42\n",
            false,
            true
        },
        new object[]
        {
            "write_storage",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let sloppyObject: any = {};\nObject.defineProperty(sloppyObject, \"x\", { value: 42, writable: true });\nsloppyObject.x = 100;\nconsole.log(sloppyObject.x);\nconsole.log(Object.getOwnPropertyDescriptor(sloppyObject, \"x\").value);\n\nlet strictObject: any = {};\nObject.defineProperty(strictObject, \"x\", { value: 42, writable: true });\nfunction assignStrict() {\n    \"use strict\";\n    strictObject.x = 200;\n}\nassignStrict();\nconsole.log(strictObject.x);\nconsole.log(Object.getOwnPropertyDescriptor(strictObject, \"x\").value);" },
            "100\n100\n200\n200\n",
            false,
            true
        },
        new object[]
        {
            "class_data",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Point {\n    x: number;\n    constructor(x: number) {\n        this.x = x;\n    }\n}\nlet p = new Point(10);\nObject.defineProperty(p, \"y\", { value: 20, writable: true, enumerable: true, configurable: true });\nconsole.log(p.x);\nconsole.log((p as any).y);" },
            "10\n20\n",
            false,
            true
        },
        new object[]
        {
            "array_data",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let arr: any = [1, 2, 3];\nObject.defineProperty(arr, \"customProp\", { value: \"hello\", writable: true, enumerable: true, configurable: true });\nconsole.log(arr.customProp);\nconsole.log(arr[0]);" },
            "hello\n1\n",
            false,
            true
        },
        new object[]
        {
            "array_length",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let arr: any = [1, 2, 3];\nlet desc = Object.getOwnPropertyDescriptor(arr, \"length\");\nconsole.log(desc.value);\nconsole.log(desc.writable);\nconsole.log(desc.enumerable);\nconsole.log(desc.configurable);" },
            "3\ntrue\nfalse\nfalse\n",
            false,
            true
        },
        new object[]
        {
            "class_descriptor",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Point {\n    x: number;\n    constructor(x: number) {\n        this.x = x;\n    }\n}\nlet p = new Point(42);\nlet desc = Object.getOwnPropertyDescriptor(p, \"x\");\nconsole.log(desc.value);\nconsole.log(desc.writable);" },
            "42\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "roundtrip",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = {};\nObject.defineProperty(obj, \"name\", {\n    value: \"Alice\",\n    writable: true,\n    enumerable: false,\n    configurable: true\n});\nlet desc = Object.getOwnPropertyDescriptor(obj, \"name\");\nconsole.log(desc.value);\nconsole.log(desc.writable);\nconsole.log(desc.enumerable);\nconsole.log(desc.configurable);" },
            "Alice\ntrue\nfalse\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "getter_setter",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = { _value: 10 };\nObject.defineProperty(obj, \"value\", {\n    get: function() { return this._value; },\n    set: function(v: number) { this._value = v; },\n    enumerable: true,\n    configurable: true\n});\nconsole.log(obj.value);\nobj.value = 50;\nconsole.log(obj.value);" },
            "10\n50\n",
            false,
            true
        },
        new object[]
        {
            "accessor",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = { _x: 5 };\nObject.defineProperty(obj, \"x\", {\n    get: function() { return this._x; },\n    enumerable: true,\n    configurable: true\n});\nlet desc = Object.getOwnPropertyDescriptor(obj, \"x\");\nconsole.log(typeof desc.get);\nconsole.log(desc.enumerable);\nconsole.log(desc.configurable);" },
            "function\ntrue\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "bound_accessors",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let backing = 0;\nfunction myGetter(): number { return backing; }\nfunction mySetter(v: number): void { backing = v; }\nlet obj: any = {};\nObject.defineProperty(obj, \"val\", {\n    get: myGetter,\n    set: mySetter,\n    enumerable: true,\n    configurable: true\n});\nobj.val = 42;\nconsole.log(obj.val);" },
            "42\n",
            false,
            true
        },
        new object[]
        {
            "arrow_accessors",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let storage: any = { _count: 0 };\nlet obj: any = {};\nObject.defineProperty(obj, \"count\", {\n    get: () => storage._count,\n    set: (v: number) => { storage._count = v; },\n    enumerable: true,\n    configurable: true\n});\nobj.count = 10;\nconsole.log(obj.count);\nconsole.log(storage._count);" },
            "10\n10\n",
            false,
            true
        },
        new object[]
        {
            "accessor_identity",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const getter = function (): number { return 1; };\nconst setter = function (value: number): void {};\nconst descriptors: any = {\n    get: getter,\n    set: setter,\n    configurable: true\n};\nconst prototype: any = {};\nObject.defineProperty(prototype, 'value', descriptors);\nconst subject: any = Object.create(prototype);\nconsole.log(subject.__lookupGetter__('value') === descriptors.get);\nconsole.log(subject.__lookupSetter__('value') === descriptors.set);" },
            "true\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "inherited_value",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const proto: any = {};\nObject.defineProperty(proto, \"value\", { set() {} });\nconst Ctor: any = function () {};\nCtor.prototype = proto;\nconst child: any = new Ctor();\nconst o: any = { property: 120 };\nObject.defineProperty(o, \"property\", child);\nconsole.log(typeof o.property);" },
            "undefined\n",
            false,
            true
        },
        new object[]
        {
            "partial",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const o: any = { a: 42 };\nObject.defineProperty(o, \"a\", { writable: false });\nconsole.log(o.a);" },
            "42\n",
            false,
            true
        },
        new object[]
        {
            "regexp_inherited",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "(RegExp.prototype as any).enumerable = true;\nconst regObj: any = new RegExp();\nconst obj: any = {};\nObject.defineProperty(obj, \"property\", regObj);\nlet seen = false;\nfor (const p in obj) if (p === \"property\") seen = true;\nconsole.log(seen);\nconsole.log((regObj as any).enumerable);" },
            "true\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "define_many",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = {};\nObject.defineProperties(obj, {\n    name: { value: \"Alice\", writable: true, enumerable: true, configurable: true },\n    age: { value: 30, writable: true, enumerable: true, configurable: true }\n});\nconsole.log(obj.name);\nconsole.log(obj.age);" },
            "Alice\n30\n",
            false,
            true
        },
        new object[]
        {
            "define_accessors",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = { _value: 0 };\nObject.defineProperties(obj, {\n    value: {\n        get: function() { return obj._value; },\n        set: function(v: number) { obj._value = v * 2; },\n        enumerable: true,\n        configurable: true\n    }\n});\nobj.value = 5;\nconsole.log(obj.value);\nconsole.log(obj._value);" },
            "10\n10\n",
            false,
            true
        },
        new object[]
        {
            "all_roundtrip",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let original: any = { a: 1, b: \"hello\" };\nlet descs = Object.getOwnPropertyDescriptors(original);\nlet copy: any = Object.defineProperties({}, descs);\nconsole.log(copy.a);\nconsole.log(copy.b);" },
            "1\nhello\n",
            false,
            true
        },
        new object[]
        {
            "proxy_missing",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const target: any = { attr: 1 };\nconst proxy: any = new Proxy(target, {});\nconst descriptor: any = Object.getOwnPropertyDescriptor(proxy, \"attr\");\nconsole.log(descriptor.value);\nconsole.log(descriptor.writable);\nconsole.log(descriptor.enumerable);\nconsole.log(descriptor.configurable);\nconsole.log(proxy.hasOwnProperty(\"attr\"));" },
            "1\ntrue\ntrue\ntrue\ntrue\n",
            false,
            false
        },
        new object[]
        {
            "proxy_define",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const target: any = {};\nlet sawWritableField = false;\nlet sawWritableValue: any;\nconst proxy: any = new Proxy(target, {\n    defineProperty(inner: any, key: string, descriptor: any) {\n        sawWritableField = Object.prototype.hasOwnProperty.call(\n            descriptor, \"writable\");\n        sawWritableValue = descriptor.writable;\n        Object.defineProperty(inner, key, {\n            configurable: false,\n            writable: true\n        });\n        return true;\n    }\n});\ntry {\n    Reflect.defineProperty(proxy, \"prop\", { writable: false });\n    console.log(false);\n} catch (error) {\n    console.log(error instanceof TypeError);\n}\nconsole.log(sawWritableField);\nconsole.log(sawWritableValue);\nconst descriptor: any = Object.getOwnPropertyDescriptor(target, \"prop\");\nconsole.log(descriptor.writable);\nconsole.log(descriptor.configurable);" },
            "true\ntrue\nfalse\ntrue\nfalse\n",
            false,
            false
        },
        new object[]
        {
            "proxy_traps",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let ordinaryGets = 0;\nconst proxy: any = new Proxy({}, {\n    ownKeys(): string[] { return ['hidden']; },\n    getOwnPropertyDescriptor(): any { return undefined; },\n    get(): any { ordinaryGets++; throw new Error('unexpected get'); }\n});\nconsole.log(Reflect.ownKeys(proxy).join(','));\nconsole.log(Object.getOwnPropertyDescriptor(proxy, 'hidden') === undefined);\nconsole.log(Object.keys(Object.getOwnPropertyDescriptors(proxy)).length);\nconsole.log(ordinaryGets);" },
            "hidden\ntrue\n0\n0\n",
            false,
            false
        },
        new object[]
        {
            "array_nonwritable",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "var values = [];\nObject.defineProperty(values, \"0\", { value: 12 });\nvalues[0] = 99;\nconsole.log(values[0]);\nconsole.log(values.length);" },
            "12\n1\n",
            false,
            true
        },
        new object[]
        {
            "array_generic",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "Object.defineProperty(Array.prototype, \"0\", {\n  value: 11,\n  configurable: true\n});\nvar values = [];\nObject.defineProperty(values, \"0\", { configurable: false });\nconsole.log(typeof values[0]);\nconsole.log(values.length);\ndelete Array.prototype[0];" },
            "undefined\n1\n",
            false,
            true
        },
        new object[]
        {
            "accessor_replace",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "var objectValue: any = { 0: 11 };\nObject.defineProperty(objectValue, \"0\", {\n  get: function() { return 7; },\n  configurable: true\n});\nconsole.log(objectValue[0]);" },
            "7\n",
            false,
            true
        },
        new object[]
        {
            "symbols",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const key = Symbol('key'); const target: any = {};\nObject.defineProperty(target, key, { value: 3, writable: true, enumerable: false, configurable: true });\nconst descriptor: any = Object.getOwnPropertyDescriptor(target, key);\nconst all: any = Object.getOwnPropertyDescriptors(target);\nconsole.log(descriptor.value, descriptor.writable, descriptor.enumerable, descriptor.configurable);\nconsole.log(all[key].value, Object.getOwnPropertySymbols(all)[0] === key);\nObject.defineProperty(target, key, { value: 4 }); console.log(target[key]);" },
            "3 true false true\n3 true\n4\n",
            false,
            true
        },
        new object[]
        {
            "json_math",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "for (const key of ['parse', 'stringify', 'isRawJSON', 'rawJSON']) {\n const descriptor: any = Object.getOwnPropertyDescriptor(JSON, key);\n console.log(descriptor.value === (JSON as any)[key], descriptor.writable, descriptor.enumerable, descriptor.configurable);\n}\nconst pi: any = Object.getOwnPropertyDescriptor(Math, 'PI');\nconsole.log(pi.value === Math.PI, pi.writable, pi.enumerable, pi.configurable);" },
            "true true false true\ntrue true false true\ntrue true false true\ntrue true false true\ntrue false false false\n",
            false,
            true
        },
        new object[]
        {
            "functions",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function abc(a: number, b: number) { return a + b; }\nfor (const key of ['name', 'length']) { const d: any = Object.getOwnPropertyDescriptor(abc, key); console.log(d.value === (abc as any)[key], d.writable, d.enumerable, d.configurable); }\nconst prototype: any = Object.getOwnPropertyDescriptor(abc, 'prototype');\nconsole.log(prototype.value === (abc as any).prototype, prototype.writable, prototype.enumerable, prototype.configurable);" },
            "true false false true\ntrue false false true\ntrue true false false\n",
            false,
            true
        },
        new object[]
        {
            "promise_callbacks",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "new Promise((resolve: any, reject: any) => {\n for (const fn of [resolve, reject]) { const d: any = Object.getOwnPropertyDescriptor(fn, 'length'); console.log(d.value === fn.length, d.writable, d.enumerable, d.configurable); }\n resolve(1);\n});" },
            "true false false true\ntrue false false true\n",
            false,
            true
        },
        new object[]
        {
            "array_length_coercion",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let log = ''; const values: any = [1, 2, 3];\nconst length: any = { valueOf() { log += 'v'; return 1; } };\nObject.defineProperty(values, 'length', { value: length });\nconsole.log(log, values.length, values[0], values[1] === undefined);\ntry { Object.defineProperty(values, 'length', {value: 1.5}); } catch (e: any) { console.log(e instanceof RangeError); }" },
            "vv 1 1 true\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "esm",
            "main.ts",
            new string[] { "dep.ts", "main.ts" },
            new string[] { "export const target = {}; Object.defineProperty(target, 'x', {value: 7});", "import {target} from './dep'; const d = Object.getOwnPropertyDescriptor(target, 'x'); console.log(d.value, d.writable);" },
            "7 false\n",
            false,
            true
        },
        new object[]
        {
            "commonjs",
            "main.cjs",
            new string[] { "dep.cjs", "main.cjs" },
            new string[] { "exports.target = {}; Object.defineProperty(exports.target, 'x', {value: 9, enumerable: true});", "const dep = require('./dep.cjs'); const all = Object.getOwnPropertyDescriptors(dep.target); console.log(all.x.value, all.x.enumerable);" },
            "9 true\n",
            false,
            true
        },
        new object[]
        {
            "hosted_descriptor",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export function descriptor() { const target = {}; Object.defineProperty(target, 'x', {value: 2}); return Object.getOwnPropertyDescriptor(target, 'x'); }" },
            "",
            true,
            true
        },
        new object[]
        {
            "hosted_minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export const value = 1;" },
            "",
            true,
            true
        },
    ];

    [Theory]
    [MemberData(nameof(ObjectDescriptorMetadataPrograms))]
    public void Isolated_ObjectDescriptorMetadata_PreservesDescriptorsAndDeployment(
        string name, string entry, string[] paths, string[] sources, string expected, bool hosted, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        for (int i = 0; i < paths.Length; i++) tempDir.CreateFile(paths[i], sources[i]);
        var sourcePath = tempDir.GetPath(entry);
        var dllPath = tempDir.GetPath($"object-descriptor-metadata_{name}.dll");
        var deployment = standalone ? " --standalone" : "";
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{dllPath}\" --verify{deployment}{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }


    public static IEnumerable<object[]> ErrorMetadataPrograms =>
    [
        new object[]
        {
            "minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(1);" },
            "1\n",
            false,
            true
        },
        new object[]
        {
            "empty",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let e = new Error();\nconsole.log(e.name);\nconsole.log(e.message);" },
            "Error\n\n",
            false,
            true
        },
        new object[]
        {
            "call",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let e = Error('Without new');\nconsole.log(e.name);\nconsole.log(e.message);" },
            "Error\nWithout new\n",
            false,
            true
        },
        new object[]
        {
            "coercion",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let e = new TypeError('boom');\nconsole.log(String(e));\nconsole.log(`${e}`);\nconsole.log('' + e);" },
            "TypeError: boom\nTypeError: boom\nTypeError: boom\n",
            false,
            true
        },
        new object[]
        {
            "aggregate",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let errors = [new Error('First'), new Error('Second')];\nlet e = new AggregateError(errors, 'Multiple errors');\nconsole.log(e.name);\nconsole.log(e.message);\nconsole.log(e.errors.length);" },
            "AggregateError\nMultiple errors\n2\n",
            false,
            true
        },
        new object[]
        {
            "mutable_name",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let e = new Error('Test');\ne.name = 'CustomError';\nconsole.log(e.name);" },
            "CustomError\n",
            false,
            true
        },
        new object[]
        {
            "mutable_message",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let e = new Error('Original');\ne.message = 'Modified';\nconsole.log(e.message);" },
            "Modified\n",
            false,
            true
        },
        new object[]
        {
            "rethrow",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "try {\n    try {\n        throw new RangeError('Inner error');\n    } catch (inner) {\n        inner.message = 'Modified in inner';\n        throw inner;\n    }\n} catch (outer) {\n    console.log(outer.name);\n    console.log(outer.message);\n}" },
            "RangeError\nModified in inner\n",
            false,
            true
        },
        new object[]
        {
            "cause_order",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const sequence: string[] = [];\nconst error = new Error(\n    ({ toString() { sequence.push(\"message\"); return \"converted\"; } } as any),\n    { get cause() { sequence.push(\"cause\"); return 42; } }\n);\nconsole.log(sequence.join(\",\"));\nconsole.log(error.message, error.cause);" },
            "message,cause\nconverted 42\n",
            false,
            true
        },
        new object[]
        {
            "symbol_message",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "try {\n    Error(Symbol() as any);\n    console.log(\"no error\");\n} catch (error) {\n    console.log(error instanceof TypeError);\n}" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "prototype_descriptor",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const descriptor = Object.getOwnPropertyDescriptor(Error, \"prototype\")!;\nconsole.log(descriptor.writable, descriptor.enumerable, descriptor.configurable);\nconsole.log(delete (Error as any).prototype);\nconsole.log(Error.prototype === descriptor.value);" },
            "false false false\nfalse\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "prototype_inherited",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const constructed = new Error(\"constructed\");\nconst called = Error(\"called\");\nconsole.log(Error.prototype.isPrototypeOf(constructed));\nconsole.log(Error.prototype.isPrototypeOf(called));\nconsole.log(Error.prototype.hasOwnProperty(\"message\"));" },
            "true\ntrue\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "unbound",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "Object.defineProperty(globalThis, \"name\", {\n    get() { throw new Error(\"name getter called\"); }\n});\nconst toString = Error.prototype.toString;\ntry {\n    toString();\n    console.log(\"no error\");\n} catch (error) {\n    console.log(error instanceof TypeError);\n}" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "cause",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const root = new Error('root');\nconst mid = new Error('middle', { cause: root });\nconst top = new Error('top', { cause: mid });\nconsole.log(top.message);\nconsole.log(top.cause.message);\nconsole.log(top.cause.cause.message);" },
            "top\nmiddle\nroot\n",
            false,
            true
        },
        new object[]
        {
            "subclass",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class CustomTypeError extends TypeError {\n    constructor(msg) {\n        super(msg);\n        this.name = 'CustomTypeError';\n    }\n}\nconst e = new CustomTypeError('bad type');\nconsole.log(e.name);\nconsole.log(e.message);\nconsole.log(e instanceof CustomTypeError);\nconsole.log(e instanceof TypeError);\nconsole.log(e instanceof Error);" },
            "CustomTypeError\nbad type\ntrue\ntrue\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "multi_subclass",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class AppError extends Error {\n    code: number;\n    constructor(msg, code) {\n        super(msg);\n        this.name = 'AppError';\n        this.code = code;\n    }\n}\nclass HttpError extends AppError {\n    constructor(msg) {\n        super(msg, 500);\n    }\n}\nconst e = new HttpError('server error');\nconsole.log(e.name);\nconsole.log(e.message);\nconsole.log(e.code);\nconsole.log(e instanceof HttpError);\nconsole.log(e instanceof AppError);\nconsole.log(e instanceof Error);" },
            "AppError\nserver error\n500\ntrue\ntrue\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "class_expression",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const Custom = class extends TypeError {};\nconst error: any = new Custom();\nconsole.log(error instanceof Custom, error instanceof TypeError, error instanceof Error);" },
            "true true true\n",
            false,
            true
        },
        new object[]
        {
            "guest_identity",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const original = new RangeError('mine');\ntry { throw original; } catch (e: any) { console.log(e === original, e instanceof RangeError); }\ntry { throw 'plain string'; } catch (e: any) { console.log(typeof e, e); }" },
            "true true\nstring plain string\n",
            false,
            true
        },
        new object[]
        {
            "shaped_string",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "try { throw \"RangeError: hand-rolled, not a real error\"; }\ncatch (e: any) {\n  console.log(typeof e);\n  console.log(e);\n  console.log(e instanceof Error);\n}" },
            "string\nRangeError: hand-rolled, not a real error\nfalse\n",
            false,
            true
        },
        new object[]
        {
            "generator",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function* g() {\n  const o: any = undefined;\n  try { yield 0; o.foo(); } catch (e: any) { console.log((e instanceof TypeError) + \" \" + e.name); }\n  yield 1;\n}\nconst it = g(); it.next(); it.next();" },
            "true TypeError\n",
            false,
            true
        },
        new object[]
        {
            "native_types",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const kinds: any[] = [Error, TypeError, RangeError, ReferenceError, SyntaxError, URIError, EvalError];\nfor (const Kind of kinds) { const e: any = new Kind('msg', {cause: 3}); console.log(e.name, e.message, e.cause, e instanceof Kind, e instanceof Error); }" },
            "Error msg 3 true true\nTypeError msg 3 true true\nRangeError msg 3 true true\nReferenceError msg 3 true true\nSyntaxError msg 3 true true\nURIError msg 3 true true\nEvalError msg 3 true true\n",
            false,
            true
        },
        new object[]
        {
            "promise",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const e = new RangeError('reject');\nPromise.reject(e).catch((x: any) => console.log(x === e, x instanceof RangeError, x.message));" },
            "true true reject\n",
            false,
            true
        },
        new object[]
        {
            "proxy_options",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let trace = ''; const cause = {};\nconst options = new Proxy({}, { has(target: any, key: any) { trace += 'h'; return key === 'cause'; }, get(target: any, key: any) { trace += 'g'; return cause; }});\nconst error: any = new Error('proxy', options);\nconsole.log(trace, error.cause === cause);" },
            "hg true\n",
            false,
            false
        },
        new object[]
        {
            "esm",
            "main.ts",
            new string[] { "dep.ts", "main.ts" },
            new string[] { "export const value = new TypeError(\"module\");", "import {value} from \"./dep\"; console.log(value instanceof TypeError, value.message);" },
            "true module\n",
            false,
            true
        },
        new object[]
        {
            "commonjs",
            "main.cjs",
            new string[] { "dep.cjs", "main.cjs" },
            new string[] { "exports.value = new TypeError(\"module\");", "const dep = require(\"./dep.cjs\"); console.log(dep.value instanceof TypeError, dep.value.message);" },
            "true module\n",
            false,
            true
        },
        new object[]
        {
            "hosted_error",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export function value(input: string) { try { throw new TypeError(input); } catch (e: any) { return e.message; } }" },
            "",
            true,
            true
        },
        new object[]
        {
            "hosted_minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export const value = 1;" },
            "",
            true,
            true
        },
    ];

    [Theory]
    [MemberData(nameof(ErrorMetadataPrograms))]
    public void Isolated_ErrorMetadata_PreservesErrorsAndDeployment(
        string name, string entry, string[] paths, string[] sources, string expected, bool hosted, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        for (int i = 0; i < paths.Length; i++) tempDir.CreateFile(paths[i], sources[i]);
        var sourcePath = tempDir.GetPath(entry);
        var dllPath = tempDir.GetPath($"error-metadata_{name}.dll");
        var deployment = standalone ? " --standalone" : "";
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{dllPath}\" --verify{deployment}{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }


    public static IEnumerable<object[]> ObjectStateMetadataPrograms =>
    [
        new object[]
        {
            "minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(1);" },
            "1\n",
            false,
            true
        },
        new object[]
        {
            "freeze_identity",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: { a: number } = { a: 1 };\nlet frozen = Object.freeze(obj);\nconsole.log(frozen === obj);" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "freeze_write",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: { a: number } = { a: 1 };\nObject.freeze(obj);\nobj.a = 100;\nconsole.log(obj.a);" },
            "1\n",
            false,
            true
        },
        new object[]
        {
            "freeze_add",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = { a: 1 };\nObject.freeze(obj);\nobj.b = 2;\nconsole.log(obj.a);\nconsole.log(obj.b === undefined || obj.b === null);" },
            "1\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "frozen_flag",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: { a: number } = { a: 1 };\nObject.freeze(obj);\nconsole.log(Object.isFrozen(obj));" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "primitives",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(Object.isFrozen(null));\nconsole.log(Object.isFrozen(42));\nconsole.log(Object.isFrozen(\"hello\"));" },
            "true\ntrue\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "seal_write",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: { a: number } = { a: 1 };\nObject.seal(obj);\nobj.a = 100;\nconsole.log(obj.a);" },
            "100\n",
            false,
            true
        },
        new object[]
        {
            "seal_add",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = { a: 1 };\nObject.seal(obj);\nobj.b = 2;\nconsole.log(obj.a);\nconsole.log(obj.b === undefined || obj.b === null);" },
            "1\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "frozen_sealed",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: { a: number } = { a: 1 };\nObject.freeze(obj);\nconsole.log(Object.isSealed(obj));" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "array_frozen",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let arr: number[] = [1, 2, 3];\nObject.freeze(arr);\narr[0] = 100;\nconsole.log(arr[0]);" },
            "1\n",
            false,
            true
        },
        new object[]
        {
            "array_sealed",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let arr: number[] = [1, 2, 3];\nObject.seal(arr);\narr[0] = 100;\nconsole.log(arr[0]);" },
            "100\n",
            false,
            true
        },
        new object[]
        {
            "class_frozen",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Point {\n    x: number;\n    y: number;\n    constructor(x: number, y: number) {\n        this.x = x;\n        this.y = y;\n    }\n}\nlet p = new Point(10, 20);\nObject.freeze(p);\np.x = 100;\nconsole.log(p.x);\nconsole.log(p.y);" },
            "10\n20\n",
            false,
            true
        },
        new object[]
        {
            "class_sealed",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Point {\n    x: number;\n    y: number;\n    constructor(x: number, y: number) {\n        this.x = x;\n        this.y = y;\n    }\n}\nlet p = new Point(10, 20);\nObject.seal(p);\np.x = 100;\nconsole.log(p.x);\nconsole.log(p.y);" },
            "100\n20\n",
            false,
            true
        },
        new object[]
        {
            "shallow",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = { nested: { value: 1 } };\nObject.freeze(obj);\nobj.nested.value = 100;\nconsole.log(obj.nested.value);" },
            "100\n",
            false,
            true
        },
        new object[]
        {
            "prevent_add",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = { x: 1 };\nObject.preventExtensions(obj);\nobj.y = 2;\nconsole.log(obj.y === undefined);" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "prevent_write",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = { x: 1 };\nObject.preventExtensions(obj);\nobj.x = 100;\nconsole.log(obj.x);" },
            "100\n",
            false,
            true
        },
        new object[]
        {
            "prevent_delete",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = { x: 1, y: 2 };\nObject.preventExtensions(obj);\ndelete obj.y;\nconsole.log(obj.y === undefined);" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "prevent_identity",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = { x: 1 };\nlet result = Object.preventExtensions(obj);\nconsole.log(result === obj);" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "prevent_array_write",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let arr: number[] = [1, 2, 3];\nObject.preventExtensions(arr);\narr[0] = 100;\nconsole.log(arr[0]);" },
            "100\n",
            false,
            true
        },
        new object[]
        {
            "callable",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function callback(): void {}\nconsole.log(Object.isExtensible(callback));\nconsole.log(Reflect.isExtensible(callback));\nObject.preventExtensions(callback);\nconsole.log(Object.isExtensible(callback));\nconsole.log(Reflect.isExtensible(callback));" },
            "true\ntrue\nfalse\nfalse\n",
            false,
            true
        },
        new object[]
        {
            "extensible_primitives",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(Object.isExtensible(42));\nconsole.log(Object.isExtensible(\"hello\"));" },
            "false\nfalse\n",
            false,
            true
        },
        new object[]
        {
            "prevent_class",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Point {\n    x: number;\n    constructor(x: number) {\n        this.x = x;\n    }\n}\nlet p: any = new Point(10);\nObject.preventExtensions(p);\nconsole.log(Object.isExtensible(p));\np.y = 20;  // Should be ignored\nconsole.log(p.y === undefined);" },
            "false\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "reflect_prevent",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let obj: any = {};\nlet result: boolean = Reflect.preventExtensions(obj);\nconsole.log(result);\nconsole.log(Reflect.isExtensible(obj));" },
            "true\nfalse\n",
            false,
            true
        },
        new object[]
        {
            "proxy_normal_deployment",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const target: any = [];\nconst proxy: any = new Proxy(new Proxy(target, {}), {});\nconsole.log(Reflect.defineProperty(proxy, \"x\", { value: 1 }));\nconsole.log(target.x);\nObject.preventExtensions(target);\nconsole.log(Reflect.defineProperty(proxy, \"y\", { value: 2 }));\nconsole.log(Reflect.set(proxy, \"z\", 3));" },
            "true\n1\nfalse\nfalse\n",
            false,
            false
        },
        new object[]
        {
            "esm",
            "main.ts",
            new string[] { "dep.ts", "main.ts" },
            new string[] { "export const value = Object.freeze({a:1});", "import {value} from \"./dep\"; console.log(Object.isFrozen(value), value.a);" },
            "true 1\n",
            false,
            true
        },
        new object[]
        {
            "commonjs",
            "main.cjs",
            new string[] { "dep.cjs", "main.cjs" },
            new string[] { "exports.value = Object.freeze({a:1});", "const dep = require(\"./dep.cjs\"); console.log(Object.isFrozen(dep.value), dep.value.a);" },
            "true 1\n",
            false,
            true
        },
        new object[]
        {
            "hosted_state",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export function value(input: any) { Object.seal(input); return Object.isExtensible(input); }" },
            "",
            true,
            true
        },
        new object[]
        {
            "hosted_minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export const value = 1;" },
            "",
            true,
            true
        },
    ];

    [Theory]
    [MemberData(nameof(ObjectStateMetadataPrograms))]
    public void Isolated_ObjectStateMetadata_PreservesIntegrityAndDeployment(
        string name, string entry, string[] paths, string[] sources, string expected, bool hosted, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        for (int i = 0; i < paths.Length; i++) tempDir.CreateFile(paths[i], sources[i]);
        var sourcePath = tempDir.GetPath(entry);
        var dllPath = tempDir.GetPath($"object-state-metadata_{name}.dll");
        var deployment = standalone ? " --standalone" : "";
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{dllPath}\" --verify{deployment}{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }


    public static IEnumerable<object[]> RegExpMetadataPrograms =>
    [
        new object[]
        {
            "minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(1);" },
            "1\n",
            false
        },
        new object[]
        {
            "captures",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let match: any = /((1)|(12))((3)|(23))/.exec(\"123\");\nconsole.log(match[0] + \":\" + match.index + \":\" + match.input);\nconsole.log(match[3] === undefined);" },
            "123:0:123\ntrue\n",
            false
        },
        new object[]
        {
            "lastindex_raw",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let reads = 0;\nlet counter: any = { valueOf: function (): any { reads++; return 0; } };\nlet re: any = /./;\nre.lastIndex = counter;\nlet match = re.exec(\"abc\");\nconsole.log(match[0] + \":\" + reads + \":\" + (re.lastIndex === counter));" },
            "a:1:true\n",
            false
        },
        new object[]
        {
            "lastindex_global",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let reads = 0;\nlet re: any = /./g;\nre.lastIndex = { valueOf: function (): any { reads++; return 1; } };\nlet match = re.exec(\"abc\");\nconsole.log(match[0] + \":\" + reads + \":\" + re.lastIndex);" },
            "b:1:2\n",
            false
        },
        new object[]
        {
            "exec_contract",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log((/undefined/).exec()[0]);\nlet exec: any = RegExp.prototype.exec;\ntry {\n    new exec();\n    console.log(\"constructed\");\n} catch (e) {\n    console.log(e instanceof TypeError);\n}" },
            "undefined\ntrue\n",
            false
        },
        new object[]
        {
            "exec_null",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let regexp: any = /ll|l/;\nlet match: any = regexp.exec(null);\nconsole.log(match instanceof Array);\nconsole.log(match[0] + \":\" + match.index + \":\" + match.input);" },
            "true\nll:2:null\n",
            false
        },
        new object[]
        {
            "match_replace_reset",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const matcher: any = /a/g;\nmatcher.lastIndex = 2;\nconst matches: any = \"aba\".match(matcher);\nconsole.log(matches.join(\",\") + \":\" + matcher.lastIndex);\n\nconst replacer: any = /a/g;\nreplacer.lastIndex = 2;\nconsole.log(\"aba\".replace(replacer, \"x\") + \":\" + replacer.lastIndex);" },
            "a,a:0\nxbx:0\n",
            false
        },
        new object[]
        {
            "symbol_accessor",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const intrinsicMatch: any = RegExp.prototype[Symbol.match];\nconst intrinsicExec: any = RegExp.prototype.exec;\nconst regexp: any = /a/g;\nlet order: string = \"\";\n\nObject.defineProperty(regexp, Symbol.match, {\n    configurable: true,\n    get: function (): any {\n        order = order + \"symbol>\";\n        return intrinsicMatch;\n    }\n});\nregexp.exec = function (input: string): any {\n    order = order + \"exec>\";\n    return intrinsicExec.call(this, input);\n};\n\nconsole.log(\"aba\".match(regexp).join(\",\"));\nconsole.log(order);" },
            "a,a\nsymbol>exec>exec>exec>\n",
            false
        },
        new object[]
        {
            "exec_accessor",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const intrinsicExec: any = RegExp.prototype.exec;\nlet gets: number = 0;\nlet calls: number = 0;\nObject.defineProperty(RegExp.prototype, \"exec\", {\n    configurable: true,\n    get: function (): any {\n        gets = gets + 1;\n        return function (input: string): any {\n            calls = calls + 1;\n            return intrinsicExec.call(this, input);\n        };\n    }\n});\n\nconsole.log(\"aba\".replace(/a/g, \"x\") + \":\" + gets + \":\" + calls);" },
            "xbx:3:3\n",
            false
        },
        new object[]
        {
            "groups_null",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const withNull: any = /x/;\nwithNull.exec = function(input: string): any {\n    return { 0: \"x\", length: 1, index: 0, input: input, groups: null };\n};\nconsole.log(\"x\".replace(withNull, function(): string {\n    console.log(arguments.length, arguments[3] === null);\n    return \"present\";\n}));\n\nconst withoutGroups: any = /x/;\nwithoutGroups.exec = function(input: string): any {\n    return { 0: \"x\", length: 1, index: 0, input: input };\n};\nconsole.log(\"x\".replace(withoutGroups, function(): string {\n    console.log(arguments.length, arguments[3] === undefined);\n    return \"absent\";\n}));\n\ntry {\n    console.log(\"x\".replace(withNull, \"$<name>\"));\n} catch (error) {\n    console.log(error instanceof TypeError);\n}" },
            "4 true\npresent\n3 true\nabsent\ntrue\n",
            false
        },
        new object[]
        {
            "symbol_overrides",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "RegExp.prototype[Symbol.match] = function (input: string): any {\n    console.log(\"match:\" + input + \":\" + (this instanceof RegExp));\n    return \"custom-match\";\n};\nRegExp.prototype[Symbol.replace] = function (\n    input: string, replacement: any): any {\n    console.log(\"replace:\" + input + \":\" + replacement + \":\" +\n        (this instanceof RegExp));\n    return \"custom-replace\";\n};\n\nconsole.log(\"aba\".match(/a/g));\nconsole.log(\"aba\".replace(/a/g, \"x\"));" },
            "match:aba:true\ncustom-match\nreplace:aba:x:true\ncustom-replace\n",
            false
        },
        new object[]
        {
            "replace_tokens",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(\"aba\".replace(/a/g, \"x\"));\nconsole.log(\"aba\".replace(/a/, \"x\"));\nconsole.log(\"abc\".replace(/(b)/, \"[$$][$&][$1][$`][$']\"));\nconsole.log(\"ab\".replace(/(?<letter>[a-z])/g, \"<$<letter>>\"));\nconsole.log(\"ab\".replace(/(?:)/g, \"-\"));" },
            "xbx\nxba\na[$][b][b][a][c]c\n<a><b>\n-a-b-\n",
            false
        },
        new object[]
        {
            "replace_callback",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const result: string = \"ab\".replace(\n    /(?<letter>[a-z])/g,\n    function(match: string, capture: string, index: number,\n        input: string, groups: any): string {\n        console.log(match, capture, index, input, groups.letter);\n        return capture.toUpperCase();\n    });\nconsole.log(result);" },
            "a a 0 ab a\nb b 1 ab b\nAB\n",
            false
        },
        new object[]
        {
            "replace_capture",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const topLevelReplacement: string = \"[$&]\";\nfunction replaceTopLevel(input: string): string {\n    return input.replace(/x/, topLevelReplacement);\n}\n\nfunction makeReplacer(replacement: string): any {\n    return function(input: string): string {\n        return input.replace(/x/, replacement);\n    };\n}\n\nconsole.log(replaceTopLevel(\"x\"));\nconst replaceCaptured: any = makeReplacer(\"<$&>\");\nconsole.log(replaceCaptured(\"x\"));" },
            "[x]\n<x>\n",
            false
        },
        new object[]
        {
            "replace_mutation",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "RegExp.prototype[Symbol.replace] = function(\n    input: string, replacement: any): string {\n    console.log(\"custom\", input, replacement);\n    return \"mutated\";\n};\nconsole.log(\"foo\".replace(/foo/g, \"bar\"));" },
            "custom foo bar\nmutated\n",
            false
        },
        new object[]
        {
            "test_overrides",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const r: any = /x/;\nr.exec = function(value: any): any {\n    console.log(\"own-exec\", value);\n    return { matched: true };\n};\nconsole.log(r.test(\"abc\"));\nr.test = function(value: any): boolean {\n    console.log(\"own-test\", value);\n    return false;\n};\nconsole.log(r.test(\"abc\"));" },
            "own-exec abc\ntrue\nown-test abc\nfalse\n",
            false
        },
        new object[]
        {
            "test_accessors",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const r: any = /x/;\nObject.defineProperty(r, \"exec\", {\n    configurable: true,\n    get: function(): any {\n        console.log(\"get-exec\");\n        return function(): any { return { matched: true }; };\n    }\n});\nconsole.log(r.test(\"abc\"));\n\nconst originalTest: any = Object.getOwnPropertyDescriptor(RegExp.prototype, \"test\");\ntry {\n    Object.defineProperty(RegExp.prototype, \"test\", {\n        configurable: true,\n        get: function(): any {\n            console.log(\"get-test\");\n            return function(): boolean { return false; };\n        }\n    });\n    console.log(/x/.test(\"abc\"));\n} finally {\n    Object.defineProperty(RegExp.prototype, \"test\", originalTest);\n}" },
            "get-exec\ntrue\nget-test\nfalse\n",
            false
        },
        new object[]
        {
            "sticky_readonly",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const globalRx: any = /a/g;\nconsole.log(globalRx.test(\"aa\"), globalRx.lastIndex);\nconsole.log(globalRx.test(\"aa\"), globalRx.lastIndex);\nconsole.log(globalRx.test(\"aa\"), globalRx.lastIndex);\n\nconst stickyRx: any = /a/y;\nstickyRx.lastIndex = 1;\nconsole.log(stickyRx.test(\"ba\"), stickyRx.lastIndex);\n\nconst locked: any = /z/g;\nObject.defineProperty(locked, \"lastIndex\", { writable: false });\ntry {\n    locked.test(\"x\");\n} catch (error) {\n    console.log(error instanceof TypeError);\n}" },
            "true 1\ntrue 2\nfalse 0\ntrue 2\ntrue\n",
            false
        },
        new object[]
        {
            "test_receiver",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let coercions: number = 0;\nconst argument: any = {\n    toString: function(): string {\n        coercions++;\n        throw new Error(\"coerced\");\n    }\n};\n\nfunction check(receiver: any): void {\n    try {\n        RegExp.prototype.test.call(receiver, argument);\n    } catch (error) {\n        console.log(error instanceof TypeError, coercions);\n    }\n}\n\ncheck(undefined);\ncheck(1n);" },
            "true 0\ntrue 0\n",
            false
        },
        new object[]
        {
            "hoist_stateful",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function g(): boolean { return /a/g.test(\"aaa\"); }\nconsole.log(g(), g(), g(), g());\nfunction y(): boolean { return /a/y.test(\"aaa\"); }\nconsole.log(y(), y(), y(), y());" },
            "true true true true\ntrue true true true\n",
            false
        },
        new object[]
        {
            "hoist_plain",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function count(n: number): number {\n    let c = 0;\n    for (let i = 0; i < n; i++) {\n        if (/^[a-z]+$/.test(\"abc\")) c++;\n    }\n    return c;\n}\nconsole.log(count(5));" },
            "5\n",
            false
        },
        new object[]
        {
            "hoist_escape",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function uses(): boolean { return /a/.test(\"ba\"); }\nconst r = /a/;\nr.lastIndex = 7;\nconsole.log(uses());\nconsole.log(r.lastIndex);" },
            "true\n7\n",
            false
        },
        new object[]
        {
            "literal_identity",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(/a/ === /a/);" },
            "false\n",
            false
        },
        new object[]
        {
            "named_exec",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let regex = /(?<year>\\d{4})-(?<month>\\d{2})-(?<day>\\d{2})/;\nlet match = regex.exec(\"2024-03-15\");\nconsole.log(match.groups.year);\nconsole.log(match.groups.month);\nconsole.log(match.groups.day);" },
            "2024\n03\n15\n",
            false
        },
        new object[]
        {
            "named_matchall",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let str = \"2024-03 and 2025-12\";\nlet matches = [...str.matchAll(/(?<year>\\d{4})-(?<month>\\d{2})/g)];\nconsole.log(matches.length);\nconsole.log(matches[0].groups.year);\nconsole.log(matches[0].groups.month);\nconsole.log(matches[1].groups.year);\nconsole.log(matches[1].groups.month);" },
            "2\n2024\n03\n2025\n12\n",
            false
        },
        new object[]
        {
            "mixed_captures",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let regex = /(\\d+)-(?<name>\\w+)/;\nlet match = regex.exec(\"42-hello\");\nconsole.log(match[0]);\nconsole.log(match[1]);\nconsole.log(match[2]);\nconsole.log(match.groups.name);" },
            "42-hello\n42\nhello\nhello\n",
            false
        },
        new object[]
        {
            "flags",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(/a/y.sticky);\nconsole.log(/a/s.dotAll);\nconsole.log(/a/d.hasIndices);\nconsole.log(/a/u.unicode);" },
            "true\ntrue\ntrue\ntrue\n",
            false
        },
        new object[]
        {
            "matchall",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const str = \"test1 test2 test3\";\nconst matches = Array.from(str.matchAll(/test\\d/g));\nconsole.log(matches.length);\nconst m0 = matches[0];\nconst m1 = matches[1];\nconst m2 = matches[2];\nconsole.log(m0[\"0\"]);\nconsole.log(m1[\"0\"]);\nconsole.log(m2[\"0\"]);" },
            "3\ntest1\ntest2\ntest3\n",
            false
        },
        new object[]
        {
            "constructors",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a = new RegExp('a', 'gi');\nconst b = new RegExp('b');\nconsole.log(a.source, a.flags, a.global, a.ignoreCase);\nconsole.log(b.source, b.flags === '', b.test('b'));\nconsole.log(a.toString());" },
            "a gi true true\nb true true\n/a/gi\n",
            false
        },
        new object[]
        {
            "clone",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const r = /a/gi; r.lastIndex = 2;\nconst c = structuredClone(r);\nconsole.log(c.source, c.flags, c.lastIndex, r.lastIndex, c === r);\nconsole.log(c.test('Aa'), c.lastIndex, r.lastIndex);" },
            "a gi 0 2 false\ntrue 1 2\n",
            false
        },
        new object[]
        {
            "consumers",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log('a1b2'.replaceAll(/\\d/g, '#'));\nconsole.log('x,y;z'.split(/[,;]/).join('-'));\nconsole.log('hello world'.search(/world/));" },
            "a#b#\nx-y-z\n6\n",
            false
        },
        new object[]
        {
            "esm",
            "main.ts",
            new string[] { "dep.ts", "main.ts" },
            new string[] { "export const pattern = /a/gi;", "import {pattern} from \"./dep\"; console.log(pattern.test(\"A\"), pattern.source, pattern.flags);" },
            "true a gi\n",
            false
        },
        new object[]
        {
            "commonjs",
            "main.cjs",
            new string[] { "dep.cjs", "main.cjs" },
            new string[] { "exports.pattern = /a/gi;", "const dep = require(\"./dep.cjs\"); console.log(dep.pattern.test(\"A\"), dep.pattern.source, dep.pattern.flags);" },
            "true a gi\n",
            false
        },
        new object[]
        {
            "hosted_regexp",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export function value(input: string) { return /^[a-z]+$/.test(input); }" },
            "",
            true
        },
        new object[]
        {
            "hosted_minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export const value = 1;" },
            "",
            true
        },
        new object[]
        {
            "prototype_descriptors",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const p: any = RegExp.prototype;\nfor (const key of ['source','flags','global','ignoreCase','multiline','sticky','unicode','dotAll','hasIndices','unicodeSets']) {\n const d: any = Object.getOwnPropertyDescriptor(p, key);\n console.log(key, typeof d.get, d.set === undefined, d.enumerable, d.configurable);\n}\nfor (const key of ['exec','test','toString']) {\n const d: any = Object.getOwnPropertyDescriptor(p, key);\n console.log(key, typeof d.value, d.writable, d.enumerable, d.configurable);\n}\nfor (const key of [Symbol.match, Symbol.matchAll, Symbol.replace, Symbol.search, Symbol.split]) {\n const d: any = Object.getOwnPropertyDescriptor(p, key);\n console.log(d.value.length, d.writable, d.enumerable, d.configurable);\n}" },
            "source function true false true\nflags function true false true\nglobal function true false true\nignoreCase function true false true\nmultiline function true false true\nsticky function true false true\nunicode function true false true\ndotAll function true false true\nhasIndices function true false true\nunicodeSets function true false true\nexec function true false true\ntest function true false true\ntoString function true false true\n1 true false true\n1 true false true\n2 true false true\n1 true false true\n2 true false true\n",
            false
        },
        new object[]
        {
            "search_override",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const r: any = /a/g; r.lastIndex = 2;\nlet calls = 0;\nr.exec = function(input: any): any { calls++; console.log(this.lastIndex, input); this.lastIndex = 9; return {index: 1}; };\nconsole.log(RegExp.prototype[Symbol.search].call(r, 'aba'), calls, r.lastIndex);" },
            "0 aba\n1 1 2\n",
            false
        },
        new object[]
        {
            "split_override",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const r: any = /a/;\nr[Symbol.split] = function(input: any, limit: any): any { console.log(this === r, input, limit); return ['custom']; };\nconsole.log('aba'.split(r, 2).join('|'));" },
            "true aba 2\ncustom\n",
            false
        },
        new object[]
        {
            "split_species_constructor",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function Splitter(pattern: any, flags: any): any {\n console.log(pattern.source, flags); return new RegExp('b', flags);\n}\nconst r: any = /a/; r.constructor = { [Symbol.species]: Splitter };\nconst parts: any = RegExp.prototype[Symbol.split].call(r, 'abc', 2);\nconsole.log(parts.join('|'), r.lastIndex);" },
            "a y\na|c 0\n",
            false
        },
        new object[]
        {
            "matchall_species_constructor",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function Matcher(pattern: any, flags: any): any {\n console.log(pattern.source, flags); return new RegExp('b', flags);\n}\nconst r: any = /a/g; r.lastIndex = 1; r.constructor = { [Symbol.species]: Matcher };\nconst iterator: any = RegExp.prototype[Symbol.matchAll].call(r, 'abb');\nconst a: any = iterator.next(); const b: any = iterator.next(); const end: any = iterator.next();\nconsole.log(a.value[0], a.value.index, b.value[0], b.value.index, end.done, r.lastIndex);" },
            "a g\nb 1 b 2 true 1\n",
            false
        },
    ];

    [Theory]
    [MemberData(nameof(RegExpMetadataPrograms))]
    public void Isolated_RegExpMetadata_PreservesCollectionsAndDeployment(
        string name, string entry, string[] paths, string[] sources, string expected, bool hosted)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        for (int i = 0; i < paths.Length; i++) tempDir.CreateFile(paths[i], sources[i]);
        var sourcePath = tempDir.GetPath(entry);
        var dllPath = tempDir.GetPath($"regexp-metadata_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }


    public static IEnumerable<object[]> DateMetadataPrograms =>
    [
        new object[]
        {
            "minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(1);" },
            "1\n",
            false,
            true
        },
        new object[]
        {
            "current",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let d = new Date();\nlet now = Date.now();\nlet diff = now - d.getTime();\nconsole.log(diff >= 0 && diff < 1000);" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "components",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let d = new Date(2024, 5, 20, 14, 30, 45, 123);\nconsole.log(d.getFullYear());\nconsole.log(d.getMonth());\nconsole.log(d.getDate());\nconsole.log(d.getHours());\nconsole.log(d.getMinutes());\nconsole.log(d.getSeconds());\nconsole.log(d.getMilliseconds());" },
            "2024\n5\n20\n14\n30\n45\n123\n",
            false,
            true
        },
        new object[]
        {
            "multi_setters",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let d = new Date(0);\nd.setUTCFullYear(2020, 5, 15);\nd.setUTCHours(13, 30, 45, 500);\nconsole.log(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate());\nconsole.log(d.getUTCHours(), d.getUTCMinutes(), d.getUTCSeconds(), d.getUTCMilliseconds());\nlet m = new Date(2024, 0, 1, 10, 20, 30, 40);\nm.setHours(8, 15, 5);\nconsole.log(m.getHours(), m.getMinutes(), m.getSeconds(), m.getMilliseconds());" },
            "2020 5 15\n13 30 45 500\n8 15 5 40\n",
            false,
            true
        },
        new object[]
        {
            "overflow",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let d = new Date(0);\nd.setUTCFullYear(2020, 1, 31);\nconsole.log(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate());" },
            "2020 2 2\n",
            false,
            true
        },
        new object[]
        {
            "utc",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(Date.UTC(2024, 0, 1));\nconsole.log(Date.UTC(2024, 5, 15, 13, 30, 45, 500));\nconsole.log(Date.UTC(2024));\nconsole.log(Date.UTC(70, 0, 1));\nconsole.log(Number.isNaN(Date.UTC(2024, NaN)));\nconsole.log(new Date(Date.UTC(2000, 0, 1)).toISOString());" },
            "1704067200000\n1718458245500\n1704067200000\n0\ntrue\n2000-01-01T00:00:00.000Z\n",
            false,
            true
        },
        new object[]
        {
            "parse",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(Date.parse('2024-01-15T10:30:00Z'));\nconsole.log(Number.isNaN(Date.parse('not a date')));\nconsole.log(Date.parse('2024-01-15T10:30:00Z') === new Date('2024-01-15T10:30:00Z').getTime());" },
            "1705314600000\ntrue\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "static_values",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const u = Date.UTC;\nconst p = Date.parse;\nconsole.log(u(2024, 0, 1));\nconsole.log(p('2024-01-15T10:30:00Z'));" },
            "1704067200000\n1705314600000\n",
            false,
            true
        },
        new object[]
        {
            "copy",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const original: any = new Date(1438560000000);\noriginal.valueOf = () => { throw new Error(\"unexpected coercion\"); };\noriginal.toString = () => { throw new Error(\"unexpected coercion\"); };\nconsole.log(new Date(original).getTime());\nconsole.log(typeof new Date(8640000000000000).getTimezoneOffset());\nconsole.log(typeof new Date(-8640000000000000).getTimezoneOffset());" },
            "1438560000000\nnumber\nnumber\n",
            false,
            true
        },
        new object[]
        {
            "prototype_mutation",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const date: any = new Date(0);\nconsole.log(/^[0-9]{2}:[0-9]{2}:[0-9]{2} GMT[+-][0-9]{4}$/.test(date.toTimeString()));\nconsole.log(/ GMT[+-][0-9]{4}$/.test(date.toString()));\nDate.prototype.toString = Object.prototype.toString;\nconsole.log(date.toString());" },
            "true\ntrue\n[object Date]\n",
            false,
            true
        },
        new object[]
        {
            "nonconstructors",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function isConstructor(value: any) {\n    try {\n        Reflect.construct(function() {}, [], value);\n        return true;\n    } catch {\n        return false;\n    }\n}\nconsole.log(isConstructor(Date.now));\nconsole.log(isConstructor(Date.parse));\nconsole.log(isConstructor(Date.UTC));" },
            "false\nfalse\nfalse\n",
            false,
            true
        },
        new object[]
        {
            "locale_options",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let d = new Date(Date.UTC(2024, 0, 15, 12, 0, 0));\nlet enFull = d.toLocaleDateString('en-US', { dateStyle: 'full', timeZone: 'UTC' });\nlet deFull = d.toLocaleDateString('de-DE', { dateStyle: 'full', timeZone: 'UTC' });\nlet enShort = d.toLocaleDateString('en-US', { dateStyle: 'short', timeZone: 'UTC' });\nlet enTime = d.toLocaleTimeString('en-US', { timeStyle: 'medium', timeZone: 'UTC' });\nconsole.log(enFull.includes('Monday') && enFull.includes('January') && enFull.includes('2024'));\nconsole.log(deFull.includes('Montag') && deFull.includes('Januar'));\nconsole.log(deFull !== enFull);\nconsole.log(enShort !== enFull && !enShort.includes('Monday'));\nconsole.log(enTime.includes('12:00:00'));" },
            "true\ntrue\ntrue\ntrue\ntrue\n",
            false,
            false
        },
        new object[]
        {
            "descriptors",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const date = new Date(0);\nconst descriptor = Object.getOwnPropertyDescriptor(Date.prototype, 'toJSON')!;\nconsole.log(Object.prototype.hasOwnProperty.call(Date.prototype, 'toJSON'));\nconsole.log(descriptor.writable, descriptor.enumerable, descriptor.configurable);\nconsole.log(Object.getOwnPropertyDescriptor(date, 'toJSON') === undefined);" },
            "true\ntrue false true\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "locale_noargs",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let d = new Date(0);\nconsole.log(typeof d.toLocaleDateString(), d.toLocaleDateString().length > 0);\nconsole.log(typeof d.toLocaleTimeString(), d.toLocaleTimeString().length > 0);\nconsole.log(typeof d.toLocaleString(), d.toLocaleString().length > 0);" },
            "string true\nstring true\nstring true\n",
            false,
            true
        },
        new object[]
        {
            "invalid",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let d = new Date(NaN);\nconsole.log(d.toUTCString());\nconsole.log(d.toLocaleDateString());\nconsole.log(Number.isNaN(d.getUTCFullYear()));\nconsole.log(Number.isNaN(d.setUTCSeconds(30)));" },
            "Invalid Date\nInvalid Date\ntrue\ntrue\n",
            false,
            true
        },
        new object[]
        {
            "json",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let d = new Date('2024-06-15T12:00:00Z');\nconsole.log(d.toJSON() === d.toISOString());" },
            "true\n",
            false,
            true
        },
        new object[]
        {
            "invalid_json",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let d = new Date(NaN);\nconsole.log(d.toJSON());" },
            "null\n",
            false,
            true
        },
        new object[]
        {
            "timer_reentrancy",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let calls = 0;\nsetTimeout(() => {\n    calls++;\n    console.log(Date.now() >= 0);\n}, 0);\n\nconst started = Date.now();\nwhile (calls === 0 && Date.now() - started < 5000) { }\nconsole.log(calls);" },
            "true\n1\n",
            false,
            true
        },
        new object[]
        {
            "clone",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a=new Date(0);const b=structuredClone(a);b.setTime(1000);console.log(a.getTime(),b.getTime(),a!==b);" },
            "0 1000 true\n",
            false,
            true
        },
        new object[]
        {
            "modules",
            "main.ts",
            new string[] { "dep.ts", "main.ts" },
            new string[] { "export const epoch=new Date(0);", "import {epoch} from \"./dep\";console.log(epoch.toISOString());" },
            "1970-01-01T00:00:00.000Z\n",
            false,
            true
        },
        new object[]
        {
            "commonjs",
            "main.cjs",
            new string[] { "dep.cjs", "main.cjs" },
            new string[] { "exports.epoch=new Date(0);", "const dep=require(\"./dep.cjs\");console.log(dep.epoch.toISOString());" },
            "1970-01-01T00:00:00.000Z\n",
            false,
            true
        },
        new object[]
        {
            "hosted_date",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export function value(){return new Date(0).toISOString();}" },
            "",
            true,
            true
        },
        new object[]
        {
            "hosted_minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export const value=1;" },
            "",
            true,
            true
        },
    ];

    [Theory]
    [MemberData(nameof(DateMetadataPrograms))]
    public void Isolated_DateMetadata_PreservesCollectionsAndDeployment(
        string name, string entry, string[] paths, string[] sources, string expected, bool hosted, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        for (int i = 0; i < paths.Length; i++) tempDir.CreateFile(paths[i], sources[i]);
        var sourcePath = tempDir.GetPath(entry);
        var dllPath = tempDir.GetPath($"date-metadata_{name}.dll");
        var deployment = standalone ? " --standalone" : "";
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{dllPath}\" --verify{deployment}{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }


    public static IEnumerable<object[]> SymbolMetadataPrograms =>
    [
        new object[]
        {
            "minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(1);" },
            "1\n",
            false
        },
        new object[]
        {
            "unique",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let s1 = Symbol(\"test\");\nlet s2 = Symbol(\"test\");\nconsole.log(s1 === s2);\nconsole.log(s1 !== s2);" },
            "false\ntrue\n",
            false
        },
        new object[]
        {
            "object_keys",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "let sym1 = Symbol(\"first\");\nlet sym2 = Symbol(\"second\");\nlet obj: { [key: symbol]: number } = {};\nobj[sym1] = 10;\nobj[sym2] = 20;\nconsole.log(obj[sym1]);\nconsole.log(obj[sym2]);" },
            "10\n20\n",
            false
        },
        new object[]
        {
            "registry",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const f: any = Symbol;\nconst shared = f.for(\"registry-key\");\nconsole.log(shared === Symbol.for(\"registry-key\"));\nconsole.log(f.keyFor(shared));" },
            "true\nregistry-key\n",
            false
        },
        new object[]
        {
            "prototype",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const s: any = Symbol(\"dyn\");\nconsole.log(s.description);\nconsole.log(s.toString());\nconsole.log(s.valueOf() === s);" },
            "dyn\nSymbol(dyn)\ntrue\n",
            false
        },
        new object[]
        {
            "string_call",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(String(Symbol(\"d\")));\nconsole.log(String(Symbol()));" },
            "Symbol(d)\nSymbol()\n",
            false
        },
        new object[]
        {
            "coercion_error",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const s = Symbol(\"d\");\ntry {\n    const t = `value: ${s}`;\n    console.log(\"no throw\", t);\n} catch (e) {\n    console.log(e instanceof TypeError, e.message);\n}" },
            "true Cannot convert a Symbol value to a string\n",
            false
        },
        new object[]
        {
            "generic_iterator",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Box<T> {\n  constructor(private items: T[]) {}\n  *[Symbol.iterator](): Iterator<T> { for (const x of this.items) yield x; }\n}\nfor (const n of new Box<number>([1, 2, 3])) console.log(n);" },
            "1\n2\n3\n",
            false
        },
        new object[]
        {
            "inherited_method",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Base { [\"inh\"]() { return 99; } }\nclass Derived extends Base {}\nconsole.log((new Derived() as any).inh());" },
            "99\n",
            false
        },
        new object[]
        {
            "async_iterator",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class ARange { async *[Symbol.asyncIterator]() { yield 10; yield 20; } }\nasync function main() {\n  for await (const x of new ARange()) console.log(x);\n}\nmain();" },
            "10\n20\n",
            false
        },
        new object[]
        {
            "bound_method",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class R { *[Symbol.iterator]() { yield 5; } }\nconst it = (new R() as any)[Symbol.iterator]();\nconsole.log(it.next().value);" },
            "5\n",
            false
        },
        new object[]
        {
            "numeric_key",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class C { [1]() { return 7; } }\nconst c = new C() as any;\nconsole.log(c[1]());\nconsole.log(c[\"1\"]());" },
            "7\n7\n",
            false
        },
        new object[]
        {
            "instance_accessor",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Tagged {\n    stored: any = null;\n    get [Symbol.toStringTag]() { return \"Tagged!\"; }\n    set [Symbol.toPrimitive](v: any) { this.stored = v; }\n}\nconst t = new Tagged();\nconsole.log((t as any)[Symbol.toStringTag]);\n(t as any)[Symbol.toPrimitive] = 42;\nconsole.log(t.stored);" },
            "Tagged!\n42\n",
            false
        },
        new object[]
        {
            "inherited_static_accessor",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Base {\n    static get [Symbol.species]() { return Base; }\n}\nclass Sub extends Base {}\nconsole.log((Sub as any)[Symbol.species] === Base);" },
            "true\n",
            false
        },
        new object[]
        {
            "class_expression",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const mk = Symbol(\"mk\");\nconst C = class {\n    _v: number = 5;\n    get [Symbol.toStringTag]() { return \"tag\" + this._v; }\n    get [mk]() { return this._v; }\n    set [mk](x: number) { this._v = x; }\n};\nconst c = new C() as any;\nconsole.log(c[Symbol.toStringTag], c[mk]);\nc[mk] = 99;\nconsole.log(c[Symbol.toStringTag], c[mk]);" },
            "tag5 5\ntag99 99\n",
            false
        },
        new object[]
        {
            "modules",
            "main.ts",
            new string[] { "key.ts", "main.ts" },
            new string[] { "export const key=Symbol.for(\"module\"); export class Box { [key]() { return 7; } }", "import {key,Box} from \"./key\"; console.log(Symbol.keyFor(key),(new Box() as any)[key]());" },
            "module 7\n",
            false
        },
        new object[]
        {
            "commonjs",
            "main.cjs",
            new string[] { "dep.cjs", "main.cjs" },
            new string[] { "const key=Symbol.for(\"common\");module.exports={key,value:{[key]:9}};", "const dep=require(\"./dep.cjs\");console.log(Symbol.keyFor(dep.key),dep.value[dep.key]);" },
            "common 9\n",
            false
        },
        new object[]
        {
            "hosted_symbols",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export function value() { const key=Symbol.for(\"hosted\"); return Symbol.keyFor(key); }" },
            "",
            true
        },
        new object[]
        {
            "hosted_minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "export const value=1;" },
            "",
            true
        },
    ];

    [Theory]
    [MemberData(nameof(SymbolMetadataPrograms))]
    public void Isolated_SymbolMetadata_PreservesCollectionsAndDeployment(
        string name, string entry, string[] paths, string[] sources, string expected, bool hosted)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        for (int i = 0; i < paths.Length; i++) tempDir.CreateFile(paths[i], sources[i]);
        var sourcePath = tempDir.GetPath(entry);
        var dllPath = tempDir.GetPath($"symbol-metadata_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }


    public static IEnumerable<object[]> WeakMetadataPrograms =>
    [
        new object[]
        {
            "minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(1);" },
            "1\n",
            false
        },
        new object[]
        {
            "weak_map",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a={id:1},b={id:1};const map=new WeakMap<object,number>();console.log(map.set(a,7)===map,map.has(a),map.has(b),map.get(a));map.set(a,9);console.log(map.get(a),map.get(b)==null,map.delete(a),map.delete(a));console.log(a.id,b.id);" },
            "true true false 7\n9 true true false\n1 1\n",
            false
        },
        new object[]
        {
            "weak_set",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a={id:1},b={id:1};const set=new WeakSet<object>();console.log(set.add(a)===set,set.has(a),set.has(b));console.log(set.delete(a),set.delete(a),set.has(a));console.log(a.id,b.id);" },
            "true true false\ntrue false false\n1 1\n",
            false
        },
        new object[]
        {
            "weak_ref_class",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "class Value{constructor(public name:string){}}const value=new Value('kept');const a=new WeakRef(value),b=new WeakRef(value);console.log(a.deref()===value,b.deref()===value,a.deref()!.name);console.log(value.name);" },
            "true true kept\nkept\n",
            false
        },
        new object[]
        {
            "finalization_tokens",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const registry=new FinalizationRegistry((held:any)=>{});const target={id:1},token={id:2},other={id:3};registry.register(target,'held',token);console.log(registry.unregister(other),registry.unregister(token),registry.unregister(token));console.log(target.id,token.id);" },
            "false true false\n1 2\n",
            false
        },
        new object[]
        {
            "independent_registries",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a=new FinalizationRegistry((held:any)=>{}),b=new FinalizationRegistry((held:any)=>{});const x={id:1},y={id:2},token={id:3};a.register(x,'x',token);b.register(y,'y',token);console.log(a.unregister(token),a.unregister(token),b.unregister(token));console.log(x.id,y.id,token.id);" },
            "true false true\n1 2 3\n",
            false
        },
        new object[]
        {
            "all_families",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const target={id:1};const map=new WeakMap<object,number>();const set=new WeakSet<object>();const ref=new WeakRef(target);const registry=new FinalizationRegistry((held:any)=>{});map.set(target,7);set.add(target);registry.register(target,'held',target);console.log(map.get(target),set.has(target),ref.deref()===target,registry.unregister(target));console.log(target.id);" },
            "7 true true true\n1\n",
            false
        },
        new object[]
        {
            "invalid_primitives",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const map=new WeakMap<any,number>(),set=new WeakSet<any>();let errors=0;try{map.set(1,2);}catch(e){errors++;}try{set.add(1);}catch(e){errors++;}try{new WeakRef(1 as any);}catch(e){errors++;}console.log(errors);" },
            "3\n",
            false
        },
        new object[]
        {
            "dynamic_properties",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const key:any={id:1};const map:any=new WeakMap<object,number>();const set:any=new WeakSet<object>();const put=map.set,get=map.get,add=set.add,has=set.has;put(key,7);add(key);console.log(typeof get,typeof has,get(key),has(key));console.log(key.id);" },
            "function function 7 true\n1\n",
            false
        },
        new object[]
        {
            "async_weak",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "async function run(){const target={id:1};const map=new WeakMap<object,number>();const ref=new WeakRef(target);map.set(target,await Promise.resolve(8));console.log(map.get(target),ref.deref()===target,target.id);}run();" },
            "8 true 1\n",
            false
        },
        new object[]
        {
            "generator_weak",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function* run():Generator<number,void,any>{const target={id:1};const map=new WeakMap<object,number>();map.set(target,8);yield map.get(target)!;const ref=new WeakRef(target);yield ref.deref()!.id;console.log(target.id);}for(const value of run()){console.log(value);}" },
            "8\n1\n1\n",
            false
        },
        new object[]
        {
            "modules",
            "main.ts",
            new string[] { "main.ts", "values.ts" },
            new string[] { "import {make} from './values';const target={id:1};const pair=make(target);console.log(pair.map.get(target),pair.set.has(target),target.id);", "export function make(target:object){const map=new WeakMap<object,number>();const set=new WeakSet<object>();map.set(target,7);set.add(target);return {map:map,set:set};}" },
            "7 true 1\n",
            false
        },
        new object[]
        {
            "commonjs",
            "main.cjs",
            new string[] { "main.cjs", "values.cjs" },
            new string[] { "const values=require('./values.cjs');const target={id:1};console.log(values.lookup(target),target.id);", "exports.lookup=function(target){const map=new WeakMap();map.set(target,5);return map.get(target);};" },
            "5 1\n",
            false
        },
        new object[]
        {
            "hosted_both",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const target={id:1};const map=new WeakMap<object,number>();const set=new WeakSet<object>();const ref=new WeakRef(target);const registry=new FinalizationRegistry((held:any)=>{});map.set(target,7);set.add(target);registry.register(target,'held',target);console.log(map.get(target),set.has(target),ref.deref()===target,registry.unregister(target));console.log(target.id);" },
            "",
            true
        },
        new object[]
        {
            "hosted_minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(1);" },
            "",
            true
        },
    ];

    [Theory]
    [MemberData(nameof(WeakMetadataPrograms))]
    public void Isolated_WeakMetadata_PreservesCollectionsAndDeployment(
        string name, string entry, string[] paths, string[] sources, string expected, bool hosted)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        for (int i = 0; i < paths.Length; i++) tempDir.CreateFile(paths[i], sources[i]);
        var sourcePath = tempDir.GetPath(entry);
        var dllPath = tempDir.GetPath($"weak-metadata_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }


    public static IEnumerable<object[]> MapSetMetadataPrograms =>
    [
        new object[]
        {
            "minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(1);" },
            "1\n",
            false
        },
        new object[]
        {
            "map_operations",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const m = new Map<string,number>([['a',1],['b',2]]); console.log(m.size,m.get('a'),m.get('missing'),m.has('b')); console.log(m.set('a',3)===m,m.delete('b'),m.delete('b')); console.log(m.size,m.get('a')); m.clear(); console.log(m.size);" },
            "2 1 undefined true\ntrue true false\n1 3\n0\n",
            false
        },
        new object[]
        {
            "map_key_identity",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a:any={},b:any={}; const s=Symbol('x'),t=Symbol('x'); const m=new Map<any,any>(); m.set(null,'null').set(undefined,'undefined').set(NaN,'nan').set(-0,'zero').set(a,'a').set(b,'b').set(s,'s').set(t,'t').set(1n,'big'); console.log(m.size,m.get(null),m.get(undefined),m.get(NaN),m.get(0),m.get(a),m.get(b),m.get(s),m.get(t),m.get(1n));" },
            "9 null undefined nan zero a b s t big\n",
            false
        },
        new object[]
        {
            "map_iterators",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const m=new Map<string,number>([['a',1],['b',2],['c',3]]); const keys=m.keys(),values=m.values(),entries=m.entries(); m.delete('b');m.set('a',9); for(const k of keys)console.log('k',k);for(const v of values)console.log('v',v);for(const p of entries)console.log('e',p[0],p[1]);" },
            "k a\nk c\nv 9\nv 3\ne a 9\ne c 3\n",
            false
        },
        new object[]
        {
            "map_bound_callbacks",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const m=new Map<string,number>([['a',1],['b',2]]);const get:any=m.get,set:any=m.set,has:any=m.has; console.log(set('c',3)===m,get('c'),has('c'));let total=0;const callback:any=(v:number,k:string,self:any)=>{total+=v;console.log(k,self===m);};m.forEach(callback);console.log(total);" },
            "true 3 true\na true\nb true\nc true\n6\n",
            false
        },
        new object[]
        {
            "map_group_by",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const values=[1,2,3,4,5];const groups=Map.groupBy(values,(x:number)=>x%2);console.log(groups.size,JSON.stringify(groups.get(0)),JSON.stringify(groups.get(1)));const key:any={};const refs=Map.groupBy(values,()=>key);console.log(refs.size,refs.get(key).length);" },
            "2 [2,4] [1,3,5]\n1 5\n",
            false
        },
        new object[]
        {
            "set_operations",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const s=new Set<number>([1,2,2,3]);console.log(s.size,s.has(2),s.has(4),s.add(4)===s);console.log(s.delete(2),s.delete(2));for(const v of s)console.log(v);s.clear();console.log(s.size);" },
            "3 true false true\ntrue false\n1\n3\n4\n0\n",
            false
        },
        new object[]
        {
            "set_key_identity",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a:any={},b:any={},s=Symbol('x'),t=Symbol('x');const values=new Set<any>([NaN,NaN,-0,0,a,a,b,s,s,t,1n,1n]);console.log(values.size,values.has(NaN),values.has(0),values.has(a),values.has(b),values.has(s),values.has(t),values.has(1n));" },
            "7 true true true true true true true\n",
            false
        },
        new object[]
        {
            "set_algebra",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const a=new Set<number>([1,2,3]),b=new Set<number>([3,4]);console.log(a.union(b).size,a.intersection(b).size,a.difference(b).size,a.symmetricDifference(b).size);console.log(a.isSubsetOf(a.union(b)),a.isSupersetOf(new Set<number>([2])),a.isDisjointFrom(new Set<number>([5])));console.log(a.size,b.size);" },
            "4 1 2 3\ntrue true true\n3 2\n",
            false
        },
        new object[]
        {
            "set_bound_callbacks",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const s=new Set<string>(['a','b']);const add:any=s.add,has:any=s.has;console.log(add('c')===s,has('c'));const callback:any=(a:string,b:string,self:any)=>console.log(a,b,self===s);s.forEach(callback);for(const p of s.entries())console.log(p[0],p[1]);" },
            "true true\na a true\nb b true\nc c true\na a\nb b\nc c\n",
            false
        },
        new object[]
        {
            "numeric_array_constructor",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const numbers:number[]=[1,2,2,3];for(let i=0;i<10;i++)numbers[0]=i;const set=new Set<number>(numbers);console.log(numbers[0],set.size,set.has(9),set.has(2),set.has(3));" },
            "9 3 true true true\n",
            false
        },
        new object[]
        {
            "generic_iterator",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "function* entries():Generator<[string,number],void,any>{yield ['a',1];yield ['b',2];}const m=new Map<string,number>();for(const p of entries())m.set(p[0],p[1]);function* values():Generator<number,void,any>{yield 1;yield 2;yield 3;}const groups=Map.groupBy(values(),(n:number)=>n%2);console.log(m.size,m.get('b'),groups.size,JSON.stringify(groups.get(1)));" },
            "2 2 2 [1,3]\n",
            false
        },
        new object[]
        {
            "async_collections",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "async function run(){const m=new Map<string,number>([['a',1]]);const s=new Set<number>([1,2]);await Promise.resolve(0);m.set('b',s.size);console.log(m.size,m.get('b'),s.has(2));}run();" },
            "2 2 true\n",
            false
        },
        new object[]
        {
            "modules",
            "main.ts",
            new string[] { "main.ts", "values.ts" },
            new string[] { "import {map,set} from './values';console.log(map.get('a'),set.size);map.set('b',2);set.add(3);console.log(map.size,set.size);", "export const map=new Map<string,number>([['a',1]]);export const set=new Set<number>([1,2]);" },
            "1 2\n2 3\n",
            false
        },
        new object[]
        {
            "commonjs",
            "main.cjs",
            new string[] { "main.cjs" },
            new string[] { "const m=new Map([['a',1]]);const s=new Set([1,2]);console.log(m.get('a'),s.size);" },
            "1 2\n",
            false
        },
        new object[]
        {
            "hosted_both",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "const map=new Map<string,number>([['a',1]]);const set=new Set<number>([1,2]);console.log(map.size,set.size);" },
            "",
            true
        },
        new object[]
        {
            "hosted_minimal",
            "main.ts",
            new string[] { "main.ts" },
            new string[] { "console.log(1);" },
            "",
            true
        },
    ];

    [Theory]
    [MemberData(nameof(MapSetMetadataPrograms))]
    public void Isolated_MapSetMetadata_PreservesCollectionsAndDeployment(
        string name, string entry, string[] paths, string[] sources, string expected, bool hosted)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        for (int i = 0; i < paths.Length; i++) tempDir.CreateFile(paths[i], sources[i]);
        var sourcePath = tempDir.GetPath(entry);
        var dllPath = tempDir.GetPath($"map-set-metadata_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> JsonMetadataPrograms =>
    [
        new object[]
        {
            "parse_reviver",
            "let result: any = JSON.parse('{\"a\":1,\"b\":2}', (key: any, value: any): any => {\n    if (typeof value === \"number\") {\n        return value * 2;\n    }\n    return value;\n});\nconsole.log(result.a);\nconsole.log(result.b);",
            "2\n4\n",
            true,
            ""
        },
        new object[]
        {
            "shape_mutations",
            "const hidden: { a: number; b: number } = { a: 1, b: 2 };\nObject.defineProperty(hidden, \"a\", {\n    value: 9, enumerable: false, configurable: true\n});\nconsole.log(JSON.stringify(hidden));\n\nconst inherited: { a: number } = { a: 4 };\nObject.setPrototypeOf(inherited, {\n    toJSON: function (): any { return { hooked: this.a + 1 }; }\n});\nconsole.log(JSON.stringify(inherited));\n\nconst extended: { a: number } = { a: 1 };\n(extended as any).z = 3;\nconsole.log(JSON.stringify(extended));\n\nconst reordered: { a: number; b: number } = { a: 1, b: 2 };\nconst dynamic: any = reordered;\ndelete dynamic.a;\ndynamic.a = 7;\nconsole.log(JSON.stringify(reordered));\n\nconst ownHook: { a: number } = { a: 6 };\n(ownHook as any).toJSON = function (): any {\n    return { custom: this.a };\n};\nconsole.log(JSON.stringify(ownHook));",
            "{\"b\":2}\n{\"hooked\":5}\n{\"a\":1,\"z\":3}\n{\"b\":2,\"a\":7}\n{\"custom\":6}\n",
            true,
            ""
        },
        new object[]
        {
            "shaped_roundtrip",
            "const payload: {\n    items: { id: number; label: string; active: boolean; note: null }[]\n} = {\n    items: [\n        { id: 1, label: \"a\", active: true, note: null },\n        { id: 2, label: \"b\", active: false, note: null }\n    ]\n};\nconst json: string = JSON.stringify(payload);\nconst shaped: any = JSON.parse(json);\nconst copied: string = (\" \" + json).slice(1);\nconst generic: any = JSON.parse(copied);\nconsole.log(shaped.items[0].id, shaped.items[1].label,\n    shaped.items[0].note === null);\nconsole.log(generic.items[0].id, generic.items[1].label,\n    generic.items[1].note === null);\nshaped.items[0].id = 9;\nObject.defineProperty(shaped.items[1], \"extra\", {\n    value: 3, enumerable: true, configurable: true\n});\nconsole.log(JSON.stringify(shaped));",
            "1 b true\n1 b true\n{\"items\":[{\"id\":9,\"label\":\"a\",\"active\":true,\"note\":null},{\"id\":2,\"label\":\"b\",\"active\":false,\"note\":null,\"extra\":3}]}\n",
            true,
            ""
        },
        new object[]
        {
            "compact_semantics",
            "const record: { a: number; b: string; c: boolean; d: null } = {\n    a: 1, b: \"x\", c: true, d: null\n};\nconsole.log(record.a, record.b, record.c, record.d === null);\nrecord.a = 8;\nconst dynamic: any = record;\ndelete dynamic.b;\nObject.defineProperty(dynamic, \"e\", {\n    value: 5, enumerable: true, configurable: true\n});\nconsole.log(Object.keys(record).join(\",\"));\nconsole.log(JSON.stringify(record));",
            "1 x true true\na,c,d,e\n{\"a\":8,\"c\":true,\"d\":null,\"e\":5}\n",
            true,
            ""
        },
        new object[]
        {
            "snapshot_tojson",
            "const obj: any = {};\nobj.a = {\n    toJSON: function (): number {\n        delete obj.b;\n        obj.c = 3;\n        return 1;\n    }\n};\nobj.b = 2;\nconsole.log(JSON.stringify(obj));",
            "{\"a\":1}\n",
            true,
            ""
        },
        new object[]
        {
            "snapshot_getter",
            "const obj: any = {};\nObject.defineProperty(obj, \"a\", {\n    enumerable: true,\n    configurable: true,\n    get: function (): number {\n        delete obj.b;\n        obj.c = 3;\n        return 1;\n    }\n});\nobj.b = 2;\nconsole.log(JSON.stringify(obj));",
            "{\"a\":1}\n",
            true,
            ""
        },
        new object[]
        {
            "escaping",
            "const key: string = \"a\\\"\\\\\\n\";\nconst value: string = \"x\\t\" + String.fromCharCode(0xd800);\nconst obj: any = {};\nobj[key] = value;\nconsole.log(JSON.stringify(obj));",
            "{\"a\\\"\\\\\\n\":\"x\\t\\ud800\"}\n",
            true,
            ""
        },
        new object[]
        {
            "parse_names",
            "const parsed: any[] = JSON.parse(\n    '[{\"id\":1,\"label\":\"a\"},{\"\\\\u0069d\":2,\"label\":\"b\"},{\"id\":3,\"id\":4}]');\nconsole.log(parsed[0].id, parsed[1].id, parsed[2].id);\nconsole.log(Object.keys(parsed[1]).join(\",\"));\nconsole.log(Object.keys(parsed[2]).join(\",\"));",
            "1 2 4\nid,label\nid\n",
            true,
            ""
        },
        new object[]
        {
            "namespace",
            "const json: any = JSON;\nconst parse = json.parse;\nconst descriptor = Object.getOwnPropertyDescriptor(json, \"parse\")!;\nconsole.log(Object.hasOwn(json, \"parse\"), parse.length);\nconsole.log(descriptor.writable, descriptor.enumerable, descriptor.configurable);\nconsole.log(delete json.parse, json.parse === undefined);\nObject.defineProperty(json, \"parse\", {\n    value: parse,\n    writable: true,\n    enumerable: false,\n    configurable: true\n});\nconsole.log(json.parse(\"1\"), json.stringify.length);",
            "true 2\ntrue false true\ntrue true\n1 3\n",
            true,
            ""
        },
        new object[]
        {
            "tojson_keys",
            "let obj: any = {\n    a: { toJSON: function(k: string): string { return \"k=\" + k; } },\n    b: [\n        { toJSON: function(k: string): string { return \"i=\" + k; } },\n        { toJSON: function(k: string): string { return \"i=\" + k; } }\n    ]\n};\nconsole.log(JSON.stringify(obj));",
            "{\"a\":\"k=a\",\"b\":[\"i=0\",\"i=1\"]}\n",
            true,
            ""
        },
        new object[]
        {
            "replacer_keys",
            "let obj: any = { x: 1, y: [10, 20] };\nlet keys: string[] = [];\nlet result: string = JSON.stringify(obj, function(k: string, v: any): any {\n    keys.push(k);\n    return v;\n});\nconsole.log(keys.join(\"|\"));\nconsole.log(result);",
            "|x|y|0|1\n{\"x\":1,\"y\":[10,20]}\n",
            true,
            ""
        },
        new object[]
        {
            "bigint",
            "try {\n    let result: string = JSON.stringify(123n);\n    console.log(\"should not reach here\");\n} catch (e) {\n    console.log(\"caught error\");\n}",
            "caught error\n",
            true,
            ""
        },
        new object[]
        {
            "indent",
            "let obj: { a: number } = { a: 1 };\nlet result: string = JSON.stringify(obj, null, \"\\t\");\nconsole.log(result);",
            "{\n\t\"a\": 1\n}\n",
            true,
            ""
        },
        new object[]
        {
            "nested_class",
            "class Inner {\n    value: number;\n    constructor(v: number) {\n        this.value = v;\n    }\n}\nclass Outer {\n    inner: Inner;\n    constructor(i: Inner) {\n        this.inner = i;\n    }\n}\nlet o: Outer = new Outer(new Inner(42));\nlet result: string = JSON.stringify(o);\nconsole.log(result);",
            "{\"inner\":{\"value\":42}}\n",
            true,
            ""
        },
        new object[]
        {
            "proxy_get",
            "let target: any = { a: 1, b: 2 };\nlet proxy: any = new Proxy(target, {\n    get: function(t: any, p: string): any { return t[p] * 10; }\n});\nconsole.log(JSON.stringify(proxy));",
            "{\"a\":10,\"b\":20}\n",
            false,
            ""
        },
        new object[]
        {
            "proxy_reviver",
            "let trapLog: string = \"\";\nJSON.parse('{\"a\":1,\"replaceMe\":2}', function (this: any, k: string, v: any): any {\n    if (k === \"a\") {\n        let target: any = { x: 100, y: 200 };\n        let proxy: any = new Proxy(target, {\n            get: function(t: any, p: string): any {\n                trapLog += \"get:\" + p + \";\";\n                return t[p];\n            },\n            ownKeys: function(t: any): string[] {\n                trapLog += \"ownKeys;\";\n                return Object.keys(t);\n            },\n            defineProperty: function(t: any, p: string, desc: any): boolean {\n                trapLog += \"define:\" + p + \"=\" + desc.value + \";\";\n                Object.defineProperty(t, p, desc);\n                return true;\n            }\n        });\n        this[\"replaceMe\"] = proxy;\n    }\n    return v;\n});\nconsole.log(trapLog.includes(\"ownKeys;\"));\nconsole.log(trapLog.includes(\"get:x;\"));\nconsole.log(trapLog.includes(\"get:y;\"));\nconsole.log(trapLog.includes(\"define:x=100;\"));\nconsole.log(trapLog.includes(\"define:y=200;\"));",
            "true\ntrue\ntrue\ntrue\ntrue\n",
            false,
            ""
        },
        new object[]
        {
            "typed_records",
            "type Item = { id: number; name: string; value: number };\ntype Payload = { items: Item[] };\n\nfunction roundTrip(n: number): number {\n    const items: Item[] = [];\n    for (let i: number = 0; i < n; i++) {\n        items.push({ id: i, name: \"item-\" + i, value: i * 3 - 1 });\n    }\n    const payload: Payload = { items: items };\n    const json: string = JSON.stringify(payload);\n    const parsed: any = JSON.parse(json);\n    const back: Item[] = parsed.items;\n    let sum: number = 0;\n    for (let i: number = 0; i < back.length; i++) {\n        sum = sum + back[i].value;\n    }\n    return sum;\n}\n\nconsole.log(roundTrip(4));",
            "14\n",
            true,
            ""
        },
        new object[]
        {
            "raw",
            "const value:any=JSON.rawJSON('123');console.log(JSON.isRawJSON(value),JSON.isRawJSON({rawJSON:'123'}));console.log(JSON.stringify({value:value}));let count=0;for(const s of ['', ' 1', '{}', '[]']){try{JSON.rawJSON(s);}catch(e){count++;}}console.log(count);",
            "true false\n{\"value\":123}\n3\n",
            true,
            ""
        },
        new object[]
        {
            "compact_only",
            "type Record={x:number;y:string};const r:Record={x:2,y:'a'};console.log(r.x,r.y);r.x=5;console.log(r.x);",
            "2 a\n5\n",
            true,
            ""
        },
        new object[]
        {
            "http_implied",
            "import * as http from 'http';console.log(typeof http.createServer);",
            "function\n",
            true,
            ""
        },
        new object[]
        {
            "minimal",
            "const value=1;",
            "",
            true,
            ""
        },
    ];

    [Theory]
    [MemberData(nameof(JsonMetadataPrograms))]
    public void Isolated_JsonMetadata_PreservesSerializationShapesAndDeployment(
        string name, string source, string expected, bool standalone, string options)
    {
        // The rawJSON baseline retains its existing object-input limitation.
        // This suite verifies ownership parity, including mutable shapes and Proxy deployment.
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"json-metadata_{name}.dll");
        var deployment = standalone ? " --standalone" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{dllPath}\" --verify {options}{deployment}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        if (standalone) Assert.DoesNotContain("SharpTS", GetAssemblyReferences(dllPath));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error)));
    }

    public static IEnumerable<object[]> BroadcastChannelMetadataPrograms =>
    [
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                import {BroadcastChannel} from 'worker_threads';const channel=new BroadcastChannel('name');console.log(channel.name);channel.close();
                """,
            },
            "name\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                const a=new BroadcastChannel('fanout');const b=new BroadcastChannel('fanout');const c=new BroadcastChannel('fanout');const values:string[]=[];function receive(value:string){values.push(value);if(values.length===2)console.log(values.sort().join(','));}a.on('message',()=>console.log('echo'));b.on('message',(event:any)=>receive('b:'+event.data));c.on('message',(event:any)=>receive('c:'+event.data));a.postMessage('value');a.close();b.close();c.close();
                """,
            },
            "b:value,c:value\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                const a=new BroadcastChannel('Topic');const b=new BroadcastChannel('topic');a.on('message',()=>console.log('echo'));b.on('message',()=>console.log('wrong'));a.postMessage('value');a.close();b.close();console.log('done');
                """,
            },
            "done\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                const a=new BroadcastChannel('clone');const b=new BroadcastChannel('clone');const c=new BroadcastChannel('clone');const values:string[]=[];const value:any={id:1,nested:[2]};function receive(event:any){values.push(event.data.id+':'+event.data.nested[0]);event.data.id=8;event.data.nested[0]=9;if(values.length===2)console.log(values.sort().join(','));}b.on('message',receive);c.on('message',receive);a.postMessage(value);value.id=7;value.nested[0]=6;a.close();b.close();c.close();
                """,
            },
            "1:2,1:2\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                const a=new BroadcastChannel('errors');const b=new BroadcastChannel('errors');b.on('messageerror',()=>console.log('listener-error'));b.onmessageerror=()=>console.log('property-error');b.on('message',(event:any)=>console.log(event.data));a.postMessage({nested:[()=>{}]});a.postMessage('after');a.close();b.close();
                """,
            },
            "listener-error\nproperty-error\nafter\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                const a=new BroadcastChannel('handlers');const b=new BroadcastChannel('handlers');function removed(event:any){console.log('removed');}b.addEventListener('message',removed);b.removeEventListener('message',removed);b.on('message',(event:any)=>console.log('listener:'+event.data+':'+event.type+':'+(event.target===b)));b.onmessage=(event:any)=>console.log('property:'+event.data);a.postMessage('one');a.close();b.close();
                """,
            },
            "listener:one:message:true\nproperty:one\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                const channel=new BroadcastChannel('refs');channel.unref();channel.unref();channel.ref();channel.ref();channel.close();channel.close();channel.unref();console.log('done');
                """,
            },
            "done\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                const a=new BroadcastChannel('closed');const b=new BroadcastChannel('closed');b.on('close',()=>console.log('closed'));b.on('message',(event:any)=>console.log(event.data));a.postMessage('queued');b.close();a.postMessage('late');a.close();try{a.postMessage('invalid');}catch(error:any){console.log(error.message);}
                """,
            },
            "closed\nInvalidStateError: BroadcastChannel is closed\nqueued\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                const a=new BroadcastChannel('binary');const b=new BroadcastChannel('binary');const value=new Uint8Array([1,2]);b.on('message',(event:any)=>console.log(event.data[0],event.data[1]));a.postMessage(value);value[0]=8;a.close();b.close();
                """,
            },
            "1 2\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                async function run(){await new Promise<void>(resolve=>setTimeout(resolve,1));const a=new BroadcastChannel('async');const b=new BroadcastChannel('async');b.onmessage=(event:any)=>console.log(event.data);a.postMessage('async');a.close();b.close();}run();
                """,
            },
            "async\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                function* values():Generator<any,void,any>{const channel=new BroadcastChannel('generator');yield channel;channel.postMessage('generator');channel.close();}const iterator=values();const a:any=iterator.next().value;const b=new BroadcastChannel(a.name);b.on('message',(event:any)=>console.log(event.data));iterator.next();b.close();
                """,
            },
            "generator\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.ts"] = """
                import * as workers from 'node:worker_threads';console.log(typeof workers.BroadcastChannel);const channel=new BroadcastChannel('namespace');console.log(channel.name);channel.close();
                """,
            },
            "function\nnamespace\n", "main.ts", true
        },
        new object[]
        {
            new Dictionary<string, string>
            {
                ["main.cjs"] = """
                const workers=require('node:worker_threads');console.log(typeof workers.BroadcastChannel);const channel=new BroadcastChannel('common');console.log(channel.name);channel.close();
                """,
            },
            "function\ncommon\n", "main.cjs", true
        }
    ];

    [Theory]
    [MemberData(nameof(BroadcastChannelMetadataPrograms))]
    public void Isolated_BroadcastChannelMetadata_PreservesRegistryDeliveryAndCloneBehavior(Dictionary<string, string> files, string expected, string entryPoint, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        foreach (var (path, source) in files) tempDir.CreateFile(path, source);
        var dllPath = tempDir.GetPath("broadcast_channel_metadata.dll");
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

    [SkippableFact]
    public void Isolated_DnsLookupOptions_PreserveLiteralsAndStableOrdering()
    {
        // The isolated child uses the same machine's OS resolver configuration.
        SharpTS.Tests.SharedTests.DnsLookupOptionsTests.RequireLocalhostIPv4();
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = SharpTS.Tests.SharedTests.DnsLookupOptionsTests.Program
        };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        var (tempDir, dllPath) = CompileStandaloneModule(files, "main.ts");
        try
        {
            Assert.DoesNotContain(GetAssemblyReferences(dllPath), name => name == "SharpTS");
            Assert.Equal(SharpTS.Tests.SharedTests.DnsLookupOptionsTests.Expected,
                ExecuteCompiledDllIsolated(dllPath, timeoutMs: 15000));
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

    public static IEnumerable<object[]> SentinelPrograms =>
    [
        new object[]
        {
            "undefined_values",
            "const value:any=undefined;console.log(typeof value,value===undefined,value==null,value===null);console.log(String(value),Boolean(value),Number.isNaN(Number(value)),value??\"fallback\");\n",
            "undefined true true false\nundefined false true fallback\n",
            false,
            "",
        },
        new object[]
        {
            "undefined_arguments",
            "function f(a?:number,b:number=7){console.log(a===undefined,b);}const callable:any=f;callable();callable(3);function empty(){}console.log(empty()===undefined);\n",
            "true 7\nfalse 7\ntrue\n",
            false,
            "",
        },
        new object[]
        {
            "undefined_arrays",
            "const a:number[]=[1];console.log(a.shift(),a.shift()===undefined);const b:boolean[]=[true];console.log(b.shift(),b.shift()===undefined);const holes=new Array(2);console.log(holes[0]===undefined,holes.length,[...holes].join(\":\"));\n",
            "1 true\ntrue true\ntrue 2 :\n",
            false,
            "",
        },
        new object[]
        {
            "lexical_read",
            "function outer(){const get=()=>x;try{get();}catch(e){console.log(e.name);}let x:any=undefined;console.log(get()===undefined);x=4;console.log(get());}outer();\n",
            "ReferenceError\ntrue\n4\n",
            false,
            "",
        },
        new object[]
        {
            "lexical_typeof",
            "function outer(){const get=()=>typeof x;try{get();}catch(e){console.log(e.name);}let x:any=undefined;console.log(get());}outer();\n",
            "ReferenceError\nundefined\n",
            false,
            "",
        },
        new object[]
        {
            "lexical_nested",
            "let x=\"outer\";function outer(){const get=()=>()=>x;try{get()();}catch(e){console.log(e.name);}let x=\"inner\";console.log(get()());}outer();console.log(x);\n",
            "ReferenceError\ninner\nouter\n",
            false,
            "",
        },
        new object[]
        {
            "undefined_async",
            "async function f(){await Promise.resolve(0);}f().then(v=>console.log(v===undefined,typeof v));\n",
            "true undefined\n",
            false,
            "",
        },
        new object[]
        {
            "undefined_generator",
            "function* g(){yield undefined;}const it=g();const first=it.next();const second=it.next();console.log(first.value===undefined,first.done,second.value===undefined,second.done);\n",
            "true false true true\n",
            false,
            "",
        },
    ];

    [Theory]
    [MemberData(nameof(SentinelPrograms))]
    public void Isolated_Sentinels_PreserveUndefinedAndLexicalInitialization(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"sentinels_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }


    public static IEnumerable<object[]> UnionValuePrograms =>
    [
        new object[]
        {
            "scalar_parameters",
            "function show(value:number|string){console.log(typeof value,String(value));}show(3);show(\"text\");\n",
            "number 3\nstring text\n",
            false,
            "",
        },
        new object[]
        {
            "union_returns",
            "function choose(flag:boolean):number|string{return flag?7:\"seven\";}const a=choose(true);const b=choose(false);console.log(typeof a,a,typeof b,b);\n",
            "number 7 string seven\n",
            false,
            "",
        },
        new object[]
        {
            "nullable_union",
            "function show(value:number|null|undefined){console.log(typeof value,value===null,value===undefined);}show(4);show(null);show(undefined);\n",
            "number false false\nobject true false\nundefined false true\n",
            false,
            "",
        },
        new object[]
        {
            "union_arrays",
            "const values:(number|string)[]=[1,\"two\",3];for(const value of values){console.log(typeof value,String(value));}console.log(values.map(value=>typeof value).join(\",\"));\n",
            "number 1\nstring two\nnumber 3\nnumber,string,number\n",
            false,
            "",
        },
        new object[]
        {
            "union_objects",
            "function show(value:number|{value:number}){if(typeof value===\"number\"){console.log(\"number\",value);}else{console.log(\"object\",value.value);}}show(2);show({value:5});\n",
            "number 2\nobject 5\n",
            false,
            "",
        },
        new object[]
        {
            "union_async",
            "async function choose(flag:boolean):Promise<number|string>{await Promise.resolve(0);return flag?4:\"four\";}Promise.all([choose(true),choose(false)]).then(values=>{for(const value of values){console.log(typeof value,String(value));}});\n",
            "number 4\nstring four\n",
            false,
            "",
        },
        new object[]
        {
            "union_boolean",
            "function show(value:number|string|boolean){console.log(typeof value,String(value));}show(5);show(\"five\");show(true);show(false);\n",
            "number 5\nstring five\nboolean true\nboolean false\n",
            false,
            "",
        },
    ];

    [Theory]
    [MemberData(nameof(UnionValuePrograms))]
    public void Isolated_UnionValues_PreserveClassificationAndTypedValues(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"union_values_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }


    public static IEnumerable<object[]> ObjectFieldPrograms =>
    [
        new object[]
        {
            "literal_fields",
            "const o:any={a:1,u:undefined,n:null};o[\"a\"]=3;o[\"b\"]=4;console.log(o.a,o.b,\"a\" in o,\"u\" in o,\"missing\" in o,o.u===undefined,o.n===null,o.missing===undefined);console.log(Object.keys(o).join(\",\"));\n",
            "3 4 true true false true true true\na,u,n,b\n",
            false,
            "",
        },
        new object[]
        {
            "generic_class_fields",
            "class Box<T>{value:T;constructor(value:T){this.value=value;}get(){return this.value;}}const a:any=new Box<number>(3);const b:any=new Box<string>(\"text\");a[\"value\"]=5;b[\"value\"]=\"next\";console.log(a.value,a.get(),b.value,b.get(),\"value\" in a);\n",
            "5 5 next next true\n",
            false,
            "",
        },
        new object[]
        {
            "class_expression",
            "const Box=class{value:number=2;read(){return this.value;}};const b:any=new Box();b[\"value\"]=6;console.log(b.value,b.read(),\"value\" in b,\"absent\" in b,Object.keys(b).join(\",\"));\n",
            "6 6 true false value\n",
            false,
            "",
        },
        new object[]
        {
            "inherited_fields",
            "class Base{a:number=2;}class Child extends Base{b:number=3;}const c:any=new Child();c[\"a\"]=5;c[\"b\"]=7;console.log(c.a,c.b,\"a\" in c,\"b\" in c,Object.keys(c).join(\",\"));\n",
            "5 7 true true a,b\n",
            false,
            "",
        },
        new object[]
        {
            "record_array",
            "const rows:{x:number,y:number}[]=[{x:1,y:2},{x:3,y:4}];let sum=0;for(const row of rows){sum+=row.x+row.y;}const chosen:any=rows[1];chosen[\"x\"]=8;console.log(sum,chosen.x,chosen.y,\"x\" in chosen,Object.keys(chosen).join(\",\"));\n",
            "10 8 4 true x,y\n",
            false,
            "",
        },
        new object[]
        {
            "descriptors",
            "const o:any={a:1};let stored=2;Object.defineProperty(o,\"value\",{get(){return stored;},set(value:number){stored=value;},enumerable:true,configurable:true});o[\"value\"]=6;console.log(o.value,stored,\"value\" in o,Object.keys(o).join(\",\"));delete o.a;console.log(\"a\" in o,o.a===undefined);\n",
            "6 6 true a,value\nfalse true\n",
            false,
            "",
        },
        new object[]
        {
            "prototype_fields",
            "const parent:any={base:3};const o:any=Object.create(parent);o[\"own\"]=4;console.log(o.base,o.own,\"base\" in o,\"own\" in o,Object.keys(o).join(\",\"),Object.getPrototypeOf(o)===parent);\n",
            "3 4 true true own true\n",
            false,
            "",
        },
        new object[]
        {
            "environment_fields",
            "const key=\"SHARPTS_FIELD_CONTRACT_PROBE_1599\";process.env[key]=\"value\";console.log(process.env[key],key in process.env);delete process.env[key];console.log(process.env[key]===undefined,key in process.env);\n",
            "value true\ntrue false\n",
            false,
            "",
        },
    ];

    [Theory]
    [MemberData(nameof(ObjectFieldPrograms))]
    public void Isolated_ObjectFields_PreserveFieldsAndPropertyAccess(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"object_fields_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }


    public static IEnumerable<object[]> UriComponentPrograms =>
    [
        new object[]
        {
            "direct",
            "console.log(encodeURIComponent(\"hello world\"));console.log(encodeURIComponent(\"a=b&c/d?e#f\"));console.log(encodeURIComponent(\"abcABC123-_.~\"));console.log(decodeURIComponent(\"a%3Db%26c%2Fd%3Fe%23f\"));console.log(encodeURIComponent(\"\")===\"\",decodeURIComponent(\"\") === \"\");\n",
            "hello%20world\na%3Db%26c%2Fd%3Fe%23f\nabcABC123-_.~\na=b&c/d?e#f\ntrue true\n",
            false,
            "",
        },
        new object[]
        {
            "first_class",
            "const encode=encodeURIComponent;const decode=decodeURIComponent;console.log(encode.name,encode.length,encode.prototype===undefined);console.log(decode.name,decode.length,decode.prototype===undefined);console.log(encode===encodeURIComponent,decode===decodeURIComponent,encode===globalThis.encodeURIComponent,decode===globalThis.decodeURIComponent);console.log(decode(encode(\"hello world\")));\n",
            "encodeURIComponent 1 true\ndecodeURIComponent 1 true\ntrue true true true\nhello world\n",
            false,
            "",
        },
        new object[]
        {
            "borrowed",
            "const encode:any=encodeURIComponent;const decode:any=decodeURIComponent;console.log(encode.call({x:1},\"a b\"),decode.apply(null,[\"a%20b\"]));const bound=encode.bind({x:2});console.log(bound(\"c d\"),bound());\n",
            "a%20b a b\nc%20d undefined\n",
            false,
            "",
        },
        new object[]
        {
            "unicode",
            "const s=\"\\u00E9\\u6F22\\uD83D\\uDE00\";const encoded=encodeURIComponent(s);const decoded=decodeURIComponent(encoded);console.log(encoded,decoded===s,decoded.length);console.log(decoded.charCodeAt(0),decoded.charCodeAt(1),decoded.charCodeAt(2),decoded.charCodeAt(3));\n",
            "%C3%A9%E6%BC%A2%F0%9F%98%80 true 4\n233 28450 55357 56832\n",
            false,
            "",
        },
        new object[]
        {
            "coercion_order",
            "let log=\"\";const value:any={toString(){log+=\"s\";return \"a b\";},valueOf(){log+=\"v\";return 3;}};console.log(encodeURIComponent(value),log);\n",
            "a%20b s\n",
            false,
            "",
        },
        new object[]
        {
            "omitted_values",
            "const encode:any=encodeURIComponent;const decode:any=decodeURIComponent;console.log(encode(),decode());console.log(encode(undefined),decode(undefined),encode(null),decode(null));\n",
            "undefined undefined\nundefined undefined null null\n",
            false,
            "",
        },
        new object[]
        {
            "coercion_values",
            "const encode:any=encodeURIComponent;const decode:any=decodeURIComponent;console.log(encode(42),decode(42),encode(true),decode(false));console.log(encode([1,2]),decode([1,2]));\n",
            "42 42 true false\n1%2C2 1,2\n",
            false,
            "",
        },
    ];

    [Theory]
    [MemberData(nameof(UriComponentPrograms))]
    public void Isolated_UriComponents_PreserveCoercionValuesAndDeployment(
        string name, string source, string expected, bool hosted, string extraArguments)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"uri_components_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting} {extraArguments}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error)));
    }


    public static IEnumerable<object[]> GlobalObjectPrograms =>
    [
        new object[]
        {
            "identity",
            "const root:any=globalThis;console.log(typeof root,root!==null,root===globalThis,root.globalThis===root,root.global===root);console.log(root.Object===Object,root.Array===Array,root.Function===Function,root.Error===Error,root.TypeError===TypeError);\n",
            "object true true true true\ntrue true true true true\n",
            false,
            true,
        },
        new object[]
        {
            "writes",
            "const root:any=globalThis;root.__sharptsGlobalProbe=4;console.log(globalThis.__sharptsGlobalProbe,root[\"__sharptsGlobalProbe\"]);globalThis[\"__sharptsGlobalProbe\"]=7;console.log(root.__sharptsGlobalProbe);delete root.__sharptsGlobalProbe;console.log(root.__sharptsGlobalProbe===undefined);\n",
            "4 4\n7\ntrue\n",
            false,
            true,
        },
        new object[]
        {
            "constants",
            "const root:any=globalThis;console.log(root.undefined===undefined,Number.isNaN(root.NaN),root.Infinity===Infinity);console.log(root.Math===Math,root.JSON===JSON,root.Symbol===Symbol,root.process===process);\n",
            "true true true\ntrue true true true\n",
            false,
            true,
        },
        new object[]
        {
            "optional_classes",
            "const root:any=globalThis;const d=new Date(0);const r=new RegExp(\"a\");console.log(root.Date===Date,root.RegExp===RegExp,d.getTime(),r.test(\"a\"));console.log(root.Reflect===Reflect);\n",
            "true true 0 true\ntrue\n",
            false,
            true,
        },
        new object[]
        {
            "optional_buffer_text",
            "const root:any=globalThis;const b=Buffer.from(\"a\");const e=new TextEncoder();const d=new TextDecoder();console.log(root.Buffer===Buffer,root.TextEncoder===TextEncoder,root.TextDecoder===TextDecoder,b[0],d.decode(e.encode(\"text\")));\n",
            "true true true 97 text\n",
            false,
            true,
        },
        new object[]
        {
            "optional_fetch_crypto",
            "const root:any=globalThis;const f=fetch;const c=crypto;console.log(typeof f,root.fetch===f,typeof c,root.crypto===c);\n",
            "function true object true\n",
            false,
            true,
        },
        new object[]
        {
            "indirect_nonstring",
            "const e:any=eval;const value:any={x:1};console.log(e(42),e(true),e(null),e(undefined)===undefined,e(value)===value);console.log(e.name,e.length,e===globalThis.eval);\n",
            "42 true null true true\neval 1 true\n",
            false,
            true,
        },
        new object[]
        {
            "direct_eval_control",
            "function get():number{const value=4;return eval(\"value+2\") as number;}console.log(get(),eval(\"1+2\"));\n",
            "6 3\n",
            false,
            true,
        },
        new object[]
        {
            "hosted_property",
            "export function read(name:string):any{return globalThis[name];}export function write(name:string,value:any):void{globalThis[name]=value;}\n",
            "",
            true,
            true,
        },
        new object[]
        {
            "hosted_identity",
            "export function root():any{return globalThis;}\n",
            "",
            true,
            true,
        },
        new object[]
        {
            "ordinary_accessor_control",
            "const root:any={};const box:any={value:2};Object.defineProperty(root,\"value\",{get(){return box.value;},set(value:number){box.value=value;},configurable:true});root[\"value\"]=8;console.log(root.value,box.value);delete root.value;console.log(root.value===undefined);\n",
            "8 8\ntrue\n",
            false,
            true,
        },
        new object[]
        {
            "delete_nonnull_control",
            "const root:any=globalThis;root.__sharptsGlobalDelete=3;console.log(delete root.__sharptsGlobalDelete,root.__sharptsGlobalDelete===undefined);Object.defineProperty(root,\"__sharptsGlobalDelete\",{value:9,writable:false,configurable:true});console.log(root.__sharptsGlobalDelete,Object.getOwnPropertyDescriptor(root,\"__sharptsGlobalDelete\")!.writable);console.log(delete root.__sharptsGlobalDelete,root.__sharptsGlobalDelete===undefined);\n",
            "true true\n9 false\ntrue true\n",
            false,
            true,
        },
        new object[]
        {
            "isnan_controls",
            "console.log(isNaN(\"x\" as any),Number.isNaN(\"x\" as any));const root:any=globalThis;console.log(root.isNaN(NaN),root.isNaN(4),root.parseInt(\"3\"));\n",
            "true false\ntrue false 3\n",
            false,
            true,
        },
        new object[]
        {
            "function_identity_control",
            "const probe:any=Function(\"return this\")();console.log(probe===globalThis,probe.Object===Object);\n",
            "true true\n",
            false,
            true,
        },
        new object[]
        {
            "direct_dynamic_eval_control",
            "const text=String(\"1+2\");console.log(eval(text));\n",
            "3\n",
            false,
            false,
        },
    ];

    [Theory]
    [MemberData(nameof(GlobalObjectPrograms))]
    public void Isolated_GlobalObject_PreservesIdentityPropertiesAndEvalDeployment(
        string name, string source, string expected, bool hosted, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"global_object_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var deployment = standalone ? " --standalone" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify{deployment}{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }

    public static IEnumerable<object[]> NamespaceValuePrograms =>
    [
        new object[]
        {
            "hosted_members",
            "namespace Api {export function plus(value:number){return value+2;}}export function call(value:number):number{return Api.plus(value);}\n",
            "",
            "",
            true,
        },
        new object[]
        {
            "hosted_identity",
            "namespace Api {export const value=4;}export function root():any{return Api;}\n",
            "",
            "",
            true,
        },
        new object[]
        {
            "value_identity_control",
            "namespace Values {export const value=3;}const values:any=Values;console.log(Values.value,values.value,values===Values);\n",
            "",
            "3 3 true\n",
            false,
        },
        new object[]
        {
            "nested_values_control",
            "namespace Outer.Inner {export const value=7;}const outer:any=Outer;console.log(Outer.Inner.value,outer.Inner.value,outer.Inner===Outer.Inner);\n",
            "",
            "7 7 true\n",
            false,
        },
        new object[]
        {
            "merged_values_control",
            "namespace Joined {export const first=3;}namespace Joined {export const second=5;}const joined:any=Joined;console.log(Joined.first,joined.first,joined.second,joined===Joined);\n",
            "",
            "3 3 5 true\n",
            false,
        },
        new object[]
        {
            "module_values_control",
            "import {Library} from \"./lib.ts\";const library:any=Library;console.log(Library.value,library===Library);\n",
            "export namespace Library {export const value=8;}\n",
            "8 true\n",
            false,
        },
    ];

    [Theory]
    [MemberData(nameof(NamespaceValuePrograms))]
    public void Isolated_NamespaceValues_PreserveMembersIdentityAndDeployment(
        string name, string source, string librarySource, string expected, bool hosted)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        if (librarySource.Length != 0) tempDir.CreateFile("lib.ts", librarySource);
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"namespace_values_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }

    public static IEnumerable<object[]> EventSubscriptionPrograms =>
    [
        new object[]
        {
            "exit_add",
            "@DotNetType(\"System.AppDomain\")\ndeclare class AppDomain {\n static readonly currentDomain: AppDomain;\n addEventListener(name:string,handler:(sender:any,args:any)=>void):void;\n removeEventListener(name:string,handler:(sender:any,args:any)=>void):void;\n}\nconst domain=AppDomain.currentDomain;const handler=(sender:any,args:any)=>console.log(\"exit\");domain.addEventListener(\"ProcessExit\",handler);console.log(\"wired\");\n",
            "wired\nexit\n",
            false,
            true,
        },
        new object[]
        {
            "exit_duplicate",
            "@DotNetType(\"System.AppDomain\")\ndeclare class AppDomain {\n static readonly currentDomain: AppDomain;\n addEventListener(name:string,handler:(sender:any,args:any)=>void):void;\n removeEventListener(name:string,handler:(sender:any,args:any)=>void):void;\n}\nconst domain=AppDomain.currentDomain;const handler=(sender:any,args:any)=>console.log(\"exit\");domain.addEventListener(\"ProcessExit\",handler);domain.addEventListener(\"ProcessExit\",handler);console.log(\"wired\");\n",
            "wired\nexit\n",
            false,
            true,
        },
        new object[]
        {
            "exit_remove",
            "@DotNetType(\"System.AppDomain\")\ndeclare class AppDomain {\n static readonly currentDomain: AppDomain;\n addEventListener(name:string,handler:(sender:any,args:any)=>void):void;\n removeEventListener(name:string,handler:(sender:any,args:any)=>void):void;\n}\nconst domain=AppDomain.currentDomain;const handler=(sender:any,args:any)=>console.log(\"exit\");domain.addEventListener(\"ProcessExit\",handler);domain.removeEventListener(\"ProcessExit\",handler);domain.removeEventListener(\"ProcessExit\",handler);console.log(\"wired\");\n",
            "wired\n",
            false,
            true,
        },
        new object[]
        {
            "exit_readd",
            "@DotNetType(\"System.AppDomain\")\ndeclare class AppDomain {\n static readonly currentDomain: AppDomain;\n addEventListener(name:string,handler:(sender:any,args:any)=>void):void;\n removeEventListener(name:string,handler:(sender:any,args:any)=>void):void;\n}\nconst domain=AppDomain.currentDomain;const handler=(sender:any,args:any)=>console.log(\"exit\");domain.addEventListener(\"ProcessExit\",handler);domain.removeEventListener(\"ProcessExit\",handler);domain.addEventListener(\"ProcessExit\",handler);console.log(\"wired\");\n",
            "wired\nexit\n",
            false,
            true,
        },
        new object[]
        {
            "exit_distinct",
            "@DotNetType(\"System.AppDomain\")\ndeclare class AppDomain {\n static readonly currentDomain: AppDomain;\n addEventListener(name:string,handler:(sender:any,args:any)=>void):void;\n removeEventListener(name:string,handler:(sender:any,args:any)=>void):void;\n}\nconst domain=AppDomain.currentDomain;const a=(sender:any,args:any)=>console.log(\"a\");const b=(sender:any,args:any)=>console.log(\"b\");domain.addEventListener(\"ProcessExit\",a);domain.addEventListener(\"ProcessExit\",b);domain.removeEventListener(\"ProcessExit\",a);console.log(\"wired\");\n",
            "wired\nb\n",
            false,
            true,
        },
        new object[]
        {
            "exit_dynamic",
            "@DotNetType(\"System.AppDomain\")\ndeclare class AppDomain {\n static readonly currentDomain: AppDomain;\n addEventListener(name:string,handler:(sender:any,args:any)=>void):void;\n removeEventListener(name:string,handler:(sender:any,args:any)=>void):void;\n}\nconst domain=AppDomain.currentDomain;const handler=(sender:any,args:any)=>console.log(\"exit\");const name:string=\"ProcessExit\";domain.addEventListener(name,handler);console.log(\"wired\");\n",
            "wired\nexit\n",
            false,
            false,
        },
        new object[]
        {
            "hosted_add",
            "@DotNetType(\"System.AppDomain\")\ndeclare class AppDomain {\n static readonly currentDomain: AppDomain;\n addEventListener(name:string,handler:(sender:any,args:any)=>void):void;\n removeEventListener(name:string,handler:(sender:any,args:any)=>void):void;\n}\nexport function wire():number{const domain=AppDomain.currentDomain;const handler=(sender:any,args:any)=>console.log(\"exit\");domain.addEventListener(\"ProcessExit\",handler);return 1;}\n",
            "",
            true,
            true,
        },
        new object[]
        {
            "hosted_remove",
            "@DotNetType(\"System.AppDomain\")\ndeclare class AppDomain {\n static readonly currentDomain: AppDomain;\n addEventListener(name:string,handler:(sender:any,args:any)=>void):void;\n removeEventListener(name:string,handler:(sender:any,args:any)=>void):void;\n}\nexport function wire():number{const domain=AppDomain.currentDomain;const handler=(sender:any,args:any)=>console.log(\"exit\");domain.addEventListener(\"ProcessExit\",handler);domain.removeEventListener(\"ProcessExit\",handler);return 1;}\n",
            "",
            true,
            true,
        },
    ];

    [Theory]
    [MemberData(nameof(EventSubscriptionPrograms))]
    public void Isolated_EventSubscriptions_PreserveHandlerIdentityAndDeployment(
        string name, string source, string expected, bool hosted, bool standalone)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"event_subscriptions_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var isolation = standalone ? " --standalone" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify{isolation}{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        // The dynamic-name bridge loads SharpTS by name; deployment is checked separately.
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.Equal(!standalone, File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }

    public static IEnumerable<object[]> ResourceDisposalPrograms =>
    [
        new object[]
        {
            "scoped_order",
            "{using a={ [Symbol.dispose](){console.log(\"a\");} };using b={ [Symbol.dispose](){console.log(\"b\");} };console.log(\"body\");}console.log(\"after\");\n",
            "",
            "body\nb\na\nafter\n",
            false,
        },
        new object[]
        {
            "nullish",
            "{using a:any=null;using b:any=undefined;console.log(\"body\");}console.log(\"after\");\n",
            "",
            "body\nafter\n",
            false,
        },
        new object[]
        {
            "nested",
            "{using a={ [Symbol.dispose](){console.log(\"outer\");} };{using b={ [Symbol.dispose](){console.log(\"inner\");} };console.log(\"nested\");}console.log(\"outer body\");}\n",
            "",
            "nested\ninner\nouter body\nouter\n",
            false,
        },
        new object[]
        {
            "function_return",
            "function work():number{using r={ [Symbol.dispose](){console.log(\"dispose\");} };return 7;}console.log(work());\n",
            "",
            "dispose\n7\n",
            false,
        },
        new object[]
        {
            "thrown_body",
            "try{{using r={ [Symbol.dispose](){console.log(\"dispose\");} };throw new Error(\"body\");}}catch(e){console.log((e as any).message);}\n",
            "",
            "dispose\nbody\n",
            false,
        },
        new object[]
        {
            "receiver",
            "{using r={ value:7,[Symbol.dispose](){console.log(this.value);this.value=9;} };console.log(r.value);}console.log(\"done\");\n",
            "",
            "7\n7\ndone\n",
            false,
        },
        new object[]
        {
            "data_descriptor",
            "const r:any={value:4};Object.defineProperty(r,Symbol.dispose,{value:function(){console.log(this.value);}});{using x=r;console.log(\"body\");}\n",
            "",
            "body\n4\n",
            false,
        },
        new object[]
        {
            "thrown_disposer",
            "try{{using r={ [Symbol.dispose](){throw new Error(\"dispose\");} };console.log(\"body\");}}catch(e){console.log((e as any).message);}\n",
            "",
            "body\ndispose\n",
            false,
        },
        new object[]
        {
            "hosted_dispose",
            "export function work():number{using r={ [Symbol.dispose](){} };return 4;}\n",
            "",
            "",
            true,
        },
        new object[]
        {
            "hosted_receiver",
            "export function work(value:number):number{using r={value:value,[Symbol.dispose](){this.value=0;}};return r.value;}\n",
            "",
            "",
            true,
        },
        new object[]
        {
            "loop_finally_control",
            "for(let i=0;i<3;i++){try{if(i===0)continue;if(i===1)break;}finally{console.log(i);}}console.log(\"after\");\n",
            "",
            "0\n1\nafter\n",
            false,
        },
        new object[]
        {
            "generator_finally_control",
            "function* values(){try{yield 1;return 2;}finally{console.log(\"dispose\");}}const g=values();console.log(g.next().value);console.log(g.return(9).value);\n",
            "",
            "1\ndispose\n9\n",
            false,
        },
    ];

    [Theory]
    [MemberData(nameof(ResourceDisposalPrograms))]
    public void Isolated_ResourceDisposal_PreservesCleanupReceiversAndDeployment(
        string name, string source, string librarySource, string expected, bool hosted)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        if (librarySource.Length != 0) tempDir.CreateFile("lib.ts", librarySource);
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"resource_disposal_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }

    public static IEnumerable<object[]> EnumReversePrograms =>
    [
        new object[]
        {
            "dynamic",
            "enum Direction {North=1,South=4,West=7}let key:number=4;console.log(Direction[key]);key=7;console.log(Direction[key]);\n",
            "",
            "South\nWest\n",
            false,
        },
        new object[]
        {
            "duplicate",
            "enum Values {First=1,Last=1,Other=2}let key:number=1;console.log(Values[key],Values[1]);\n",
            "",
            "Last Last\n",
            false,
        },
        new object[]
        {
            "module",
            "import {Values} from \"./lib.ts\";let key:number=3;console.log(Values[key],Values.First);\n",
            "export enum Values {First=2,Second=3}\n",
            "Second 2\n",
            false,
        },
        new object[]
        {
            "function_any",
            "enum Values {First=2,Second=3}function read(key:any):string{return Values[key];}console.log(read(3),read(2));\n",
            "",
            "Second First\n",
            false,
        },
        new object[]
        {
            "hosted_literal",
            "enum Values {First=2,Second=3}export function read():string{return Values[3];}\n",
            "",
            "",
            true,
        },
    ];

    [Theory]
    [MemberData(nameof(EnumReversePrograms))]
    public void Isolated_EnumReverse_PreservesLookupAndDeployment(
        string name, string source, string librarySource, string expected, bool hosted)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        if (librarySource.Length != 0) tempDir.CreateFile("lib.ts", librarySource);
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"enum_reverse_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }

    public static IEnumerable<object[]> ReceiverGuardPrograms =>
    [
        new object[]
        {
            "null_receiver",
            "const trim:any=String.prototype.trim;try{trim.call(null);}catch(e){console.log(e instanceof TypeError);}\n",
            "true\n",
            false,
        },
        new object[]
        {
            "undefined_receiver",
            "const trim:any=String.prototype.trim;try{trim.call(undefined);}catch(e){console.log(e instanceof TypeError);}\n",
            "true\n",
            false,
        },
        new object[]
        {
            "symbol_receiver",
            "const trim:any=String.prototype.trim;try{trim.call(Symbol(\"x\"));}catch(e){console.log(e instanceof TypeError);}\n",
            "true\n",
            false,
        },
        new object[]
        {
            "number_receiver",
            "const slice:any=String.prototype.slice;console.log(slice.call(12345,1,3));\n",
            "23\n",
            false,
        },
        new object[]
        {
            "boolean_receiver",
            "const upper:any=String.prototype.toUpperCase;console.log(upper.call(true));\n",
            "TRUE\n",
            false,
        },
        new object[]
        {
            "object_receiver",
            "let calls=0;const r={toString(){calls++;return \"  abc  \";}};const trim:any=String.prototype.trim;console.log(trim.call(r),calls);\n",
            "abc 1\n",
            false,
        },
        new object[]
        {
            "primitive_hook",
            "let hint=\"\";const r={ [Symbol.toPrimitive](value:string){hint=value;return \"  abc  \";} };const trim:any=String.prototype.trim;console.log(trim.call(r),hint);\n",
            "abc string\n",
            false,
        },
        new object[]
        {
            "bound_receiver",
            "const trim:any=String.prototype.trim;const fn=trim.bind(\" abc \");console.log(fn());\n",
            "abc\n",
            false,
        },
        new object[]
        {
            "array_missing_receiver",
            "try{\n// @ts-expect-error Deliberately omit the call receiver to verify the runtime TypeError.\nArray.prototype.join.call();\n}catch(e){console.log(e instanceof TypeError);}\n",
            "true\n",
            false,
        },
        new object[]
        {
            "arraylike_receiver",
            "const join:any=Array.prototype.join;console.log(join.call({0:\"a\",1:\"b\",length:2},\"-\"));\n",
            "a-b\n",
            false,
        },
        new object[]
        {
            "coercion_error",
            "const r={toString(){throw \"coercion\";}};const trim:any=String.prototype.trim;try{trim.call(r);}catch(e){console.log(e);}\n",
            "coercion\n",
            false,
        },
        new object[]
        {
            "string_receiver",
            "const trim:any=String.prototype.trim;console.log(trim.call(\" abc \"),trim.call(\"\"));\n",
            "abc \n",
            false,
        },
        new object[]
        {
            "hosted_string",
            "export function trim(value:any){const fn:any=String.prototype.trim;return fn.call(value);}\n",
            "",
            true,
        },
        new object[]
        {
            "hosted_array",
            "export function check(){try{\n// @ts-expect-error Deliberately omit the call receiver to verify the runtime TypeError.\nArray.prototype.join.call();\n}catch(e){return e instanceof TypeError;}}\n",
            "",
            true,
        },
    ];

    [Theory]
    [MemberData(nameof(ReceiverGuardPrograms))]
    public void Isolated_ReceiverGuard_PreservesValidationCoercionAndDeployment(
        string name, string source, string expected, bool hosted)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"receiver_guard_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }

    public static IEnumerable<object[]> StringSymbolDispatchPrograms =>
    [
        new object[]
        {
            "native_protocols",
            "console.log(\"aba\".match(/a/g)!.join(\",\"),\"abc\".search(/b/),\"abc\".replace(/a/,\"X\"),\"aba\".split(/b/).join(\"|\"));\n",
            "a,a 1 Xbc a|a\n",
            false,
        },
        new object[]
        {
            "custom_match",
            "const value:any={tag:\"M\",[Symbol.match](s:string){return this.tag+\":\"+s;}};console.log(\"abc\".match(value));\n",
            "M:abc\n",
            false,
        },
        new object[]
        {
            "custom_search",
            "const value:any={[Symbol.search](s:string){return s.length+4;}};console.log(\"abc\".search(value));\n",
            "7\n",
            false,
        },
        new object[]
        {
            "custom_replace",
            "const value:any={[Symbol.replace](s:string,r:string){return s+\":\"+r;}};console.log(\"abc\".replace(value,\"X\"));\n",
            "abc:X\n",
            false,
        },
        new object[]
        {
            "custom_split",
            "const value:any={[Symbol.split](s:string,limit:number){return [s,String(limit)];}};console.log(\"abc\".split(value,2).join(\"|\"));\n",
            "abc|2\n",
            false,
        },
        new object[]
        {
            "null_method",
            "const value:any={toString(){return \"b\";},[Symbol.match]:null};console.log(\"abc\".match(value)![0]);\n",
            "b\n",
            false,
        },
        new object[]
        {
            "undefined_method",
            "const value:any={toString(){return \"b\";},[Symbol.search]:undefined};console.log(\"abc\".search(value));\n",
            "1\n",
            false,
        },
        new object[]
        {
            "noncallable_method",
            "const value:any={[Symbol.match]:17};try{console.log(\"abc\".match(value));}catch(e){console.log(e instanceof TypeError);}\n",
            "true\n",
            false,
        },
        new object[]
        {
            "getter_order",
            "let order=\"\";const value:any={};Object.defineProperty(value,Symbol.match,{get(){order+=\"get>\";return function(s:string){order+=\"call>\";return s+\"!\";};}});console.log(\"abc\".match(value),order);\n",
            "abc! get>call>\n",
            false,
        },
        new object[]
        {
            "getter_error",
            "const value:any={};Object.defineProperty(value,Symbol.search,{get(){throw \"getter\";}});try{console.log(\"abc\".search(value));}catch(e){console.log(e);}\n",
            "getter\n",
            false,
        },
        new object[]
        {
            "prototype_override",
            "RegExp.prototype[Symbol.match]=function(s:string):any{return [\"hook\",s];};console.log(\"abc\".match(/b/)!.join(\",\"));\n",
            "hook,abc\n",
            false,
        },
        new object[]
        {
            "boxed_prototype",
            "(Number.prototype as any)[Symbol.search]=function(s:string){return s.length+4;};console.log(\"abc\".search(new Number(1) as any));\n",
            "7\n",
            false,
        },
        new object[]
        {
            "function_candidate",
            "const value:any=function(){};value[Symbol.match]=function(s:string){return this===value?s+\"!\":\"wrong\";};console.log(\"abc\".match(value));\n",
            "abc!\n",
            false,
        },
        new object[]
        {
            "undefined_result",
            "const value:any={[Symbol.match](s:string){return undefined;}};console.log(\"abc\".match(value));\n",
            "undefined\n",
            false,
        },
        new object[]
        {
            "hosted_custom",
            "export function match(s:string,value:any){return s.match(value);}\n",
            "",
            true,
        },
        new object[]
        {
            "hosted_native",
            "export function split(s:string){return s.split(/b/);}\n",
            "",
            true,
        },
    ];

    [Theory]
    [MemberData(nameof(StringSymbolDispatchPrograms))]
    public void Isolated_StringSymbolDispatch_PreservesProtocolsAndDeployment(
        string name, string source, string expected, bool hosted)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"string_symbol_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }

    public static IEnumerable<object[]> ProxyConstructionPrograms =>
    [
        new object[]
        {
            "plain_control",
            "console.log(\"plain\");\n",
            "plain\n",
            false,
            false,
        },
        new object[]
        {
            "ordinary_identity",
            "const target:any={value:3};const proxy:any=new Proxy(target,{});proxy.value=8;console.log(target.value,proxy.value,proxy===target);\n",
            "8 8 false\n",
            false,
            true,
        },
        new object[]
        {
            "get_receiver",
            "const target:any={value:4};let observed:any;const proxy:any=new Proxy(target,{get(t:any,k:any,r:any){observed=r;return Reflect.get(t,k,r);}});console.log(proxy.value,observed===proxy);\n",
            "4 true\n",
            false,
            true,
        },
        new object[]
        {
            "set_receiver",
            "const target:any={value:4};let observed:any;const proxy:any=new Proxy(target,{set(t:any,k:any,v:any,r:any){observed=r;t[k]=v+1;return true;}});proxy.value=8;console.log(target.value,observed===proxy);\n",
            "9 true\n",
            false,
            true,
        },
        new object[]
        {
            "primitive_target",
            "const values:any[]=[null,undefined,1,true,\"x\",Symbol(\"s\"),1n];for(const value of values){try{new Proxy(value,{});console.log(\"accepted\");}catch(e){console.log(e instanceof TypeError);}}\n",
            "true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n",
            false,
            true,
        },
        new object[]
        {
            "primitive_handler",
            "const values:any[]=[null,undefined,1,true,\"x\",Symbol(\"s\"),1n];for(const value of values){try{new Proxy({},value);console.log(\"accepted\");}catch(e){console.log(e instanceof TypeError);}}\n",
            "true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n",
            false,
            true,
        },
        new object[]
        {
            "revocable_target",
            "const values:any[]=[null,undefined,1,true,\"x\",Symbol(\"s\"),1n];for(const value of values){try{Proxy.revocable(value,{});console.log(\"accepted\");}catch(e){console.log(e instanceof TypeError);}}\n",
            "true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n",
            false,
            true,
        },
        new object[]
        {
            "revocable_handler",
            "const values:any[]=[null,undefined,1,true,\"x\",Symbol(\"s\"),1n];for(const value of values){try{Proxy.revocable({},value);console.log(\"accepted\");}catch(e){console.log(e instanceof TypeError);}}\n",
            "true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n",
            false,
            true,
        },
        new object[]
        {
            "revocation",
            "const pair:any=Proxy.revocable({value:7},{});console.log(pair.proxy.value);console.log(pair.revoke()===undefined,pair.revoke()===undefined);try{console.log(pair.proxy.value);}catch(e){console.log(e instanceof TypeError);}\n",
            "7\ntrue true\ntrue\n",
            false,
            true,
        },
        new object[]
        {
            "revocable_shape",
            "const pair:any=Proxy.revocable({value:7},{});console.log(Object.keys(pair).join(\",\"),typeof pair.revoke,typeof pair.proxy);\n",
            "proxy,revoke function object\n",
            false,
            true,
        },
        new object[]
        {
            "aliased_revocable",
            "const make:any=Proxy.revocable;const pair:any=make({value:9},{});console.log(pair.proxy.value);pair.revoke();try{console.log(pair.proxy.value);}catch(e){console.log(e instanceof TypeError);}\n",
            "9\ntrue\n",
            false,
            true,
        },
        new object[]
        {
            "nested_proxy",
            "const first:any=new Proxy({value:5},{});const second:any=new Proxy(first,{});console.log(second.value);second.value=10;console.log(first.value);\n",
            "5\n10\n",
            false,
            true,
        },
        new object[]
        {
            "hosted_factory",
            "export function create(target:any,handler:any){return new Proxy(target,handler);}\n",
            "",
            true,
            true,
        },
        new object[]
        {
            "hosted_revocable",
            "export function create(target:any,handler:any){return Proxy.revocable(target,handler);}\n",
            "",
            true,
            true,
        },
        new object[]
        {
            "function_direct_control",
            "const proxy:any=new Proxy(function(a:number,b:number){return a+b;},{apply(t:any,r:any,args:any[]){return args[0]+args[1]+1;}});console.log(typeof proxy,proxy(2,3));\n",
            "function 6\n",
            false,
            true,
        },
        new object[]
        {
            "class_constructor_control",
            "class Value{value:number;constructor(n:number){this.value=n;}}console.log(new Value(8).value);\n",
            "8\n",
            false,
            false,
        },
        new object[]
        {
            "dynamic_constructor_control",
            "class Value{value:number;constructor(n:number){this.value=n;}}const make:any=Value;console.log(new make(8).value);\n",
            "8\n",
            false,
            false,
        },
    ];

    [Theory]
    [MemberData(nameof(ProxyConstructionPrograms))]
    public void Isolated_ProxyConstruction_PreservesFactoriesValidationAndDeployment(
        string name, string source, string expected, bool hosted, bool requiresRuntime)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"proxy_construction_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var deployment = requiresRuntime ? "" : " --standalone";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify{deployment}{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        var runtimePath = tempDir.GetPath("SharpTS.dll");
        Assert.Equal(requiresRuntime, File.Exists(runtimePath));
        if (requiresRuntime)
            Assert.Equal(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(RuntimeEmitter).Assembly.Location)),
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(runtimePath)));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }

    public static IEnumerable<object[]> BuiltInStaticDispatchPrograms =>
    [
        new object[]
        {
            "plain_control",
            "console.log(\"plain\");\n",
            "plain\n",
            false,
        },
        new object[]
        {
            "array_alias",
            "const A:any=Array;const f=A.isArray;console.log(f([]),f({}),f===Array.isArray,f.name,f.length);\n",
            "true false true isArray 1\n",
            false,
        },
        new object[]
        {
            "string_alias",
            "const S:any=String;console.log(S.fromCharCode(65,66),S.fromCodePoint(67),S.raw({raw:[\"a\",\"b\"]},3));console.log(S.fromCharCode===String.fromCharCode,S.fromCodePoint.name,S.fromCodePoint.length);\n",
            "AB C a3b\ntrue fromCodePoint 1\n",
            false,
        },
        new object[]
        {
            "object_alias",
            "const O:any=Object;const value={x:1,y:2};console.log(O.keys(value).join(\",\"),O.values(value).join(\",\"),O.entries(value).length);console.log(O.is(NaN,NaN),O.hasOwn(value,\"x\"),O.assign({},value).y);\n",
            "x,y 1,2 2\ntrue true 2\n",
            false,
        },
        new object[]
        {
            "object_state",
            "const O:any=Object;const value=O.freeze({x:2});console.log(O.isFrozen(value),O.isSealed(value),O.isExtensible(value),O.getOwnPropertyNames(value).join(\",\"));\n",
            "true true false x\n",
            false,
        },
        new object[]
        {
            "symbol_alias",
            "const S:any=Symbol;const value=S.for(\"static-probe\");console.log(S.keyFor(value),value===Symbol.for(\"static-probe\"),S.for===Symbol.for,S.keyFor.length);\n",
            "static-probe true true 1\n",
            false,
        },
        new object[]
        {
            "bigint_alias",
            "const B:any=BigInt;console.log(String(B.asIntN(8,255n)),String(B.asUintN(8,-1n)),B.asIntN===BigInt.asIntN,B.asUintN.length);\n",
            "-1 255 true 2\n",
            false,
        },
        new object[]
        {
            "date_alias",
            "const D:any=Date;console.log(D.UTC(2000,0,1),D.parse(\"2000-01-01T00:00:00.000Z\"),D.UTC===Date.UTC,D.UTC.length,D.now.name);\n",
            "946684800000 946684800000 true 7 now\n",
            false,
        },
        new object[]
        {
            "promise_alias",
            "const P:any=Promise;console.log(P.resolve===Promise.resolve,P.all===Promise.all,P.resolve.name,P.resolve.length);P.all([P.resolve(2),3]).then((values:any[])=>console.log(values.join(\",\")));\n",
            "true true resolve 1\n2,3\n",
            false,
        },
        new object[]
        {
            "missing_member",
            "const A:any=Array;const N:any=Number;const S:any=String;console.log(A.missing===undefined,N.missing===undefined,S.missing===undefined);\n",
            "true true true\n",
            false,
        },
        new object[]
        {
            "hosted_required",
            "export function check(value:any){const A:any=Array;return A.isArray(value);}\n",
            "",
            true,
        },
        new object[]
        {
            "hosted_optional",
            "export function check(value:bigint){const B:any=BigInt;const D:any=Date;return String(B.asIntN(8,value))+\":\"+D.UTC(2000,0,1);}\n",
            "",
            true,
        },
        new object[]
        {
            "number_call_control",
            "const N:any=Number;console.log(N.isNaN(NaN),N.isFinite(3),N.isInteger(3),N.isSafeInteger(3),N.isInteger(3.5));\n",
            "true true true true false\n",
            false,
        },
    ];

    [Theory]
    [MemberData(nameof(BuiltInStaticDispatchPrograms))]
    public void Isolated_BuiltInStaticDispatch_PreservesValuesIdentityAndDeployment(
        string name, string source, string expected, bool hosted)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"builtin_static_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }


    public static IEnumerable<object[]> CancellationPrograms =>
    [
        new object[] { "for_loop", "let sum:number=0;for(let i:number=0;i<6;i++){sum+=i;}console.log(sum);\n", "15\n", false },
        new object[] { "while_do", "let x=0;while(x<3){x++;}do{x--;}while(x>1);console.log(x);\n", "1\n", false },
        new object[] { "for_of", "let sum=0;for(const value of [2,3,4]){sum+=value;}console.log(sum);\n", "9\n", false },
        new object[] { "for_in", "let keys=\"\";for(const key in {a:1,b:2}){keys+=key;}console.log(keys);\n", "ab\n", false },
        new object[] { "generator", "function* values():Generator<number>{for(let i=0;i<3;i++){yield i;}}let sum=0;for(const v of values()){sum+=v;}console.log(sum);\n", "3\n", false },
        new object[] { "async_loop", "async function sum(){let x=0;for(const v of [2,3]){x+=await Promise.resolve(v);}return x;}sum().then(v=>console.log(v));\n", "5\n", false },
        new object[] { "finally_loop", "let n=0;try{for(let i=0;i<3;i++){n+=i;}}finally{console.log(\"finally\",n);}\n", "finally 3\n", false },
        new object[] { "numeric_accumulator", "function sum(n:number):number{let value:number=0;for(let i:number=0;i<n;i++){value+=i;}return value;}console.log(sum(100));\n", "4950\n", false },
        new object[] { "hosted_required", "export function sum(n:number):number{let total=0;for(let i=0;i<n;i++){total+=i;}return total;}\n", "", true },
        new object[] { "hosted_optional", "export async function sum(n:number):Promise<number>{let total=0;for(let i=0;i<n;i++){total+=await Promise.resolve(i);}return total;}\n", "", true },
    ];

    [Theory]
    [MemberData(nameof(CancellationPrograms))]
    public void Isolated_CancellationMetadata_PreservesLoopsAndDeployment(
        string name, string source, string expected, bool hosted)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"cancellation_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }


    public static IEnumerable<object[]> ClassInitializationPrograms =>
    [
        new object[] { "declaration_order", "console.log(\"before\");class C{static first=1;static {console.log(\"init\",C.first);}static second=2;}console.log(\"after\",C.second);\n", "before\ninit 1\nafter 2\n", false },
        new object[] { "class_expression", "console.log(\"before\");const C=class{static value=3;static {console.log(\"expression\");}};console.log(\"after\",C.value);\n", "before\nexpression\nafter 3\n", false },
        new object[] { "error_identity", "const marker=new Error(\"marker\");function fail(){throw marker;}function define(){class C{static value=fail();}}try{define();}catch(e){console.log(e===marker,e.message);}\n", "true marker\n", false },
        new object[] { "inheritance", "class A{static value=2;static {console.log(\"parent\");}}class B extends A{static other=3;static {console.log(\"child\");}}console.log(B.value,B.other);\n", "parent\nchild\n2 3\n", false },
        new object[] { "generator_declaration", "function* run():Generator<number>{class C{static value=3;}yield C.value;}console.log(run().next().value);\n", "3\n", false },
        new object[] { "generator_expression", "function* run():Generator<number>{const C=class{static value=4;};yield C.value;}console.log(run().next().value);\n", "4\n", false },
        new object[] { "async_expression", "async function run(){await Promise.resolve(0);const C=class{static value=6;};return C.value;}run().then(v=>console.log(v));\n", "6\n", false },
        new object[] { "hosted_required", "export function run(){class C{static value=7;}return C.value;}\n", "", true },
        new object[] { "hosted_optional", "export async function run(){await Promise.resolve(0);const C=class{static value=8;};return C.value;}\n", "", true },
        new object[] { "async_control", "async function run(){await Promise.resolve(0);return 5;}run().then(v=>console.log(v));\n", "5\n", false },
    ];

    [Theory]
    [MemberData(nameof(ClassInitializationPrograms))]
    public void Isolated_ClassInitialization_PreservesEvaluationAndDeployment(
        string name, string source, string expected, bool hosted)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"class_initialization_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }

    public static IEnumerable<object[]> RegexLiteralCachePrograms =>
    [
        new object[] { "empty", "console.log(1);\n", "1\n", false },
        new object[] { "plain", "function run(){return /a/.test(\"ba\");}console.log(run(),run());\n", "true true\n", false },
        new object[] { "exec", "function run(){const value=/a/.exec(\"ba\");return value===null?\"none\":value[0];}console.log(run(),run());\n", "a a\n", false },
        new object[] { "stateful", "function global(){return /a/g.test(\"a\");}function sticky(){return /a/y.test(\"a\");}console.log(global(),global(),sticky(),sticky());\n", "true true true true\n", false },
        new object[] { "string_consumers", "for(let i=0;i<2;i++){console.log(\"a1b2\".replace(/\\d/g,\"#\"),\"a,b\".split(/,/).length,\"cat\".search(/a/),(\"a1a\".match(/a/g)||[]).length);}\n", "a#b# 2 1 2\na#b# 2 1 2\n", false },
        new object[] { "escaping", "function run(){return /a/.test(\"a\");}const a=/a/;a.lastIndex=7;const b=/a/;console.log(run(),a.lastIndex,a===b,b.lastIndex);\n", "true 7 false 0\n", false },
        new object[] { "distinct_sites", "function first(){return /a/.test(\"a\");}function second(){return /a/.test(\"b\");}console.log(first(),second(),first());\n", "true false true\n", false },
        new object[] { "async_function", "async function run(){await Promise.resolve(1);return /a/.test(\"a\");}run().then(v=>console.log(v));\n", "true\n", false },
        new object[] { "async_arrow", "const run=async()=>{await Promise.resolve(1);return \"a1\".replace(/\\d/g,\"#\");};run().then(v=>console.log(v));\n", "a#\n", false },
        new object[] { "generator", "function* run():Generator<boolean>{yield /a/.test(\"a\");yield /b/.test(\"b\");}const g=run();console.log(g.next().value,g.next().value);\n", "true true\n", false },
        new object[] { "async_generator", "async function* run(){yield /a/.test(\"a\");yield /b/.test(\"b\");}async function main(){const g=run();console.log((await g.next()).value,(await g.next()).value);}main();\n", "true true\n", false },
        new object[] { "prototype_mutation", "const prototype:any=RegExp.prototype;prototype.test=function(s:any){return false;};function run(){return /a/.test(\"a\");}console.log(run(),run());\n", "false false\n", false },
        new object[] { "lazy_branch", "function unused(){return /a/.test(\"a\");}console.log(\"before\");if(false){unused();}console.log(\"after\");\n", "before\nafter\n", false },
        new object[] { "hosted_required", "export function run(){return /a/.test(\"a\");}\n", "", true },
        new object[] { "hosted_optional", "export async function run(){await Promise.resolve(1);return /a/.test(\"a\");}\n", "", true },
    ];

    [Theory]
    [MemberData(nameof(RegexLiteralCachePrograms))]
    public void Isolated_RegexLiteralCache_PreservesIdentityAndDeployment(
        string name, string source, string expected, bool hosted)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"regex_literal_cache_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }

    public static IEnumerable<object[]> RuntimeClassPrograms =>
    [
        new object[] { "plain", "console.log(1);\n", "1\n", false },
        new object[] { "array_from", "const values=Array.from({0:3,1:4,length:2},(x,i)=>x+i);console.log(values.join(\",\"));\n", "3,5\n", false },
        new object[] { "array_from_iterator", "function* values(){yield 2;yield 5;}console.log(Array.from(values(),(x,i)=>x+i).join(\",\"));\n", "2,6\n", false },
        new object[] { "array_destructure", "function* values(){yield 2;yield 4;yield 6;}const [first,...rest]=values();console.log(first,rest.join(\",\"));\n", "2 4,6\n", false },
        new object[] { "for_of", "function* values(){yield 2;yield 3;}let sum=0;for(const value of values()){sum+=value;}console.log(sum);\n", "5\n", false },
        new object[] { "spread", "function total(a:number,b:number,c:number){return a+b+c;}const values=[2,3,4];console.log([...values].join(\",\"),total(...values as [number,number,number]));\n", "2,3,4 9\n", false },
        new object[] { "yield_delegate", "function* values(){yield* [2,3];}const g=values();console.log(g.next().value,g.next().value,g.next().done);\n", "2 3 true\n", false },
        new object[] { "promise_executor", "new Promise<number>((resolve,reject)=>{resolve(7);}).then(v=>console.log(v));\n", "7\n", false },
        new object[] { "object_fields", "class Value{value=7;getValue(){return this.value;}}const v=new Value();const o={value:3,getValue(){return this.value;}};console.log(v.getValue(),o.getValue());\n", "7 3\n", false },
        new object[] { "async_iterator", "async function* values(){yield 2;yield 4;}async function run(){let sum=0;for await(const v of values()){sum+=v;}return sum;}run().then(v=>console.log(v));\n", "6\n", false },
        new object[] { "readable_stream", "const stream=new ReadableStream({start(controller){controller.enqueue(7);controller.close();}});const reader=stream.getReader();reader.read().then(result=>console.log(result.value,result.done));\n", "7 false\n", false },
        new object[] { "hosted_required", "export function run(){return Array.from({0:3,length:1},(x,i)=>x+i);}\n", "", true },
        new object[] { "hosted_optional", "export async function run(){await Promise.resolve(0);return Array.from([2,3],(x,i)=>x+i);}\n", "", true },
        new object[] { "object_group_by_value", "const O:any=Object;try{const groups=O.groupBy([1,2,3],(x:number)=>x%2);console.log(groups[1].join(\",\"));}catch(e){console.log(\"failed\");}\n", "1,3\n", false },
        new object[] { "object_group_by_direct", "interface ObjectConstructor{groupBy(items:any,callback:any):any;}const groups=Object.groupBy([1,2,3],(x:number)=>x%2);console.log(groups[1].join(\",\"));\n", "1,3\n", false },
        new object[] { "map_group_by_direct", "interface MapConstructor{groupBy(items:any,callback:any):any;}const groups=Map.groupBy([1,2,3],(x:number)=>x%2);console.log(groups.get(0).join(\",\"));\n", "2\n", false },
    ];

    [Theory]
    [MemberData(nameof(RuntimeClassPrograms))]
    public void Isolated_RuntimeClass_PreservesCrossFamilyCallsAndDeployment(
        string name, string source, string expected, bool hosted)
    {
        using var tempDir = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = tempDir.CreateFile("main.ts", source);
        var dllPath = tempDir.GetPath($"runtime_class_{name}.dll");
        var hosting = hosted ? " --target dll --hosted" : "";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{sourcePath}\" -o \"{dllPath}\" --verify --standalone{hosting}", tempDir.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(dllPath);
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        Assert.False(File.Exists(tempDir.GetPath("SharpTS.dll")));
        if (!hosted)
            Assert.Equal(expected, ExecuteCompiledDllIsolated(dllPath, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }

}
