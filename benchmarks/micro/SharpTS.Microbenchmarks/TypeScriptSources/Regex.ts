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

// Exact intrinsic global String.match path from #1387. The returned array is
// observable, but its elements only require the full-match strings; detailed
// exec result objects (captures/index/input/groups) must not be materialized.
function regexGlobalMatchLoop(input: string, n: number): number {
    let count: number = 0;
    for (let i: number = 0; i < n; i++) {
        const matches = input.match(/[a-z]+/g);
        if (matches !== null) {
            count = count + matches.length;
        }
    }
    return count;
}

// Allocation contrast for #1387. String.matchAll must expose detailed result
// objects (including index/input/captures), so this is the observable-semantics
// case against which the allocation-light String.match path is compared.
function regexDetailedMatchAllLoop(input: string, n: number): number {
    let count: number = 0;
    for (let i: number = 0; i < n; i++) {
        const matches = input.matchAll(/[a-z]+/g);
        for (const match of matches) {
            count = count + match.length;
        }
    }
    return count;
}
