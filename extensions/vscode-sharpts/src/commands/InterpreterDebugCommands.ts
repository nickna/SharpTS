import * as path from 'path';
import * as vscode from 'vscode';
import { createInterpreterDebugConfiguration } from './InterpreterDebugConfiguration';

export const INTERPRETER_DEBUG_TYPE = 'sharpts-interpreter';

/** Starts the bundled SharpTS interpreter adapter for the active TypeScript file. */
export class InterpreterDebugCommands {
    async debugCurrentFile(): Promise<void> {
        const editor = vscode.window.activeTextEditor;
        if (!editor) {
            vscode.window.showWarningMessage('No active editor');
            return;
        }
        if (!['.ts', '.tsx', '.mts', '.cts'].includes(path.extname(editor.document.uri.fsPath))) {
            vscode.window.showWarningMessage('Active file is not a TypeScript file');
            return;
        }

        // Breakpoint checksums are computed from the source on disk, so never launch with a
        // dirty editor buffer that the adapter cannot see.
        if (editor.document.isDirty && !(await editor.document.save())) {
            vscode.window.showErrorMessage('Could not save the file; interpreter debugging was not started.');
            return;
        }

        const folder = vscode.workspace.getWorkspaceFolder(editor.document.uri);
        const settings = vscode.workspace.getConfiguration('sharpts', editor.document.uri);
        await vscode.debug.startDebugging(folder, createInterpreterDebugConfiguration(
            editor.document.uri.fsPath,
            folder?.uri.fsPath ?? path.dirname(editor.document.uri.fsPath),
            {
                projectFile: settings.get<string>('projectFile'),
                additionalReferences: settings.get<string[]>('additionalReferences', []),
            },
        ));
    }
}
