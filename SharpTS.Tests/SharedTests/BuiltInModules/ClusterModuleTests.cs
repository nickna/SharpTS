using System.Net;
using System.Net.Sockets;
using SharpTS.Runtime.Types;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the Node.js 'cluster' module: multi-process-like patterns using threads.
/// </summary>
/// <remarks>
/// Tests run sequentially because ClusterSingleton is a global singleton
/// and concurrent tests would pollute each other's state.
/// </remarks>
[Collection("ClusterTests")]
public class ClusterModuleTests : IDisposable
{
    public void Dispose()
    {
        // Reset singleton state between tests to prevent cross-test interference
        ClusterSingleton.Instance.Reset();
        // Give worker threads time to fully exit
        Thread.Sleep(50);
    }

    /// <summary>
    /// Fork+port tests spin up worker threads, bind a real OS port, and round-trip a request.
    /// Under load that can take noticeably longer than a plain module run, so they use a higher
    /// timeout than <see cref="TestHarness.DefaultTimeout"/> (issue #747).
    /// </summary>
    private static readonly TimeSpan PortTestTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Allocates a free TCP loopback port for a cluster test to bind. Binds to port 0 to let the
    /// OS assign a free port, then releases it so the guest interpreter can re-bind it. That
    /// release→re-bind handoff has a TOCTOU window, so the candidate is verified bindable once
    /// more before being returned, re-rolling if a racing test claimed it in between.
    /// </summary>
    private static int GetFreePort()
    {
        const int attempts = 5;
        for (int i = 0; i < attempts; i++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            // Confirm the port is still free after the release; if a concurrent test grabbed it
            // in the gap, re-roll rather than hand back a contended port.
            try
            {
                var verify = new TcpListener(IPAddress.Loopback, port);
                verify.Start();
                verify.Stop();
                return port;
            }
            catch (SocketException)
            {
                // Port taken between Stop() and the verify bind — try again.
            }
        }

        // Fall back to a plain OS-assigned port if every verification raced (extremely unlikely).
        var fallback = new TcpListener(IPAddress.Loopback, 0);
        fallback.Start();
        int fallbackPort = ((IPEndPoint)fallback.LocalEndpoint).Port;
        fallback.Stop();
        return fallbackPort;
    }

