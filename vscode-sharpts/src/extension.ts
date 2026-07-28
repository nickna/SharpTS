/**
 * SharpTS VS Code extension.
 *
 * Thin client over the SharpTS language server (`sharpts lsp`): all language
 * features (diagnostics, and future hover/completion) come from the server via
 * vscode-languageclient. The extension only spawns the server and keeps the
 * compile/run commands, which are build operations rather than LSP concerns.
 */

import * as path from 'path';
import * as vscode from 'vscode';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    TransportKind
} from 'vscode-languageclient/node';
import { CompileCommands } from './commands/CompileCommands';
import { DebugCommands } from './commands/DebugCommands';

let client: LanguageClient | undefined;

export async function activate(context: vscode.ExtensionContext) {
    const config = vscode.workspace.getConfiguration('sharpts');

    // Bundled binaries (both land in bin/server/: the LSP server publish includes the
    // core SharpTS.dll it references).
    const serverDll = path.join(context.extensionPath, 'bin', 'server', 'SharpTS.LanguageServer.dll');
    const coreDll = path.join(context.extensionPath, 'bin', 'server', 'SharpTS.dll');
    const dotnetPath = config.get<string>('dotnetPath') || 'dotnet';

    // The language server executable *is* the server — no subcommand.
    //
    // Interop-only: in VS Code these files are already served by tsserver, which does ordinary
    // navigation better than SharpTS could and would collide with it. What SharpTS contributes
    // here is the .NET interop surface — diagnostics, hover, completion, signature help — and
    // nothing tsserver already provides. The standalone `sharpts-lsp` tool defaults to full
    // navigation instead, for editors with no TypeScript server of their own.
    const diagnostics = config.get<string>('diagnostics') || 'sharpts-only';
    const args = [
        serverDll,
        '--language-features',
        'interop-only',
        '--diagnostics',
        diagnostics
    ];
    const projectFile = config.get<string>('projectFile');
    if (projectFile) {
        args.push('--project', projectFile);
    }
    for (const ref of config.get<string[]>('additionalReferences', [])) {
        args.push('-r', ref);
    }

    const serverOptions: ServerOptions = {
        run: { command: dotnetPath, args, transport: TransportKind.stdio },
        debug: { command: dotnetPath, args, transport: TransportKind.stdio }
    };

    const clientOptions: LanguageClientOptions = {
        documentSelector: [
            { language: 'typescript', scheme: 'file' },
            { language: 'typescriptreact', scheme: 'file' }
        ],
        synchronize: {
            configurationSection: 'sharpts'
        }
    };

    client = new LanguageClient('sharpts', 'SharpTS Language Server', serverOptions, clientOptions);
    await client.start();

    // Compile / run commands use the core CLI (`SharpTS.dll`), not the LSP server.
    const compileCommands = new CompileCommands(coreDll);
    // Debugging reuses that compile step with `-g`, then hands the assembly to the .NET debug
    // adapter — SharpTS ships no adapter of its own.
    const debugCommands = new DebugCommands((extraArgs) => compileCommands.compile(extraArgs));
    context.subscriptions.push(
        vscode.commands.registerCommand('sharpts.compile', () => compileCommands.compile()),
        vscode.commands.registerCommand('sharpts.debug', () => debugCommands.debugCurrentFile()),
        vscode.commands.registerCommand('sharpts.run', () => compileCommands.run()),
        vscode.commands.registerCommand('sharpts.compileAndRun', () => compileCommands.compileAndRun()),
        vscode.commands.registerCommand('sharpts.restartServer', () => client?.restart()),
        { dispose: () => compileCommands.dispose() }
    );
}

export async function deactivate() {
    await client?.stop();
}
