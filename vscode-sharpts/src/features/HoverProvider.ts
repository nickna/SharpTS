/**
 * Hover provider for SharpTS decorator documentation.
 * Shows XML documentation when hovering over decorators.
 */

import * as vscode from 'vscode';
import { BridgeClient } from '../bridge/BridgeClient';
import { BridgeCache } from '../bridge/BridgeCache';
import { DocumentationResult } from '../bridge/BridgeProtocol';

export class SharpTSHoverProvider implements vscode.HoverProvider {
    constructor(
        private bridge: BridgeClient,
        private cache: BridgeCache
    ) {}

    async provideHover(
        document: vscode.TextDocument,
        position: vscode.Position,
        token: vscode.CancellationToken
    ): Promise<vscode.Hover | undefined> {
        if (!this.bridge.ready) {
            return undefined;
        }

        // Check if we're on a decorator
        const range = document.getWordRangeAtPosition(position, /@[\w.]+/);
        if (!range) {
            return undefined;
        }

        const text = document.getText(range);
        if (!text.startsWith('@')) {
            return undefined;
        }

        // Extract decorator name (remove @ and any trailing parentheses/arguments)
        const decoratorName = text.substring(1).replace(/\(.*$/, '');

        try {
            const cacheKey = `doc:${decoratorName}`;
            let docResult = this.cache.get<DocumentationResult>(cacheKey);

            if (!docResult) {
                const response = await this.bridge.sendRequest<DocumentationResult>(
                    'get-type-documentation',
                    { typeName: decoratorName }
                );

                if (!response.success || !response.body) {
                    return undefined;
                }

                docResult = response.body;
                this.cache.set(cacheKey, docResult);
            }

            const content = new vscode.MarkdownString();
            content.appendCodeblock(docResult.fullName, 'csharp');

            if (docResult.documentation) {
                content.appendText('\n\n');
                content.appendMarkdown(docResult.documentation);
            }

            return new vscode.Hover(content, range);
        } catch {
            return undefined;
        }
    }
}
