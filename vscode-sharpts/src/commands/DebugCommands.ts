/**
 * "SharpTS: Debug Current File" — compiles the active TypeScript file with debug symbols and
 * launches it under the installed .NET debug adapter.
 *
 * SharpTS deliberately ships no debug adapter of its own. A compiled program is an ordinary .NET
 * assembly, and `--debug` gives it a portable PDB whose documents and sequence points refer to the
 * original `.ts` files, so the `coreclr` adapter from the C# extension can bind breakpoints in
 * TypeScript without any SharpTS-specific protocol.
 */

import * as vscode from 'vscode';
import * as path from 'path';

/** Extension that supplies the `coreclr` debug adapter. */
const CSHARP_EXTENSION_ID = 'ms-dotnettools.csharp';

export class DebugCommands {
    constructor(private compile: (extraArgs: string[]) => Promise<string | undefined>) {}

    async debugCurrentFile(): Promise<void> {
        const editor = vscode.window.activeTextEditor;
        if (!editor) {
            vscode.window.showWarningMessage('No active editor');
            return;
        }
        if (!editor.document.uri.fsPath.endsWith('.ts')) {
            vscode.window.showWarningMessage('Active file is not a TypeScript file');
            return;
        }

        if (!(await this.ensureDebugAdapter())) return;

        // Compile the file as it exists on disk. Saving first means the PDB's checksums describe
        // the text the editor is showing, so the debugger will not refuse to bind breakpoints
        // against a file it considers stale.
        if (editor.document.isDirty && !(await editor.document.save())) {
            vscode.window.showErrorMessage('Could not save the file; nothing was compiled.');
            return;
        }

        const assemblyPath = await this.compile(['-g']);
        if (!assemblyPath) return;

        const folder = vscode.workspace.getWorkspaceFolder(editor.document.uri);

        await vscode.debug.startDebugging(folder, {
            type: 'coreclr',
            request: 'launch',
            name: `SharpTS: ${path.basename(editor.document.uri.fsPath)}`,
            program: assemblyPath,
            // The assembly is written beside its source, where the compiler also puts the
            // runtimeconfig and any co-located dependencies, and where imported modules resolve
            // from. Launching there keeps relative paths in the program working.
            cwd: path.dirname(assemblyPath),
            console: 'internalConsole',
            stopAtEntry: false,
        });
    }

    /**
     * Confirms the .NET debug adapter is available, since it comes from a separate extension and
     * `startDebugging` fails with an unhelpful error when it is missing.
     */
    private async ensureDebugAdapter(): Promise<boolean> {
        if (vscode.extensions.getExtension(CSHARP_EXTENSION_ID)) return true;

        const install = 'Install C# extension';
        const choice = await vscode.window.showErrorMessage(
            'Debugging compiled TypeScript needs the C# extension, which supplies the .NET debug adapter.',
            install,
        );

        if (choice === install) {
            await vscode.commands.executeCommand('workbench.extensions.installExtension', CSHARP_EXTENSION_ID);
        }
        return false;
    }
}
