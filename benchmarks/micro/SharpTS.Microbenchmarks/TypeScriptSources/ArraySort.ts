function sortNumbers(source: number[]): number {
    const copy: number[] = source.slice();
    copy.sort((left: number, right: number): number => left - right);
    return copy[0] + copy[copy.length - 1];
}
