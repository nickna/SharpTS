import { bench } from "./lib/bench.ts";

// A small Brainfuck interpreter — a "macro" workload dominated by interpreter
// dispatch: a tight loop over the program string (charCodeAt), precomputed
// bracket jumps, and tape reads/writes on a Uint8Array. The program is a
// pointer-neutral snippet concatenated `n` times, so larger n = more dispatch
// (linear) with a deterministic final tape across SharpTS, Node, and Bun.

// "+++++[>+<-]>[<+>-]<": adds 5 to cell0 (mod 256), pointer returns to cell0.
const SNIPPET: string = "+++++[>+<-]>[<+>-]<";

function buildProgram(reps: number): string {
    let s: string = "";
    for (let i: number = 0; i < reps; i++) {
        s = s + SNIPPET;
    }
    return s;
}

// Precompute matching-bracket targets so '[' / ']' are O(1) in the hot loop.
function buildJumps(program: string): number[] {
    const len: number = program.length;
    const jumps: number[] = [];
    for (let i: number = 0; i < len; i++) {
        jumps.push(0);
    }
    const stack: number[] = [];
    for (let i: number = 0; i < len; i++) {
        const c: number = program.charCodeAt(i);
        if (c === 91) { // '['
            stack.push(i);
        } else if (c === 93) { // ']'
            const open: number = stack[stack.length - 1];
            stack.pop();
            jumps[open] = i;
            jumps[i] = open;
        }
    }
    return jumps;
}

function runBF(program: string, jumps: number[]): number {
    const TAPE: number = 4096;
    const tape: Uint8Array = new Uint8Array(TAPE);
    let ptr: number = 0;
    let ip: number = 0;
    const len: number = program.length;
    while (ip < len) {
        const c: number = program.charCodeAt(ip);
        if (c === 43) {           // '+'
            tape[ptr] = (tape[ptr] + 1) & 255;
        } else if (c === 45) {    // '-'
            tape[ptr] = (tape[ptr] - 1) & 255;
        } else if (c === 62) {    // '>'
            ptr = ptr + 1;
        } else if (c === 60) {    // '<'
            ptr = ptr - 1;
        } else if (c === 91) {    // '['
            if (tape[ptr] === 0) ip = jumps[ip];
        } else if (c === 93) {    // ']'
            if (tape[ptr] !== 0) ip = jumps[ip];
        }
        ip = ip + 1;
    }
    let sum: number = 0;
    for (let i: number = 0; i < TAPE; i++) {
        sum = sum + tape[i];
    }
    return sum;
}

const params: number[] = [50, 500, 5000];
for (let p: number = 0; p < params.length; p++) {
    const n: number = params[p];
    const program: string = buildProgram(n);
    const jumps: number[] = buildJumps(program);
    bench("brainfuck", n, () => runBF(program, jumps));
}
