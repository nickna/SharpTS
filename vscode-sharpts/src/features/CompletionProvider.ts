/**
 * Completion provider for SharpTS decorator attributes.
 * Triggers on @ character and provides .NET attribute completions.
 */

import * as vscode from 'vscode';
import { BridgeClient } from '../bridge/BridgeClient';
import { BridgeCache } from '../bridge/BridgeCache';
import { AttributeInfo, ListAttributesResult } from '../bridge/BridgeProtocol';

export class SharpTSCompletionProvider implements vscode.CompletionItemProvider {
    constructor(
        private bridge: BridgeClient,
        private cache: BridgeCache
    ) {}

    async provideCompletionItems(
        document: vscode.TextDocument,
        position: vscode.Position,
        token: vscode.CancellationToken
    ): Promise<vscode.CompletionItem[] | undefined> {
        if (!this.bridge.ready) {
            return undefined;
        }

        // Check if we're after an @ symbol
        const line = document.lineAt(position.line).text;
        const textBefore = line.substring(0, position.character);

        const decoratorMatch = textBefore.match(/@(\w*)$/);
        if (!decoratorMatch) {
            return undefined;
        }

        const prefix = decoratorMatch[1];

        try {
            // Try cache first
            let attributes = this.cache.getAttributeList();

            if (!attributes) {
                const response = await this.bridge.sendRequest<ListAttributesResult>(
                    'list-attributes',
                    prefix ? { filter: prefix } : undefined
                );

                if (response.success && response.body) {
                    attributes = response.body.attributes;
                    this.cache.setAttributeList(attributes);
                } else {
                    return undefined;
                }
            }

            // Filter and map to completion items
            return attributes
                .filter(attr =>
                    !prefix ||
                    attr.name.toLowerCase().startsWith(prefix.toLowerCase())
                )
                .map(attr => this.createCompletionItem(attr));
        } catch (err) {
            console.error('Failed to get completions:', err);
            return undefined;
        }
    }

    private createCompletionItem(attr: AttributeInfo): vscode.CompletionItem {
        const item = new vscode.CompletionItem(
            attr.name,
            vscode.CompletionItemKind.Class
        );

        item.detail = attr.fullName;
        item.documentation = new vscode.MarkdownString(
            `**${attr.fullName}**\n\nFrom assembly: ${attr.assembly}`
        );

        // Insert text includes parentheses for factory call
        item.insertText = new vscode.SnippetString(`${attr.name}($0)`);

        // Trigger parameter hints after insertion
        item.command = {
            command: 'editor.action.triggerParameterHints',
            title: 'Trigger Parameter Hints'
        };

        return item;
    }
}
