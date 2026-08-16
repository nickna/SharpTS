// Regex benchmarks — measure per-invocation regex compilation overhead.
// Each call to `text.replace(/.../g, ...)` currently constructs a fresh
// SharpTSRegExp → System.Text.RegularExpressions.Regex compile, even when
// the literal is identical across invocations. A cache should collapse
// repeated calls to a single compilation per process lifetime.

function regexLiteralLoop(input: string, n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        const out = input.replace(/foo/g, "bar");
        total = total + out.length;
    }
    return total;
}

function regexValidatorLoop(input: string, n: number): number {
    let valid: number = 0;
    for (let i: number = 0; i < n; i++) {
        if (/^[a-z]+$/.test(input)) {
            valid = valid + 1;
        }
    }
    return valid;
}

function regexExtractLoop(input: string, n: number): number {
    let count: number = 0;
    for (let i: number = 0; i < n; i++) {
        const m = input.match(/(\w+)@(\w+)/);
        if (m !== null) {
            count = count + 1;
        }
    }
    return count;
}