    #region Import and Basic Properties

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IsPrimary_InMainThread_ReturnsTrue(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as cluster from 'cluster';
                console.log(cluster.isPrimary);
                console.log(cluster.isWorker);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IsMaster_AliasForIsPrimary(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as cluster from 'cluster';
                console.log(cluster.isMaster === cluster.isPrimary);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Import_Named(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { isPrimary, isWorker, isMaster } from 'cluster';
                console.log(isPrimary);
                console.log(isWorker);
                console.log(isMaster);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\nfalse\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SetupPrimary_StoresSettings(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setupPrimary } from 'cluster';
                setupPrimary({ exec: 'worker.ts' });
                console.log('setup done');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("setup done\n", output);
    }

    #endregion

    #region Fork and Worker Lifecycle (dual-mode: compiled fork bridges to interpreted workers, #1171)

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Fork_WorkerExitsSuccessfully(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as cluster from 'cluster';

                if (cluster.isPrimary) {
                    const worker = cluster.fork();
                    worker.on('exit', (code: any) => {
                        console.log('worker exited: ' + code);
                    });
                } else {
                    // Worker exits immediately
                    console.log('worker running');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("worker exited:", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Fork_WorkerSeesIsWorkerTrue(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as cluster from 'cluster';

                if (cluster.isPrimary) {
                    const worker = cluster.fork();
                    worker.on('message', (msg: any) => {
                        console.log('worker isWorker: ' + msg);
                    });
                } else {
                    process.send(cluster.isWorker);
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("worker isWorker: true", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Fork_WorkerToParentMessaging(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as cluster from 'cluster';

                if (cluster.isPrimary) {
                    const worker = cluster.fork();
                    worker.on('message', (msg: any) => {
                        console.log('received: ' + msg);
                    });
                    worker.on('error', (err: any) => {
                        console.log('error: ' + err);
                    });
                } else {
                    process.send('hello from worker');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("received: hello from worker", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Fork_ParentToWorkerMessaging(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as cluster from 'cluster';

                if (cluster.isPrimary) {
                    const worker = cluster.fork();
                    worker.on('online', () => {
                        worker.send('hello from primary');
                    });
                    worker.on('message', (msg: any) => {
                        console.log(msg);
                        worker.kill();
                    });
                } else {
                    process.on('message', (msg: any) => {
                        process.send('echo: ' + msg);
                    });
                    // Keep worker alive briefly to receive messages
                    setTimeout(() => {}, 3000);
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("echo: hello from primary", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Fork_WorkerExitEvent(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as cluster from 'cluster';

                if (cluster.isPrimary) {
                    const worker = cluster.fork();
                    worker.on('exit', (code: any) => {
                        console.log('exit code: ' + code);
                    });
                } else {
                    // Worker exits immediately
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("exit code: 0", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Fork_ClusterWorkersDict(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as cluster from 'cluster';

                if (cluster.isPrimary) {
                    const w1 = cluster.fork();
                    const w2 = cluster.fork();
                    // Verify workers have IDs
                    console.log('w1 id: ' + (typeof w1.id === 'number'));
                    console.log('w2 id: ' + (typeof w2.id === 'number'));
                    console.log('different: ' + (w1.id !== w2.id));
                    w1.on('exit', () => {});
                    w2.on('exit', () => {});
                } else {
                    // Workers exit immediately
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("w1 id: true", output);
        Assert.Contains("w2 id: true", output);
        Assert.Contains("different: true", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Worker_Disconnect(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as cluster from 'cluster';

                if (cluster.isPrimary) {
                    const worker = cluster.fork();
                    worker.on('online', () => {
                        worker.disconnect();
                    });
                    worker.on('disconnect', () => {
                        console.log('disconnected');
                    });
                    worker.on('exit', () => {
                        console.log('exited');
                    });
                } else {
                    // Worker waits
                    setTimeout(() => {}, 3000);
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("disconnected", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Worker_Kill(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as cluster from 'cluster';

                if (cluster.isPrimary) {
                    const worker = cluster.fork();
                    worker.on('online', () => {
                        worker.kill();
                    });
                    worker.on('exit', () => {
                        console.log('worker killed');
                    });
                } else {
                    setTimeout(() => {}, 3000);
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("worker killed", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Cluster_DisconnectAll(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as cluster from 'cluster';

                if (cluster.isPrimary) {
                    const w1 = cluster.fork();
                    const w2 = cluster.fork();
                    let exitCount = 0;

                    const onExit = () => {
                        exitCount++;
                        if (exitCount === 2) {
                            console.log('all exited');
                        }
                    };
                    w1.on('exit', onExit);
                    w2.on('exit', onExit);

                    // Disconnect all after both are online
                    let onlineCount = 0;
                    const onOnline = () => {
                        onlineCount++;
                        if (onlineCount === 2) {
                            cluster.disconnect();
                        }
                    };
                    w1.on('online', onOnline);
                    w2.on('online', onOnline);
                } else {
                    setTimeout(() => {}, 3000);
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("all exited", output);
    }

    #endregion

    #region Cluster Port Sharing

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Fork_WorkersShareNetPort(ExecutionMode mode)
    {
        var port = GetFreePort();
        var files = new Dictionary<string, string>
        {
            // Mutable state and the per-connection helper live at module level:
            // closures created in a loop inside another closure hit a pre-existing
            // compiled closure-capture bug (#1201: each inner closure sees a fresh
            // copy of the enclosing block environment), so the test keeps captures
            // out of that shape — it exercises cluster, not the closure bug.
            ["main.ts"] = $$"""
                import * as cluster from 'cluster';
                import * as net from 'net';

                let readyCount = 0;
                let responseCount = 0;
                let w1: any = null;
                let w2: any = null;

                const connectOnce = () => {
                    const client = net.createConnection({ port: {{port}}, host: '127.0.0.1' });
                    client.setEncoding('utf8');
                    client.on('data', (data: string) => {
                        responseCount++;
                        client.destroy();
                        if (responseCount === 4) {
                            console.log('responses: ' + responseCount);
                            w1.kill();
                            w2.kill();
                        }
                    });
                };

                const checkReady = () => {
                    readyCount++;
                    if (readyCount === 2) {
                        // Both workers are listening, connect multiple times
                        for (let i = 0; i < 4; i++) connectOnce();
                    }
                };

                if (cluster.isPrimary) {
                    w1 = cluster.fork();
                    w2 = cluster.fork();
                    w1.on('message', (msg: any) => {
                        if (msg === 'ready') checkReady();
                    });
                    w2.on('message', (msg: any) => {
                        if (msg === 'ready') checkReady();
                    });
                } else {
                    const server = net.createServer((socket) => {
                        socket.write('worker-' + cluster.worker.id);
                        socket.end();
                    });
                    server.listen({{port}}, () => {
                        process.send('ready');
                    });
                    setTimeout(() => {}, 10000);
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode, PortTestTimeout);
        Assert.Contains("responses: 4", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Fork_WorkerExitReleasesSlot(ExecutionMode mode)
    {
        var port = GetFreePort();
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as cluster from 'cluster';
                import * as net from 'net';

                if (cluster.isPrimary) {
                    const w1 = cluster.fork();
                    const w2 = cluster.fork();
                    let readyCount = 0;

                    const checkReady = () => {
                        readyCount++;
                        if (readyCount === 2) {
                            // Kill w1, then verify w2 still serves
                            w1.kill();
                            setTimeout(() => {
                                const client = net.createConnection({ port: {{port}}, host: '127.0.0.1' });
                                client.setEncoding('utf8');
                                client.on('data', (data: string) => {
                                    console.log('after kill: ' + data);
                                    client.destroy();
                                    w2.kill();
                                });
                            }, 200);
                        }
                    };

                    w1.on('message', (msg: any) => {
                        if (msg === 'ready') checkReady();
                    });
                    w2.on('message', (msg: any) => {
                        if (msg === 'ready') checkReady();
                    });
                } else {
                    const server = net.createServer((socket) => {
                        socket.write('worker-' + cluster.worker.id);
                        socket.end();
                    });
                    server.listen({{port}}, () => {
                        process.send('ready');
                    });
                    setTimeout(() => {}, 10000);
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode, PortTestTimeout);
        Assert.Contains("after kill: worker-", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Fork_LastWorkerExitStopsListener(ExecutionMode mode)
    {
        var port = GetFreePort();
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as cluster from 'cluster';
                import * as net from 'net';

                if (cluster.isPrimary) {
                    const w1 = cluster.fork();

                    w1.on('message', (msg: any) => {
                        if (msg === 'ready') {
                            // First verify the port works
                            const probe = net.createConnection({ port: {{port}}, host: '127.0.0.1' });
                            probe.setEncoding('utf8');
                            probe.on('data', () => {
                                probe.destroy();
                                console.log('port was working');
                                // Now kill the worker
                                w1.kill();
                            });
                        }
                    });

                    w1.on('exit', () => {
                        console.log('worker exited');
                    });
                } else {
                    const server = net.createServer((socket) => {
                        socket.write('hello');
                        socket.end();
                    });
                    server.listen({{port}}, () => {
                        process.send('ready');
                    });
                    setTimeout(() => {}, 10000);
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode, PortTestTimeout);
        Assert.Contains("port was working", output);
        Assert.Contains("worker exited", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Fork_WorkersShareHttpPort(ExecutionMode mode)
    {
        var port = GetFreePort();
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as cluster from 'cluster';
                import * as http from 'http';

                if (cluster.isPrimary) {
                    const w1 = cluster.fork();
                    const w2 = cluster.fork();
                    let readyCount = 0;

                    const checkReady = () => {
                        readyCount++;
                        if (readyCount === 2) {
                            // Both workers listening on same port — send an HTTP request
                            fetch('http://localhost:{{port}}/').then(async (res) => {
                                const text = await res.text();
                                console.log('http response: ' + text);
                                w1.kill();
                                w2.kill();
                            });
                        }
                    };

                    w1.on('message', (msg: any) => {
                        if (msg === 'ready') checkReady();
                    });
                    w2.on('message', (msg: any) => {
                        if (msg === 'ready') checkReady();
                    });
                } else {
                    const server = http.createServer((req: any, res: any) => {
                        res.writeHead(200);
                        res.end('worker-' + cluster.worker.id);
                    });
                    server.listen({{port}}, () => {
                        process.send('ready');
                    });
                    setTimeout(() => {}, 10000);
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode, PortTestTimeout);
        Assert.Contains("http response: worker-", output);
    }

    #endregion

    #region Live State, Events, Worker + Settings Completeness (#1166)

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void LiveWorkers_ReflectedThroughImportedBinding(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as cluster from 'cluster';

                if (cluster.isPrimary) {
                    console.log('before: ' + Object.keys(cluster.workers).length);
                    cluster.on('exit', () => {
                        console.log('after exit: ' + Object.keys(cluster.workers).length);
                    });
                    cluster.fork();
                    console.log('after fork: ' + Object.keys(cluster.workers).length);
                } else {
                    // Worker exits immediately
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("before: 0", output);
        Assert.Contains("after fork: 1", output);
        Assert.Contains("after exit: 0", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Setup_EventFiresOnSetupPrimary(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as cluster from 'cluster';

                cluster.on('setup', (settings: any) => {
                    console.log('setup: silent=' + settings.silent);
                });
                cluster.setupPrimary({ silent: true });
                console.log('done');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("setup: silent=true", output);
        Assert.Contains("done", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Listening_EventFiresWithAddress(ExecutionMode mode)
    {
        var port = GetFreePort();
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as cluster from 'cluster';
                import * as net from 'net';

                if (cluster.isPrimary) {
                    cluster.on('listening', (worker: any, address: any) => {
                        console.log('listening: port=' + address.port + ' type=' + address.addressType);
                        worker.kill();
                    });
                    const w = cluster.fork();
                    w.on('listening', (address: any) => {
                        console.log('worker-level listening: ' + address.port);
                    });
                } else {
                    const server = net.createServer((socket) => { socket.end(); });
                    server.listen({{port}});
                    setTimeout(() => {}, 10000);
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode, PortTestTimeout);
        Assert.Contains($"listening: port={port} type=4", output);
        Assert.Contains($"worker-level listening: {port}", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void WorkerProcess_PidConnectedAndMessageForwarding(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as cluster from 'cluster';

                if (cluster.isPrimary) {
                    const w = cluster.fork();
                    w.process.on('message', (msg: any) => {
                        console.log('via process: ' + msg);
                        console.log('pid number: ' + (typeof w.process.pid === 'number'));
                        console.log('connected: ' + w.process.connected);
                        w.kill();
                    });
                } else {
                    process.send('hi');
                    setTimeout(() => {}, 10000);
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("via process: hi", output);
        Assert.Contains("pid number: true", output);
        Assert.Contains("connected: true", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Worker_DestroyEmitsExitWithSignal(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as cluster from 'cluster';

                if (cluster.isPrimary) {
                    const w = cluster.fork();
                    w.on('online', () => {
                        w.destroy();
                    });
                    w.on('exit', (code: any, signal: any) => {
                        console.log('exit: code=' + code + ' signal=' + signal + ' ead=' + w.exitedAfterDisconnect);
                    });
                } else {
                    setTimeout(() => {}, 10000);
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("exit: code=null signal=SIGTERM ead=true", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Settings_DefaultsAndMergeSemantics(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as cluster from 'cluster';

                cluster.setupPrimary({ args: ['x'], silent: true });
                console.log('args: ' + cluster.settings.args[0]);
                console.log('silent: ' + cluster.settings.silent);
                console.log('serialization: ' + cluster.settings.serialization);
                cluster.setupPrimary({ silent: false });
                console.log('args after merge: ' + cluster.settings.args[0]);
                console.log('silent after merge: ' + cluster.settings.silent);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("args: x", output);
        Assert.Contains("silent: true", output);
        Assert.Contains("serialization: json", output);
        Assert.Contains("args after merge: x", output);
        Assert.Contains("silent after merge: false", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SchedulingPolicy_ConstantsAndRoundTrip(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as cluster from 'cluster';

                console.log('SCHED_NONE: ' + cluster.SCHED_NONE);
                console.log('SCHED_RR: ' + cluster.SCHED_RR);
                cluster.schedulingPolicy = cluster.SCHED_NONE;
                console.log('none round-trips: ' + (cluster.schedulingPolicy === cluster.SCHED_NONE));
                cluster.schedulingPolicy = cluster.SCHED_RR;
                console.log('rr round-trips: ' + (cluster.schedulingPolicy === cluster.SCHED_RR));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("SCHED_NONE: 1", output);
        Assert.Contains("SCHED_RR: 2", output);
        Assert.Contains("none round-trips: true", output);
        Assert.Contains("rr round-trips: true", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void WorkerArgv_HonorsSettingsArgs(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as cluster from 'cluster';

                if (cluster.isPrimary) {
                    cluster.setupPrimary({ args: ['alpha'] });
                    const w = cluster.fork();
                    w.on('message', (msg: any) => {
                        console.log('argv2: ' + msg);
                        w.kill();
                    });
                } else {
                    process.send(process.argv[2]);
                    setTimeout(() => {}, 10000);
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("argv2: alpha", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Silent_SuppressesWorkerConsoleOutput(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as cluster from 'cluster';

                if (cluster.isPrimary) {
                    cluster.setupPrimary({ silent: true });
                    const w = cluster.fork();
                    w.on('message', (msg: any) => {
                        console.log('signal: ' + msg);
                    });
                } else {
                    console.log('WORKER_NOISE');
                    process.send('done');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("signal: done", output);
        Assert.DoesNotContain("WORKER_NOISE", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void RoundRobin_DistributesConnectionsAcrossWorkers(ExecutionMode mode)
    {
        var port = GetFreePort();
        var files = new Dictionary<string, string>
        {
            // Module-level mutable state + connection helper — see Fork_WorkersShareNetPort (#1201).
            ["main.ts"] = $$"""
                import * as cluster from 'cluster';
                import * as net from 'net';

                let readyCount = 0;
                let responses = 0;
                let firstServer = '';
                let distinct = 0;
                let w1: any = null;
                let w2: any = null;

                const connectOnce = () => {
                    const client = net.createConnection({ port: {{port}}, host: '127.0.0.1' });
                    client.setEncoding('utf8');
                    client.on('data', (data: string) => {
                        if (firstServer === '') {
                            firstServer = data;
                            distinct = 1;
                        } else if (data !== firstServer && distinct === 1) {
                            distinct = 2;
                        }
                        responses++;
                        client.destroy();
                        if (responses === 4) {
                            console.log('distinct servers: ' + distinct);
                            w1.kill();
                            w2.kill();
                        }
                    });
                };

                const checkReady = () => {
                    readyCount++;
                    if (readyCount === 2) {
                        for (let i = 0; i < 4; i++) connectOnce();
                    }
                };

                if (cluster.isPrimary) {
                    cluster.schedulingPolicy = cluster.SCHED_RR;
                    w1 = cluster.fork();
                    w2 = cluster.fork();
                    w1.on('message', (msg: any) => { if (msg === 'ready') checkReady(); });
                    w2.on('message', (msg: any) => { if (msg === 'ready') checkReady(); });
                } else {
                    const server = net.createServer((socket) => {
                        socket.write('worker-' + cluster.worker.id);
                        socket.end();
                    });
                    server.listen({{port}}, () => {
                        process.send('ready');
                    });
                    setTimeout(() => {}, 10000);
                }
                """
        };

        // With SCHED_RR and two registered workers, four connections split 2/2 —
        // both workers must observably serve.
        var output = TestHarness.RunModules(files, "main.ts", mode, PortTestTimeout);
        Assert.Contains("distinct servers: 2", output);
    }

    #endregion
}
