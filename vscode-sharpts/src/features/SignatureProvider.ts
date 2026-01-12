/**
 * Signature help provider for SharpTS decorator constructors.
 * Shows parameter information when typing inside decorator parentheses.
 */

import * as vscode from 'vscode';
import { BridgeClient } from '../bridge/BridgeClient';
import { BridgeCache } from '../bridge/BridgeCache';
import { AttributeDetailResult } from '../bridge/BridgeProtocol';

export class SharpTSSignatureProvider implements vscode.SignatureHelpProvider {
    constructor(
        private bridge: BridgeClient,
        private cache: BridgeCache
    ) {}

    async provideSignatureHelp(
        document: vscode.TextDocument,
        position: vscode.Position,
        token: vscode.CancellationToken
    ): Promise<vscode.SignatureHelp | undefined> {
        if (!this.bridge.ready) {
            return undefined;
        }

        // Parse to find decorator call context
        const context = this.parseDecoratorContext(document, position);
        if (!context) {
            return undefined;
        }

        const cacheKey = `info:${context.decoratorName}`;
        let info = this.cache.get<AttributeDetailResult>(cacheKey);

        if (!info) {
            try {
                const response = await this.bridge.sendRequest<AttributeDetailResult>(
                    'get-attribute-info',
                    { typeName: context.decoratorName }
                );

                if (!response.success || !response.body) {
                    return undefined;
                }

                info = response.body;
                this.cache.set(cacheKey, info);
            } catch {
                return undefined;
            }
        }

        if (!info.constructors || info.constructors.length === 0) {
            return undefined;
        }

        const help = new vscode.SignatureHelp();
        help.activeParameter = context.parameterIndex;

        // Find the best matching signature based on parameter count
        let bestSignatureIndex = 0;
        for (let i = 0; i < info.constructors.length; i++) {
            const ctor = info.constructors[i];
            if (context.parameterIndex < ctor.parameters.length) {
                bestSignatureIndex = i;
                break;
            }
        }
        help.activeSignature = bestSignatureIndex;

        // Create signature for each constructor
        help.signatures = info.constructors.map(ctor => {
            const params = ctor.parameters.map(p => {
                const optional = p.isOptional ? '?' : '';
                const defaultVal = p.defaultValue ? ` = ${p.defaultValue}` : '';
                return new vscode.ParameterInformation(
                    `${p.name}${optional}: ${p.type}${defaultVal}`,
                    `Parameter: ${p.name}`
                );
            });

            const paramList = ctor.parameters.map(p => {
                const optional = p.isOptional ? '?' : '';
                return `${p.name}${optional}: ${p.type}`;
            }).join(', ');

            const sig = new vscode.SignatureInformation(
                `@${context.decoratorName}(${paramList})`
            );
            sig.parameters = params;

            return sig;
        });

        return help;
    }

    private parseDecoratorContext(
        document: vscode.TextDocument,
        position: vscode.Position
    ): { decoratorName: string; parameterIndex: number } | undefined {
        const line = document.lineAt(position.line).text;
        const textBefore = line.substring(0, position.character);

        // Match @DecoratorName(arg1, arg2, |
        const match = textBefore.match(/@(\w+)\s*\(([^)]*?)$/);
        if (!match) {
            return undefined;
        }

        const decoratorName = match[1];
        const argsText = match[2];

        // Count commas to determine parameter index
        // Handle nested parentheses by tracking depth
        let parameterIndex = 0;
        let depth = 0;
        for (const char of argsText) {
            if (char === '(' || char === '[' || char === '{') {
                depth++;
            } else if (char === ')' || char === ']' || char === '}') {
                depth--;
            } else if (char === ',' && depth === 0) {
                parameterIndex++;
            }
        }

        return { decoratorName, parameterIndex };
    }
}
