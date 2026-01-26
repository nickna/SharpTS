/**
 * Bridge client for communication with the SharpTS LSP bridge process.
 * Handles process spawning, stdin/stdout communication, and auto-restart.
 */

import * as cp from 'child_process';
import * as readline from 'readline';
import * as vscode from 'vscode';
import { BridgeRequest, BridgeResponse } from './BridgeProtocol';

interface PendingRequest {
    resolve: (value: BridgeResponse) => void;
    reject: (error: Error) => void;
    timeout: NodeJS.Timeout;
}

export class BridgeClient implements vscode.Disposable {
    private process: cp.ChildProcess | null = null;
    private rl: readline.Interface | null = null;
    private seq = 0;
    private pendingRequests = new Map<number, PendingRequest>();
    private restartAttempts: number[] = [];
    private maxRestartAttempts: number;
    private restartOnCrash: boolean;
    private outputChannel: vscode.OutputChannel;
    private statusBarItem: vscode.StatusBarItem;
    private isReady = false;

    constructor(
        private bridgePath: string,
        private args: string[],
        private config: vscode.WorkspaceConfiguration
    ) {
        this.maxRestartAttempts = config.get('maxRestartAttempts', 3);
        this.restartOnCrash = config.get('restartOnCrash', true);
        this.outputChannel = vscode.window.createOutputChannel('SharpTS Bridge');
        this.statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
        this.statusBarItem.command = 'sharpts.showBridgeStatus';
    }

    async start(): Promise<boolean> {
        try {
            this.updateStatus('starting');
            this.outputChannel.appendLine(`[Info] Starting bridge...`);
            this.outputChannel.appendLine(`[Info] Bridge path: ${this.bridgePath}`);
            this.outputChannel.appendLine(`[Info] Args: ${this.args.join(' ')}`);

            // Run the bundled bridge DLL
            const spawnArgs = [this.bridgePath, 'lsp-bridge', ...this.args];

            this.process = cp.spawn('dotnet', spawnArgs, {
                stdio: ['pipe', 'pipe', 'pipe']
            });

            this.process.on('error', (err) => {
                this.outputChannel.appendLine(`[Error] Process error: ${err.message}`);
                this.handleProcessExit(-1);
            });

            this.process.on('exit', (code) => {
                this.outputChannel.appendLine(`[Info] Process exited with code: ${code}`);
                this.handleProcessExit(code ?? -1);
            });

            // Read stderr for diagnostics
            if (this.process.stderr) {
                this.process.stderr.on('data', (data: Buffer) => {
                    this.outputChannel.appendLine(`[stderr] ${data.toString().trim()}`);
                });
            }

            // Set up line reader for stdout
            if (!this.process.stdout) {
                throw new Error('Failed to get stdout from bridge process');
            }

            this.rl = readline.createInterface({
                input: this.process.stdout,
                crlfDelay: Infinity
            });

            this.rl.on('line', (line) => this.handleResponse(line));

            // Wait for ready signal
            const ready = await this.waitForReady();
            if (ready) {
                this.isReady = true;
                this.updateStatus('ready');
                this.outputChannel.appendLine('[Info] Bridge is ready');
                return true;
            }

            this.outputChannel.appendLine('[Error] Bridge did not send ready signal');
            return false;
        } catch (err) {
            this.outputChannel.appendLine(`[Error] Failed to start: ${err}`);
            this.updateStatus('error');
            return false;
        }
    }

    private async waitForReady(): Promise<boolean> {
        return new Promise((resolve) => {
            const timeout = setTimeout(() => {
                this.outputChannel.appendLine('[Error] Timeout waiting for ready signal');
                resolve(false);
            }, 30000);

            const onLine = (line: string) => {
                try {
                    const response = JSON.parse(line) as BridgeResponse;
                    if (response.seq === 0 && response.success &&
                        (response.body as { ready?: boolean })?.ready) {
                        clearTimeout(timeout);
                        this.rl?.removeListener('line', onLine);
                        resolve(true);
                    }
                } catch {
                    // Ignore parse errors during startup
                }
            };

            this.rl?.on('line', onLine);
        });
    }

    async sendRequest<T>(command: string, args?: Record<string, unknown>): Promise<BridgeResponse<T>> {
        if (!this.process || !this.process.stdin || !this.isReady) {
            throw new Error('Bridge not started or not ready');
        }

        const seq = ++this.seq;
        const request: BridgeRequest = { seq, command, arguments: args };

        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => {
                this.pendingRequests.delete(seq);
                reject(new Error(`Request timeout: ${command}`));
            }, 30000);

            this.pendingRequests.set(seq, {
                resolve: resolve as (value: BridgeResponse) => void,
                reject,
                timeout
            });

            const json = JSON.stringify(request);
            this.outputChannel.appendLine(`[Request] ${json}`);
            this.process!.stdin!.write(json + '\n');
        });
    }

    private handleResponse(line: string): void {
        try {
            this.outputChannel.appendLine(`[Response] ${line}`);
            const response = JSON.parse(line) as BridgeResponse;
            const pending = this.pendingRequests.get(response.seq);

            if (pending) {
                clearTimeout(pending.timeout);
                this.pendingRequests.delete(response.seq);

                if (response.success) {
                    pending.resolve(response);
                } else {
                    pending.reject(new Error(response.message || 'Unknown error'));
                }
            }
        } catch (err) {
            this.outputChannel.appendLine(`[Error] Failed to parse response: ${err}`);
        }
    }

    private handleProcessExit(code: number): void {
        this.isReady = false;
        this.process = null;
        this.rl?.close();
        this.rl = null;

        // Reject all pending requests
        for (const [seq, pending] of this.pendingRequests) {
            clearTimeout(pending.timeout);
            pending.reject(new Error('Bridge process exited'));
        }
        this.pendingRequests.clear();

        if (!this.restartOnCrash) {
            this.updateStatus('stopped');
            return;
        }

        // Check restart limits
        const now = Date.now();
        this.restartAttempts = this.restartAttempts.filter(t => now - t < 60000);

        if (this.restartAttempts.length >= this.maxRestartAttempts) {
            this.updateStatus('error');
            vscode.window.showErrorMessage(
                'SharpTS bridge crashed too many times. Click to restart.',
                'Restart'
            ).then(choice => {
                if (choice === 'Restart') {
                    this.restartAttempts = [];
                    this.start();
                }
            });
            return;
        }

        this.restartAttempts.push(now);
        this.updateStatus('restarting');

        setTimeout(() => this.start(), 1000);
    }

    private updateStatus(status: 'starting' | 'ready' | 'restarting' | 'error' | 'stopped'): void {
        const icons: Record<string, string> = {
            starting: '$(sync~spin)',
            ready: '$(check)',
            restarting: '$(sync~spin)',
            error: '$(error)',
            stopped: '$(circle-slash)'
        };

        this.statusBarItem.text = `${icons[status]} SharpTS`;
        this.statusBarItem.tooltip = `SharpTS Bridge: ${status}`;
        this.statusBarItem.show();
    }

    get ready(): boolean {
        return this.isReady;
    }

    showOutput(): void {
        this.outputChannel.show();
    }

    dispose(): void {
        if (this.process) {
            // Send shutdown command before killing
            try {
                const request: BridgeRequest = { seq: ++this.seq, command: 'shutdown' };
                this.process.stdin?.write(JSON.stringify(request) + '\n');
            } catch {
                // Ignore errors during shutdown
            }

            setTimeout(() => {
                this.process?.kill();
            }, 1000);
        }
        this.rl?.close();
        this.outputChannel.dispose();
        this.statusBarItem.dispose();
    }
}
