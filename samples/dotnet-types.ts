// .NET Types from TypeScript — @DotNetType demonstration
// Usage: sharpts samples/dotnet-types.ts
//
// Demonstrates: @DotNetType decorator, static members,
//               @DotNetOverload for overload selection,
//               delegates (TS closure → .NET Action via Task.Run),
//               events (DOM-style addEventListener on AppDomain.ProcessExit).
//
// Unlike the Interop/ example (which shows C# consuming compiled TS),
// this shows TypeScript consuming .NET — the inbound direction.
//
// Runs clean in both interpreted and compiled modes (including standalone
// DLL deployment without SharpTS.dll alongside). Exercises overloaded method
// dispatch, @DotNetOverload hints, TS→Action delegates, and DOM-style event
// subscription across the .NET BCL.

@DotNetType("System.Text.StringBuilder")
declare class StringBuilder {
    constructor();
    append(value: string): StringBuilder;
    appendLine(value: string): StringBuilder;
    toString(): string;
    readonly length: number;
}

@DotNetType("System.Guid")
declare class Guid {
    static newGuid(): Guid;
    toString(): string;
}

@DotNetType("System.Convert")
declare class Convert {
    // Without the hint, Convert.ToInt32(3.7) resolves to ToInt32(double) which
    // rounds to 4. @DotNetOverload("int") pins to ToInt32(int), which truncates
    // to 3 — a visible, testable behavior difference from the hint.
    @DotNetOverload("int")
    static toInt32(value: number): number;
}

@DotNetType("System.Threading.Tasks.Task")
declare class Task {
    static run(action: () => void): Task;
    wait(): void;
}

@DotNetType("System.AppDomain")
declare class AppDomain {
    static readonly currentDomain: AppDomain;
    addEventListener(name: string, handler: (sender: any, args: any) => void): void;
}

console.log('.NET Types from TypeScript');
console.log('==========================');
console.log('');

// ---- 1. StringBuilder + Guid.newGuid() ----
console.log('1) StringBuilder + static Guid.newGuid()');
console.log('----------------------------------------');
const sb = new StringBuilder();
sb.append('user=');
sb.appendLine('alice');
sb.append('id=');
sb.append(Guid.newGuid().toString());
console.log(sb.toString());
console.log('length: ' + sb.length);
console.log('');

// ---- 2. @DotNetOverload: pin which overload gets called ----
console.log('2) @DotNetOverload — Convert.toInt32(3.7) with "int" hint');
console.log('---------------------------------------------------------');
console.log('truncates to: ' + Convert.toInt32(3.7));
console.log('(without the hint, .NET would pick ToInt32(double) and round to 4)');
console.log('');

// ---- 3. Delegates: pass a TS closure where .NET wants an Action ----
console.log('3) Delegates — TS closure as Action for Task.Run');
console.log('------------------------------------------------');
let marker = 'before';
const t = Task.run(() => {
    marker = 'inside the task';
    console.log('  delegate body ran on a Task');
});
t.wait();
console.log('marker after Task.Run: ' + marker);
console.log('');

// ---- 4. Events: DOM-style addEventListener on a .NET event ----
console.log('4) Events — AppDomain.ProcessExit');
console.log('---------------------------------');
AppDomain.currentDomain.addEventListener('ProcessExit', (sender: any, args: any) => {
    console.log('(event) ProcessExit fired during shutdown');
});
console.log('handler wired — the ProcessExit line below fires as the process exits');
