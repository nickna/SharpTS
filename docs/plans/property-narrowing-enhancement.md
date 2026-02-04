# Property Narrowing Enhancement Plan

## Overview

This plan outlines enhancements to SharpTS's type narrowing system to better align with TypeScript's Control Flow Analysis (CFA). The goal is to support narrowing for property access, nested paths, and proper invalidation on mutation.

## Current State

### What Works
- Variable narrowing: `if (x !== null)` narrows `x` from `T | null` to `T`
- Simple property narrowing: `if (obj.prop !== null)` narrows `obj.prop` (just implemented)
- Type guards: `typeof`, `instanceof`, discriminated unions

### Limitations
1. No nested property narrowing: `obj.a.b !== null`
2. No narrowing invalidation on reassignment
3. No narrowing through aliasing: `const alias = obj; alias.prop = null;` doesn't invalidate `obj.prop`
4. No narrowing in compound expressions: `obj.prop !== null && obj.prop.value`
5. No narrowing for computed properties: `obj[key]`
6. No narrowing for method return values: `getObj().prop`

---

## Proposed Architecture

### Phase 1: Narrowing Path Infrastructure

**Goal**: Represent narrowable locations as paths rather than simple variable names.

```
NarrowingPath =
  | Variable(name: string)
  | PropertyAccess(base: NarrowingPath, property: string)
  | ElementAccess(base: NarrowingPath, index: number)  // For tuple narrowing
```

**Key Changes**:
- Replace `string? VarName` with `NarrowingPath?` in type guard analysis
- Create `NarrowingPathComparer` for dictionary keys
- Store narrowings keyed by path in the narrowing scope

**Files Affected**:
- `TypeChecker.cs` - Add `NarrowingPath` type
- `TypeChecker.Compatibility.TypeGuards.cs` - Return paths instead of names
- `TypeChecker.Statements.cs` - Handle path-based narrowings in `VisitIf`
- `TypeChecker.Properties.cs` - Look up narrowings by path in `CheckGet`

### Phase 2: Assignment Tracking & Invalidation

**Goal**: Invalidate narrowings when the narrowed location or its ancestors are assigned.

**Approach**:
When processing an assignment `target = value`:
1. Compute the `NarrowingPath` for `target`
2. Invalidate all narrowings where:
   - The path equals `target`
   - The path is a descendant of `target` (e.g., assigning `obj` invalidates `obj.prop`)
   - The path is an ancestor of `target` (e.g., assigning `obj.prop` may affect `obj` narrowings based on discriminants)

**Key Insight**: TypeScript is conservative here. If you reassign any part of an object, narrowings on that object's properties may be invalidated.

**Files Affected**:
- `TypeChecker.Statements.cs` - Add invalidation in `VisitAssign`, `VisitLet`, `VisitConst`
- `TypeChecker.cs` - Add `InvalidateNarrowingsFor(NarrowingPath)` method

### Phase 3: Control Flow Graph (CFG) Based Narrowing

**Goal**: Track narrowings across complex control flow, not just if/else blocks.

**Current Limitation**:
```typescript
function example(x: string | null) {
    if (x === null) return;
    // x should be narrowed to string here (after early return)
    console.log(x.length);  // Currently works via AlwaysTerminates check
}
```

**More Complex Cases**:
```typescript
function example(x: string | null) {
    x !== null && console.log(x.length);  // Narrowing in && RHS
    x === null || console.log(x.length);  // Narrowing in || RHS
    const y = x !== null ? x.length : 0;  // Narrowing in ternary
}
```

**Approach**:
Build a simplified CFG during type checking:
1. **Basic Blocks**: Sequences of statements with single entry/exit
2. **Edges**: Control flow between blocks (conditional, unconditional, back-edges for loops)
3. **Narrowing State**: Each block has entry/exit narrowing states
4. **Data Flow**: Forward propagation of narrowings through the CFG

**Files Affected**:
- New file: `TypeChecker.ControlFlow.cs` - CFG construction
- New file: `TypeChecker.ControlFlow.Narrowing.cs` - Narrowing propagation
- `TypeChecker.Statements.cs` - Integrate CFG-based narrowing

### Phase 4: Expression-Level Narrowing

**Goal**: Support narrowing within expressions (&&, ||, ternary).

**Approach**:
When type-checking expressions like `a && b`:
1. Check `a`, collecting any narrowings it implies
2. Check `b` with those narrowings applied (for `&&`) or inverted (for `||`)
3. The result type considers both paths

