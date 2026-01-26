/**
 * SharpTS VS Code Extension
 * Provides .NET attribute support for TypeScript decorators.
 */

import * as vscode from 'vscode';
import * as path from 'path';
import { BridgeClient } from './bridge/BridgeClient';
import { BridgeCache } from './bridge/BridgeCache';
import { SharpTSCompletionProvider } from './features/CompletionProvider';
import { SharpTSSignatureProvider } from './features/SignatureProvider';
import { SharpTSHoverProvider } from './features/HoverProvider';
import { DeclarationGenerator } from './features/DeclarationGenerator';
import { CompileCommands } from './commands/CompileCommands';

let bridgeClient: BridgeClient | undefined;
let cache: BridgeCache;

export async function activate(context: vscode.ExtensionContext) {
    const config = vscode.workspace.getConfiguration('sharpts');
    cache = new BridgeCache();

    // Resolve path to bundled bridge DLL
    const bridgePath = path.join(context.extensionPath, 'bin', 'bridge', 'SharpTS.dll');

    // Verify bridge exists
    try {
        await vscode.workspace.fs.stat(vscode.Uri.file(bridgePath));
    } catch {
        vscode.window.showErrorMessage(
            'SharpTS bridge not found. The extension may need to be reinstalled.'
        );
        return;
    }

    // Build bridge arguments
    const args: string[] = [];

    const projectFile = config.get<string>('projectFile');
    if (projectFile) {
        args.push('--project', projectFile);
    }

    const additionalRefs = config.get<string[]>('additionalReferences', []);
    for (const ref of additionalRefs) {
        args.push('-r', ref);
    }

    // Create and start bridge
    bridgeClient = new BridgeClient(bridgePath, args, config);

    const started = await bridgeClient.start();
    if (!started) {
        vscode.window.showWarningMessage(
            'Failed to start SharpTS bridge. Some features may be unavailable. Click "Show Output" for details.',
            'Show Output'
        ).then(choice => {
            if (choice === 'Show Output') {
                bridgeClient?.showOutput();
            }
        });
    }

    // Generate TypeScript declarations for annotations
    if (started && vscode.workspace.workspaceFolders?.length) {
        const generator = new DeclarationGenerator(bridgeClient);
        for (const folder of vscode.workspace.workspaceFolders) {
            try {
                await generator.generate(folder);
            } catch (err) {
                // Log but don't fail activation
                console.error('Failed to generate declarations:', err);
            }
        }
    }

    // Register providers
    const selector: vscode.DocumentSelector = [
        { language: 'typescript', scheme: 'file' },
        { language: 'typescriptreact', scheme: 'file' }
    ];

    context.subscriptions.push(
        vscode.languages.registerCompletionItemProvider(
            selector,
            new SharpTSCompletionProvider(bridgeClient, cache),
            '@'
        ),
        vscode.languages.registerSignatureHelpProvider(
            selector,
            new SharpTSSignatureProvider(bridgeClient, cache),
            '(', ','
        ),
        vscode.languages.registerHoverProvider(
            selector,
            new SharpTSHoverProvider(bridgeClient, cache)
        )
    );

    // Register commands
    context.subscriptions.push(
        vscode.commands.registerCommand('sharpts.restartBridge', async () => {
            cache.invalidate();
            if (bridgeClient) {
                bridgeClient.dispose();
                bridgeClient = new BridgeClient(bridgePath, args, config);
                await bridgeClient.start();
            }
        }),
        vscode.commands.registerCommand('sharpts.showBridgeStatus', () => {
            if (bridgeClient?.ready) {
                vscode.window.showInformationMessage('SharpTS bridge is running and ready.');
            } else {
                vscode.window.showWarningMessage(
                    'SharpTS bridge is not running.',
                    'Restart'
                ).then(choice => {
                    if (choice === 'Restart') {
                        vscode.commands.executeCommand('sharpts.restartBridge');
                    }
                });
            }
            bridgeClient?.showOutput();
        })
    );

    // Register compile/run commands
    const compileCommands = new CompileCommands(bridgePath);
    context.subscriptions.push(
        vscode.commands.registerCommand('sharpts.compile', () => compileCommands.compile()),
        vscode.commands.registerCommand('sharpts.run', () => compileCommands.run()),
        vscode.commands.registerCommand('sharpts.compileAndRun', () => compileCommands.compileAndRun()),
        { dispose: () => compileCommands.dispose() }
    );

    // Watch for configuration changes
    context.subscriptions.push(
        vscode.workspace.onDidChangeConfiguration(e => {
            if (e.affectsConfiguration('sharpts')) {
                cache.invalidate();
                vscode.window.showInformationMessage(
                    'SharpTS configuration changed. Restart the bridge for changes to take effect.',
                    'Restart Bridge'
                ).then(choice => {
                    if (choice === 'Restart Bridge') {
                        vscode.commands.executeCommand('sharpts.restartBridge');
                    }
                });
            }
        })
    );

    context.subscriptions.push({
        dispose: () => bridgeClient?.dispose()
    });
}

export function deactivate() {
    bridgeClient?.dispose();
}
