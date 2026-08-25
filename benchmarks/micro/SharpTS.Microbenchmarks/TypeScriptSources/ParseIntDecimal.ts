// Keep both runtime parse helpers rooted so the C# benchmark can call them
// directly without measuring a generated TypeScript wrapper.
export function parseDecimal(value: string): number {
    return parseInt(value, 10);
}

export function parseGeneral(value: string, radix: number): number {
    return parseInt(value, radix);
}
