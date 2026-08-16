import { bench } from "./lib/bench.ts";

// Regex throughput: V8 (Irregexp), JSC, and .NET's Regex diverge sharply here.
// The corpus is deterministic and built once per param (outside the timed fn)
// so we measure matching/replacing, not string construction. The timed fn
// funnels to a numeric checksum for the anti-DCE guard.

function buildCorpus(n: number): string {
    let s: string = "";
    for (let i: number = 0; i < n; i++) {
        s = s + "user" + i + "@host" + (i % 97) + ".com paid 1" + i + " on day " + (i % 365) + "; ";
    }
    return s;
}

function regexWork(corpus: string): number {
    const emails = corpus.match(/\w+@\w+\.\w+/g);
    const emailCount: number = emails === null ? 0 : emails.length;
    const masked: string = corpus.replace(/\d+/g, "#");
    const hasDay: boolean = /day \d+/.test(corpus);
    return emailCount + masked.length + (hasDay ? 1 : 0);
}

const params: number[] = [100, 1000, 10000];
for (let p: number = 0; p < params.length; p++) {
    const n: number = params[p];
    const corpus: string = buildCorpus(n);
    bench("regex", n, () => regexWork(corpus));
}