**Implementation**:
```csharp
private TypeInfo CheckLogicalAnd(Expr.Binary expr)
{
    var leftType = CheckExpr(expr.Left);
    var guard = AnalyzeTypeGuard(expr.Left);

    // Apply narrowing for right side
    using (var scope = ApplyNarrowing(guard))
    {
        var rightType = CheckExpr(expr.Right);
        // ... compute result type
    }
}
```

**Files Affected**:
- `TypeChecker.Operators.cs` - Add narrowing to logical operators
- `TypeChecker.Expressions.cs` - Add narrowing to ternary operator

### Phase 5: Aliasing Awareness (Advanced)

**Goal**: Handle cases where objects are aliased and mutations through aliases should invalidate narrowings.

```typescript
const obj = { prop: getValue() };
const alias = obj;
if (obj.prop !== null) {
    alias.prop = null;  // Should invalidate obj.prop narrowing
    console.log(obj.prop.value);  // Error: obj.prop might be null
}
```

**Approach Options**:

**Option A: Conservative (TypeScript's approach for mutable properties)**
- Only narrow `readonly` properties reliably
- For mutable properties, be conservative about what can be narrowed

**Option B: Alias Tracking**
- Track when variables alias the same object
- Invalidate narrowings on all aliases when any is mutated
- Complex and potentially expensive

**Option C: Escape Analysis**
- Determine if an object "escapes" (is passed to functions, stored in other objects)
- Only allow narrowing for non-escaping objects

**Recommendation**: Start with Option A (conservative approach matching TypeScript).

---

## Implementation Order

```
Phase 1 (Foundation)     [Estimated: Medium complexity]
    │
    ▼
Phase 2 (Invalidation)   [Estimated: Medium complexity]
    │
    ▼
Phase 4 (Expressions)    [Estimated: Low-Medium complexity]
    │
    ▼
Phase 3 (CFG)            [Estimated: High complexity]
    │
    ▼
Phase 5 (Aliasing)       [Estimated: High complexity, may be optional]
```

Phases 1-2 provide the most value for common patterns.
Phase 4 is relatively self-contained.
Phase 3 is the most architectural change.
Phase 5 may not be necessary if we match TypeScript's conservative behavior.

---

## Test Cases to Support

### Phase 1: Nested Property Paths
```typescript
type Nested = { a: { b: { c: string | null } } };
function test(obj: Nested) {
    if (obj.a.b.c !== null) {
        console.log(obj.a.b.c.length);  // Should work
    }
}
```

### Phase 2: Assignment Invalidation
```typescript
function test(obj: { prop: string | null }) {
    if (obj.prop !== null) {
        obj.prop = null;  // Reassignment
        console.log(obj.prop.length);  // Should error
    }
}

function test2(obj: { prop: string | null }) {
    if (obj.prop !== null) {
        obj = { prop: null };  // Object reassignment
        console.log(obj.prop.length);  // Should error
    }
}
```

### Phase 3: Control Flow
```typescript
function test(x: string | null) {
    if (x === null) {
        throw new Error();
    }
    console.log(x.length);  // x is string (already partially works)
}

function test2(x: string | null) {
    while (x === null) {
        x = getValue();
    }
    console.log(x.length);  // x is string after loop
}
```

### Phase 4: Expression Narrowing
```typescript
function test(x: string | null) {
    x !== null && console.log(x.length);  // Should work
    const len = x !== null ? x.length : 0;  // Should work
    x === null || console.log(x.length);  // Should work
}
```

### Phase 5: Aliasing
```typescript
function test(obj: { prop: string | null }) {
    const alias = obj;
    if (obj.prop !== null) {
        mutate(alias);  // Might modify alias.prop
        console.log(obj.prop.length);  // Should error (conservative)
    }
}
```

---

## Performance Considerations

1. **Path Comparison Cost**: Nested paths require structural comparison. Consider interning paths or using efficient hash codes.

2. **Narrowing Scope Size**: Deep nesting could create many narrowing entries. Consider limiting depth or using lazy evaluation.

3. **CFG Construction**: Building a CFG adds overhead. Consider:
   - Lazy CFG construction (only when needed for complex narrowing)
   - Caching CFG for functions that are type-checked multiple times

4. **Invalidation Cost**: Checking all narrowings for invalidation on each assignment could be O(n). Consider:
   - Indexing narrowings by their root variable
   - Using a trie structure for path lookups

---

## Compatibility Considerations

1. **Breaking Changes**: More precise narrowing could cause existing code to fail type checking if it was relying on unsound behavior.

2. **Strictness Levels**: Consider a flag for "strict narrowing" that enables more aggressive invalidation.

3. **TypeScript Alignment**: Document any intentional deviations from TypeScript's behavior.

---

## Decisions Made

Based on requirements gathering:

| Decision | Choice | Rationale |
|----------|--------|-----------|
| TypeScript Parity | **Exact parity** | Maximum compatibility with existing TS codebases |
| Performance | **Full analysis acceptable** | Correctness over speed; typical compiler approach |
| Breaking Changes | **Always strict** | Soundness is more important than backwards compat |
| Phase Priority | **All phases** | Complete implementation for full TS alignment |

## Revised Implementation Order

Given the decision for exact TypeScript parity, the recommended order is:

```
Phase 1: Narrowing Path Infrastructure     [2-3 days]
    ├── NarrowingPath type and comparison
    ├── Path-based narrowing storage
    └── Update type guard analysis
         │
         ▼
Phase 2: Assignment Invalidation           [2-3 days]
    ├── Track assignments in VisitAssign/VisitLet
    ├── Invalidate affected narrowings
    └── Handle object reassignment
         │
         ▼
Phase 4: Expression-Level Narrowing        [2-3 days]
    ├── && and || operators
    ├── Ternary expressions
    └── Nullish coalescing (??)
         │
         ▼
Phase 3: Control Flow Graph                [5-7 days]
    ├── CFG construction
    ├── Forward narrowing propagation
    ├── Loop handling
    └── Exception flow (try/catch/finally)
         │
         ▼
Phase 5: Aliasing Awareness                [3-4 days]
    ├── Conservative mutable property handling
    ├── Readonly property optimization
    └── Function call escape analysis
```

**Total Estimated Effort: 14-20 days**

## Implementation Decisions

| Question | Decision | Notes |
|----------|----------|-------|
| CFG Representation | **Separate pre-built CFG** | Build in dedicated pass before type checking |
| Narrowing Storage | **Separate NarrowingContext** | Clean separation from TypeEnvironment |
| Test Strategy | **Comprehensive TS test port** | Port from microsoft/TypeScript repo |

---

## Detailed Implementation Plan

### New Files to Create

```
TypeSystem/
├── ControlFlow/
│   ├── ControlFlowGraph.cs       # CFG data structure
│   ├── ControlFlowBuilder.cs     # CFG construction from AST
│   ├── BasicBlock.cs             # Basic block representation
│   └── FlowEdge.cs               # Edge types (conditional, unconditional, etc.)
├── Narrowing/
│   ├── NarrowingPath.cs          # Path representation (var, prop access, etc.)
│   ├── NarrowingContext.cs       # Scoped narrowing state
│   ├── NarrowingAnalyzer.cs      # Core narrowing logic
│   └── NarrowingPropagator.cs    # CFG-based propagation
```

### NarrowingPath Design

```csharp
/// <summary>
/// Represents a narrowable location in code.
/// </summary>
public abstract record NarrowingPath
{
    /// <summary>Simple variable: x</summary>
    public sealed record Variable(string Name) : NarrowingPath;

    /// <summary>Property access: x.prop</summary>
    public sealed record PropertyAccess(NarrowingPath Base, string Property) : NarrowingPath;

    /// <summary>Element access with literal index: x[0] (for tuples)</summary>
    public sealed record ElementAccess(NarrowingPath Base, int Index) : NarrowingPath;

    /// <summary>
    /// Checks if this path is a prefix of another (for invalidation).
    /// e.g., "obj" is a prefix of "obj.prop.value"
    /// </summary>
    public bool IsPrefixOf(NarrowingPath other) { ... }

    /// <summary>
    /// Gets the root variable of this path.
    /// e.g., "obj.prop.value" -> "obj"
    /// </summary>
    public string RootVariable { get; }
}
```

### NarrowingContext Design

```csharp
/// <summary>
/// Tracks type narrowings for a scope in the control flow.
/// Immutable - operations return new contexts.
/// </summary>
public sealed class NarrowingContext
{
    private readonly ImmutableDictionary<NarrowingPath, TypeInfo> _narrowings;

    /// <summary>Gets the narrowed type for a path, or null if not narrowed.</summary>
    public TypeInfo? GetNarrowing(NarrowingPath path);

    /// <summary>Returns a new context with the narrowing applied.</summary>
    public NarrowingContext WithNarrowing(NarrowingPath path, TypeInfo type);

    /// <summary>Returns a new context with narrowings invalidated for the path and descendants.</summary>
    public NarrowingContext Invalidate(NarrowingPath path);

    /// <summary>Merges two contexts (for join points in CFG).</summary>
    public static NarrowingContext Merge(NarrowingContext a, NarrowingContext b);
}
```

### Control Flow Graph Design

```csharp
public sealed class ControlFlowGraph
{
    public BasicBlock Entry { get; }
    public BasicBlock Exit { get; }
    public IReadOnlyList<BasicBlock> Blocks { get; }
}

public sealed class BasicBlock
{
    public int Id { get; }
    public IReadOnlyList<Stmt> Statements { get; }
    public IReadOnlyList<FlowEdge> Predecessors { get; }
    public IReadOnlyList<FlowEdge> Successors { get; }

    // For narrowing analysis
    public NarrowingContext? EntryContext { get; set; }
    public NarrowingContext? ExitContext { get; set; }
}

public sealed record FlowEdge(
    BasicBlock From,
    BasicBlock To,
    FlowEdgeKind Kind,
    Expr? Condition = null,      // For conditional edges
    bool ConditionIsTrue = true  // Whether condition is true or false on this edge
);

public enum FlowEdgeKind
{
    Unconditional,    // Normal flow
    ConditionalTrue,  // if/while condition is true
    ConditionalFalse, // if/while condition is false
    Return,           // Return statement
    Throw,            // Throw statement
    Break,            // Break statement
    Continue,         // Continue statement
    LoopBack          // Back edge in loops
}
```

### Integration with TypeChecker

```csharp
public partial class TypeChecker
{
    private ControlFlowGraph? _currentCfg;
    private NarrowingContext _narrowingContext = NarrowingContext.Empty;

    public TypeMap Check(List<Stmt> statements)
    {
        // Phase 1: Build CFG
        _currentCfg = ControlFlowBuilder.Build(statements);

        // Phase 2: Propagate narrowings through CFG
        NarrowingPropagator.Propagate(_currentCfg, this);

        // Phase 3: Type check with narrowing information
        foreach (var stmt in statements)
        {
            CheckStmt(stmt);
        }

        return _typeMap;
    }
}
```

---

## Test Porting Strategy

### TypeScript Test Sources

The TypeScript compiler has narrowing tests in:
- `tests/cases/compiler/` - General narrowing tests
- `tests/cases/conformance/controlFlow/` - CFG-specific tests
- `tests/cases/conformance/types/typeGuards/` - Type guard tests

### Porting Process

1. **Identify relevant tests**: Search for tests with "narrow", "controlFlow", "typeGuard" in names
2. **Convert syntax**: TS tests use special comments for expected errors; convert to xUnit assertions
3. **Categorize by phase**: Group tests by which phase they validate
4. **Create baseline**: Run against TypeScript to capture expected behavior

### Test File Structure

```
SharpTS.Tests/
└── TypeCheckerTests/
    └── NarrowingTests/
        ├── VariableNarrowingTests.cs      # Existing + enhanced
        ├── PropertyNarrowingTests.cs       # Phase 1
        ├── AssignmentInvalidationTests.cs  # Phase 2
        ├── ControlFlowNarrowingTests.cs    # Phase 3
        ├── ExpressionNarrowingTests.cs     # Phase 4
        └── AliasingTests.cs                # Phase 5
```

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| CFG complexity for async/generators | Start with sync functions; add async support incrementally |
| Performance regression | Benchmark before/after; optimize hot paths |
| TypeScript behavior changes | Pin to specific TS version for test baselines |
| Scope creep | Strict phase boundaries; merge after each phase |

---

## Implementation Status

### Phase 1: Narrowing Path Infrastructure - COMPLETE

**Files Created:**
- `TypeSystem/Narrowing/NarrowingPath.cs` - Path representation (Variable, PropertyAccess, ElementAccess)
- `TypeSystem/Narrowing/NarrowingContext.cs` - Immutable narrowing state with merge support
- `TypeSystem/Narrowing/NarrowingPathExtractor.cs` - Extracts paths from AST expressions

**Files Modified:**
- `TypeChecker.cs` - Added narrowing context stack and methods
- `TypeChecker.Compatibility.TypeGuards.cs` - Added path-based type guard analysis
- `TypeChecker.Statements.cs` - Integrated path-based narrowing in VisitIf
- `TypeChecker.Properties.cs` - Added narrowing lookup in CheckGet

**Tests:** 8 tests in `PropertyNarrowingTests.cs`

### Phase 2: Assignment Invalidation - COMPLETE

**Files Modified:**
- `TypeChecker.Expressions.cs` - Added invalidation to CheckAssign
- `TypeChecker.Properties.cs` - Added invalidation to CheckSet
- `TypeChecker.Properties.Index.cs` - Added invalidation to CheckSetIndex
- `TypeChecker.Operators.cs` - Added invalidation to compound/logical assignment methods

**Tests:** 9 passing, 2 skipped (known limitations) in `AssignmentInvalidationTests.cs`

**Known Limitations:**
- Variable narrowing uses TypeEnvironment which replaces the declared type
- Loop-based narrowing requires full CFG analysis

### Phase 3: Control Flow Graph - PARTIAL

**Files Created:**
- `TypeSystem/ControlFlow/BasicBlock.cs` - Basic block representation
- `TypeSystem/ControlFlow/FlowEdge.cs` - Edge types and representation
- `TypeSystem/ControlFlow/ControlFlowGraph.cs` - CFG data structure
- `TypeSystem/ControlFlow/ControlFlowBuilder.cs` - Builds CFG from AST
- `TypeSystem/ControlFlow/NarrowingPropagator.cs` - CFG-based narrowing analysis

**Implementation Note:**
The CFG infrastructure is complete but not yet integrated into the main type checking flow. Instead, compound conditions are handled directly in VisitIf using `AnalyzeCompoundTypeGuards`, which provides the key functionality for common use cases.

**Tests:** 12 tests in `ControlFlowNarrowingTests.cs`

**What Works:**
- Early return/throw narrowing
- Logical && narrowing in conditions
- Ternary expression narrowing
- Property narrowing in all contexts
- Multiple null checks in && conditions

**What Needs Full CFG (Future):**
- Loop narrowing with back-edge analysis
- Complex control flow with multiple join points
- Exception flow analysis

### Phase 4: Expression-Level Narrowing - COMPLETE

**Files Modified:**
- `TypeChecker.Operators.cs` - Added expression-level narrowing to `CheckLogical` and `CheckTernary`
- `TypeChecker.Compatibility.TypeGuards.cs` - Added `AnalyzeCompoundTypeGuards` for collecting multiple narrowings

**Implementation:**
- `CheckLogical`: Applies narrowings when checking the right operand of && and ||
- `CheckTernary`: Applies narrowings to both then and else branches based on condition
- Uses `AnalyzeCompoundTypeGuards` to collect all narrowings from compound conditions
- Properly handles both variable and property narrowings in expressions

**Tests:** 15 tests in `ExpressionNarrowingTests.cs`

**What Works:**
- Logical AND (&&) expression narrowing: `x !== null && x.length`
- Logical OR (||) expression narrowing: `x === null || x.length`
- Ternary expression narrowing: `x !== null ? x.length : 0`
- Property narrowing in all expression contexts
- Multiple narrowings in compound expressions
- Chained property access after narrowing
- Combined patterns (&&, ||, ?:, ??)

### Phase 5: Aliasing Awareness - COMPLETE

**Files Modified:**
- `TypeSystem/Narrowing/NarrowingContext.cs` - Added `InvalidatePropertiesOf()` for invalidating property narrowings
- `TypeChecker.cs` - Added `_variableAliases` dictionary, `InvalidatePropertiesForFunctionArg()`, `GetNarrowingPath()`
- `TypeChecker.Calls.cs` - Added invalidation in `CheckCall()` for function arguments and method calls
- `TypeChecker.Properties.cs` - Added alias-aware invalidation in `CheckSet()`
- `TypeChecker.Statements.cs` - Added alias tracking in `VisitVar()` and `VisitConst()`
- `TypeChecker.Compatibility.Helpers.cs` - Added `IsObjectType()` helper

**Implementation:**
- Function call invalidation: When an object is passed to a function, its property narrowings are invalidated
- Method call invalidation: When a method is called on an object (`obj.method()`), property narrowings are invalidated
- Simple alias tracking: When `const alias = obj` is declared, mutations through `alias.prop` invalidate `obj.prop` narrowings

**Tests:** 8 passing, 3 skipped in `AliasingTests.cs`

**What Works:**
- Function call invalidates mutable property narrowings
- Method call invalidates mutable property narrowings
- Only passed object's narrowings invalidated (not other objects)
- Simple alias assignment invalidation
- Local object literals remain safe to narrow
- Const bindings with no escape remain safe
- Discriminated union narrowing preserved (discriminant properties typically immutable)

**Skipped (not yet implemented):**
- Readonly property narrowing (parser doesn't support `readonly` keyword in interfaces)
- Complex cross-scope alias tracking (requires escape analysis)

---

## Success Criteria

1. All existing tests continue to pass
2. Ported TypeScript narrowing tests pass
3. Performance within 20% of current type checking time
4. No regressions in error message quality
