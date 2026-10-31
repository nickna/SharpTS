export function addTwo(value: number): number {
    const result = value + 2; // @break:helper
    return result; // @step:return
}
