/**
 * Protocol types for communication with the SharpTS LSP bridge.
 */

export interface BridgeRequest {
    seq: number;
    command: string;
    arguments?: Record<string, unknown>;
}

export interface BridgeResponse<T = unknown> {
    seq: number;
    success: boolean;
    message?: string;
    body?: T;
}

// Command-specific response types

export interface ResolveTypeResult {
    exists: boolean;
    fullName?: string;
    isAttribute?: boolean;
    isAbstract?: boolean;
    isSealed?: boolean;
    assembly?: string;
}

export interface AttributeInfo {
    name: string;
    fullName: string;
    namespace: string;
    assembly: string;
}

export interface ListAttributesResult {
    attributes: AttributeInfo[];
}

export interface ParameterInfo {
    name: string;
    type: string;
    isOptional: boolean;
    defaultValue?: string;
}

export interface ConstructorInfo {
    parameters: ParameterInfo[];
}

export interface PropertyInfo {
    name: string;
    type: string;
}

export interface AttributeDetailResult {
    fullName: string;
    constructors: ConstructorInfo[];
    properties: PropertyInfo[];
}

export interface DocumentationResult {
    fullName: string;
    documentation?: string;
}
