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

let bridgeClient: BridgeClient | undefined;
let cache: BridgeCache;

export async function activate(context: vscode.ExtensionContext) {
    const config = vscode.workspace.getConfiguration('sharpts');
    cache = new BridgeCache();

    // Get configuration
    const execPath = config.get<string>('executablePath', 'dotnet');
    let projectPath = config.get<string>('projectPath', '');

    // If no project path specified, try to find SharpTS in common locations
    if (!projectPath) {
        // Try to find relative to extension
        const extensionPath = context.extensionPath;
        const parentDir = path.dirname(extensionPath);

        // Common relative paths to check
        const possiblePaths = [
            path.join(parentDir, 'SharpTS'),
            path.join(parentDir, '..', 'SharpTS'),
            path.join(extensionPath, '..'),
        ];

        for (const p of possiblePaths) {
            const csprojPath = path.join(p, 'SharpTS.csproj');
            try {
                await vscode.workspace.fs.stat(vscode.Uri.file(csprojPath));
                projectPath = p;
                break;
            } catch {
                // Path doesn't exist, try next
            }
        }
    }

    if (!projectPath) {
        vscode.window.showWarningMessage(
            'SharpTS project path not configured. Please set sharpts.projectPath in settings.'
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
    bridgeClient = new BridgeClient(execPath, projectPath, args, config);

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
                bridgeClient = new BridgeClient(execPath, projectPath, args, config);
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
