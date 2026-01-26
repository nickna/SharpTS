/**
 * Compile and run commands for SharpTS.
 * Spawns dotnet process directly (not through bridge) since these are build operations.
 */

import * as vscode from 'vscode';
import * as cp from 'child_process';
import * as path from 'path';

export class CompileCommands {
    private outputChannel: vscode.OutputChannel;

    constructor(private sharptsPath: string) {
        this.outputChannel = vscode.window.createOutputChannel('SharpTS');
    }

    /**
     * Compile current file to .dll
     */
    async compile(): Promise<string | undefined> {
        const file = this.getActiveTypeScriptFile();
        if (!file) return undefined;

        this.outputChannel.clear();
        this.outputChannel.show();
        this.outputChannel.appendLine(`[Compile] Compiling ${path.basename(file)}...`);

        const outputPath = file.replace(/\.ts$/, '.dll');

        try {
            await this.runSharpTS(['--compile', file, '-o', outputPath]);
            this.outputChannel.appendLine(`[Compile] Success: ${outputPath}`);
            return outputPath;
        } catch (err) {
            this.outputChannel.appendLine(`[Error] ${err}`);
            vscode.window.showErrorMessage('Compilation failed. See output for details.');
            return undefined;
        }
    }

    /**
     * Run current file (interpreted)
     */
    async run(): Promise<void> {
        const file = this.getActiveTypeScriptFile();
        if (!file) return;

        this.outputChannel.clear();
        this.outputChannel.show();
        this.outputChannel.appendLine(`[Run] Running ${path.basename(file)}...`);
        this.outputChannel.appendLine('');

        try {
            await this.runSharpTS([file]);
            this.outputChannel.appendLine('');
            this.outputChannel.appendLine('[Run] Completed');
        } catch (err) {
            this.outputChannel.appendLine(`[Error] ${err}`);
        }
    }

    /**
     * Compile then run
     */
    async compileAndRun(): Promise<void> {
        const dllPath = await this.compile();
        if (!dllPath) return;

        this.outputChannel.appendLine('');
        this.outputChannel.appendLine(`[Run] Executing ${path.basename(dllPath)}...`);
        this.outputChannel.appendLine('');

        try {
            await this.runDotnet([dllPath]);
            this.outputChannel.appendLine('');
            this.outputChannel.appendLine('[Run] Completed');
        } catch (err) {
            this.outputChannel.appendLine(`[Error] ${err}`);
        }
    }

    private getActiveTypeScriptFile(): string | undefined {
        const editor = vscode.window.activeTextEditor;
        if (!editor) {
            vscode.window.showWarningMessage('No active editor');
            return undefined;
        }

        const file = editor.document.uri.fsPath;
        if (!file.endsWith('.ts')) {
            vscode.window.showWarningMessage('Active file is not a TypeScript file');
            return undefined;
        }

        return file;
    }

    private runSharpTS(args: string[]): Promise<void> {
        return this.runDotnet([this.sharptsPath, ...args]);
    }

    private runDotnet(args: string[]): Promise<void> {
        return new Promise((resolve, reject) => {
            const proc = cp.spawn('dotnet', args, {
                cwd: vscode.workspace.workspaceFolders?.[0]?.uri.fsPath
            });

            proc.stdout.on('data', (data: Buffer) => {
                this.outputChannel.append(data.toString());
            });

            proc.stderr.on('data', (data: Buffer) => {
                this.outputChannel.append(data.toString());
            });

            proc.on('close', (code) => {
                if (code === 0) {
                    resolve();
                } else {
                    reject(new Error(`Process exited with code ${code}`));
                }
            });

            proc.on('error', (err) => {
                reject(err);
            });
        });
    }

    dispose(): void {
        this.outputChannel.dispose();
    }
}
