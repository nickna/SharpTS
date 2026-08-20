interface SortRecord {
    key: number;
    tag: string;
}

function makeNumbers(n: number): number[] {
    const out: number[] = [];
    let state: number = 123456789;
    for (let i: number = 0; i < n; i++) {
        state = (state * 48271) % 2147483647;
        out.push(state);
    }
    return out;
}

function makeRecords(n: number): SortRecord[] {
    const out: SortRecord[] = [];
    let state: number = 987654321;
    for (let i: number = 0; i < n; i++) {
        state = (state * 48271) % 2147483647;
        out.push({ key: state, tag: "t" + (state % 1000) });
    }
    return out;
}

function copyNumbers(source: number[]): number {
    const copy: number[] = source.slice();
    return copy[0] + copy[copy.length - 1];
}

function copyRecords(source: SortRecord[]): number {
    const copy: SortRecord[] = source.slice();
    return copy[0].key + copy[copy.length - 1].key;
}

function sortNumbers(source: number[]): number {
    const copy: number[] = source.slice();
    copy.sort((left: number, right: number): number => left - right);
    return copy[0] + copy[copy.length - 1];
}

function sortRecords(source: SortRecord[]): number {
    const copy: SortRecord[] = source.slice();
    copy.sort((left: SortRecord, right: SortRecord): number => left.key - right.key);
    return copy[0].key;
}

function sortCombined(numbers: number[], records: SortRecord[]): number {
    return sortNumbers(numbers) + sortRecords(records);
}
