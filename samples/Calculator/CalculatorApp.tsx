import {
    Border, Button, ComboBox, DatePicker, DrawingCanvas, ErrorBoundary, Grid, NumericUpDown,
    ScrollViewer, StackPanel, TextBlock, TextBox, Window, useCallback, useControlRef, useMemo,
    useReducer, useState, WindowHandle, KeyEvent,
} from "@sharpts/gui";
import {
    CalculatorAction, CalculatorState, Operator, calculatorActionForKey, calculatorReducer,
    deriveCalculatorPresentation, initialCalculatorState,
} from "./calculator";
import { AngleUnit, evaluateExpression, formatApproximate } from "./expression";
import {
    ProgrammerAction, ProgrammerBinaryOperator, ProgrammerState, initialProgrammerState,
    programmerReadouts, programmerReducer,
} from "./programmer";
import { addCalendarDuration, daysBetweenDates, durationBetweenDates } from "./dateCalculation";
import { OFFLINE_CURRENCY_RATES, UNIT_FAMILIES, convertCurrency, convertUnit } from "./converters";
import { DEFAULT_VIEWPORT, GRAPH_COLORS, GraphEquation, GraphViewport, graphCommands, traceEquation } from "./graphing";

export type CalculatorButtonRole = "digit" | "utility" | "operator" | "equal";
export interface CalculatorButtonDefinition {
    readonly id: string; readonly label: string; readonly automationName: string; readonly shortcut: string;
    readonly role: CalculatorButtonRole; readonly row: number; readonly column: number; readonly action: CalculatorAction;
}

export const CALCULATOR_BUTTONS: CalculatorButtonDefinition[] = [
    { id: "percent", label: "%", automationName: "Percent", shortcut: "%", role: "utility", row: 3, column: 0, action: { type: "percent" } },
    { id: "clear-entry", label: "CE", automationName: "Clear entry", shortcut: "Delete", role: "utility", row: 3, column: 1, action: { type: "clearEntry" } },
    { id: "clear", label: "C", automationName: "Clear", shortcut: "Escape", role: "utility", row: 3, column: 2, action: { type: "clear" } },
    { id: "backspace", label: "⌫", automationName: "Backspace", shortcut: "Backspace", role: "utility", row: 3, column: 3, action: { type: "backspace" } },
    { id: "reciprocal", label: "1/x", automationName: "Reciprocal", shortcut: "R", role: "utility", row: 4, column: 0, action: { type: "reciprocal" } },
    { id: "square", label: "x²", automationName: "Square", shortcut: "Q", role: "utility", row: 4, column: 1, action: { type: "square" } },
    { id: "square-root", label: "√x", automationName: "Square root", shortcut: "@", role: "utility", row: 4, column: 2, action: { type: "squareRoot" } },
    { id: "divide", label: "÷", automationName: "Divide", shortcut: "/", role: "operator", row: 4, column: 3, action: { type: "operator", operator: "/" } },
    { id: "digit-7", label: "7", automationName: "Seven", shortcut: "7", role: "digit", row: 5, column: 0, action: { type: "digit", digit: "7" } },
    { id: "digit-8", label: "8", automationName: "Eight", shortcut: "8", role: "digit", row: 5, column: 1, action: { type: "digit", digit: "8" } },
    { id: "digit-9", label: "9", automationName: "Nine", shortcut: "9", role: "digit", row: 5, column: 2, action: { type: "digit", digit: "9" } },
    { id: "multiply", label: "×", automationName: "Multiply", shortcut: "* or X", role: "operator", row: 5, column: 3, action: { type: "operator", operator: "*" } },
    { id: "digit-4", label: "4", automationName: "Four", shortcut: "4", role: "digit", row: 6, column: 0, action: { type: "digit", digit: "4" } },
    { id: "digit-5", label: "5", automationName: "Five", shortcut: "5", role: "digit", row: 6, column: 1, action: { type: "digit", digit: "5" } },
    { id: "digit-6", label: "6", automationName: "Six", shortcut: "6", role: "digit", row: 6, column: 2, action: { type: "digit", digit: "6" } },
    { id: "subtract", label: "−", automationName: "Subtract", shortcut: "-", role: "operator", row: 6, column: 3, action: { type: "operator", operator: "-" } },
    { id: "digit-1", label: "1", automationName: "One", shortcut: "1", role: "digit", row: 7, column: 0, action: { type: "digit", digit: "1" } },
    { id: "digit-2", label: "2", automationName: "Two", shortcut: "2", role: "digit", row: 7, column: 1, action: { type: "digit", digit: "2" } },
    { id: "digit-3", label: "3", automationName: "Three", shortcut: "3", role: "digit", row: 7, column: 2, action: { type: "digit", digit: "3" } },
    { id: "add", label: "+", automationName: "Add", shortcut: "+", role: "operator", row: 7, column: 3, action: { type: "operator", operator: "+" } },
    { id: "sign", label: "±", automationName: "Change sign", shortcut: "F9", role: "utility", row: 8, column: 0, action: { type: "sign" } },
    { id: "digit-0", label: "0", automationName: "Zero", shortcut: "0", role: "digit", row: 8, column: 1, action: { type: "digit", digit: "0" } },
    { id: "decimal", label: ".", automationName: "Decimal point", shortcut: ".", role: "digit", row: 8, column: 2, action: { type: "decimal" } },
    { id: "equals", label: "=", automationName: "Equals", shortcut: "Enter", role: "equal", row: 8, column: 3, action: { type: "equals" } },
];

export interface CalculatorButtonProps { readonly definition: CalculatorButtonDefinition; readonly active: boolean; readonly onPress: (action: CalculatorAction) => void; }
function buttonBackground(role: CalculatorButtonRole, active: boolean): string {
    if (active) return "#b45309"; if (role === "operator") return "#f59e0b"; if (role === "equal") return "#2563eb";
    if (role === "utility") return "#dbe4ee"; return "#ffffff";
}
export function CalculatorButton(props: CalculatorButtonProps): JSX.Element {
    const definition = props.definition;
    const dark = definition.role === "operator" || definition.role === "equal" || props.active;
    return <Button gridRow={definition.row} gridColumn={definition.column} margin={3} minWidth={52} minHeight={43}
        fontSize={17} fontWeight="semibold" background={buttonBackground(definition.role, props.active)} foreground={dark ? "white" : "#172033"}
        cornerRadius={8} automationName={definition.automationName} toolTip={definition.automationName + " · " + definition.shortcut}
        onClick={() => props.onPress(definition.action)}>{definition.label}</Button>;
}

interface SharedResultProps { readonly expression: string; readonly display: string; readonly status: string; }
function ResultPanel(props: SharedResultProps): JSX.Element {
    return <Border padding={14} background="#111827" cornerRadius={12}><StackPanel spacing={4}>
        <TextBlock key="expression" horizontalAlignment="right" textAlignment="right" fontSize={15} foreground="#a8b3c7" automationName="Expression">{props.expression === "" ? " " : props.expression}</TextBlock>
        <TextBlock key="display" horizontalAlignment="right" textAlignment="right" fontSize={34} fontWeight="semibold" foreground="white" automationName="Display">{props.display}</TextBlock>
        <TextBlock key="status" horizontalAlignment="right" textAlignment="right" fontSize={12} foreground="#cbd5e1" automationName="Status">{props.status}</TextBlock>
    </StackPanel></Border>;
}

interface MemoryBarProps { readonly value: string; readonly onRecall: (value: string) => void; }
function MemoryBar(props: MemoryBarProps): JSX.Element {
    const memoryState = useState<number[]>([]); const memory = memoryState[0]; const setMemory = memoryState[1];
    const current = Number.parseFloat(props.value); const stored = memory.length === 0 ? 0 : memory[0];
    return <Grid columns="*,*,*,*,*">
        <Button key="memory-clear" gridColumn={0} automationName="Clear memory" isEnabled={memory.length > 0} onClick={() => setMemory([])}>MC</Button>
        <Button key="memory-recall" gridColumn={1} automationName="Recall memory" isEnabled={memory.length > 0} onClick={() => props.onRecall(String(stored))}>MR</Button>
        <Button key="memory-add" gridColumn={2} automationName="Add to memory" onClick={() => setMemory([stored + current])}>M+</Button>
        <Button key="memory-subtract" gridColumn={3} automationName="Subtract from memory" onClick={() => setMemory([stored - current])}>M−</Button>
        <Button key="memory-store" gridColumn={4} automationName="Store in memory" onClick={() => setMemory([current])}>MS</Button>
    </Grid>;
}

interface HistorySinkProps { readonly onHistory: (entry: string) => void; }
interface StandardViewProps extends HistorySinkProps { readonly state: CalculatorState; readonly onPress: (action: CalculatorAction) => void; }
function StandardCalculatorView(props: StandardViewProps): JSX.Element {
    const state = props.state;
    const presentation = useMemo(() => deriveCalculatorPresentation(state), [state.display, state.accumulator, state.pendingOperator, state.waitingForOperand, state.repeatOperator, state.error, state.lastExpression]);
    const press = (action: CalculatorAction): void => {
        props.onPress(action);
    };
    const onKey = (event: KeyEvent): boolean => {
        if (event.ctrl || event.alt || event.meta) return false;
        const action = calculatorActionForKey(event.key); if (action === null) return false; press(action); return true;
    };
    const recall = (value: string): void => {
        props.onPress({ type: "clearEntry" });
        for (const character of value) {
            if (character === "-") props.onPress({ type: "sign" }); else if (character === ".") props.onPress({ type: "decimal" });
            else props.onPress({ type: "digit", digit: character });
        }
    };
    return <Grid rows="100,34,38,*,*,*,*,*,*" columns="*,*,*,*" onKeyDown={onKey}>
        <Border gridRow={0} gridColumnSpan={4}><ResultPanel expression={presentation.expression} display={state.display} status={presentation.status} /></Border>
        <Border gridRow={1} gridColumnSpan={4}><MemoryBar value={state.display} onRecall={recall} /></Border>
        {CALCULATOR_BUTTONS.map(definition => <CalculatorButton key={definition.id} definition={definition}
            active={definition.action.type === "operator" && state.pendingOperator === (definition.action as { type: "operator"; operator: Operator }).operator} onPress={press} />)}
    </Grid>;
}

const SCIENTIFIC_KEYS: readonly string[] = ["(", ")", "pi", "e", "sin(", "cos(", "tan(", "sqrt(", "7", "8", "9", "/", "4", "5", "6", "*", "1", "2", "3", "-", "0", ".", "^", "+", "log(", "ln(", "!", "%"];
function ScientificCalculatorView(props: HistorySinkProps): JSX.Element {
    const expressionState = useState<string>(""); const expression = expressionState[0]; const setExpression = expressionState[1];
    const displayState = useState<string>("0"); const display = displayState[0]; const setDisplay = displayState[1];
    const angleState = useState<AngleUnit>("deg"); const angle = angleState[0]; const setAngle = angleState[1];
    const notationState = useState<boolean>(false); const scientificNotation = notationState[0]; const setScientificNotation = notationState[1];
    const append = (value: string): void => setExpression(expression + value);
    const calculate = (): void => {
        try { const value = evaluateExpression(expression, { angleUnit: angle }); const formatted = formatApproximate(value, scientificNotation); setDisplay(formatted); props.onHistory(expression + " = " + formatted); }
        catch (_error) { setDisplay("Error"); }
    };
    return <Grid rows="100,40,42,*,*,*,*,*,*,*" columns="*,*,*,*">
        <Border gridRow={0} gridColumnSpan={4}><ResultPanel expression={expression} display={display} status={angle.toUpperCase() + (scientificNotation ? " · scientific notation" : "")} /></Border>
        <ComboBox key="angle-unit" gridRow={1} gridColumn={0} gridColumnSpan={2} items={["DEG", "RAD", "GRAD"]} selectedIndex={angle === "deg" ? 0 : angle === "rad" ? 1 : 2} automationName="Angle unit" onSelectionChanged={index => setAngle(index === 1 ? "rad" : index === 2 ? "grad" : "deg")} />
        <Button key="notation" gridRow={1} gridColumn={2} automationName="Toggle scientific notation" onClick={() => setScientificNotation(!scientificNotation)}>F-E</Button>
        <Button key="scientific-clear" gridRow={1} gridColumn={3} automationName="Clear" onClick={() => { setExpression(""); setDisplay("0"); }}>C</Button>
        {SCIENTIFIC_KEYS.map((label, index) => <Button key={"scientific-" + index} gridRow={2 + Math.floor(index / 4)} gridColumn={index % 4} margin={3} minHeight={40} automationName={label} onClick={() => append(label)}>{label === "pi" ? "π" : label}</Button>)}
        <Button key="scientific-equals" gridRow={9} gridColumn={0} gridColumnSpan={4} background="#2563eb" foreground="white" automationName="Equals" onClick={calculate}>=</Button>
    </Grid>;
}

const PROGRAMMER_DIGITS: readonly string[] = ["A", "B", "C", "D", "E", "F", "7", "8", "9", "4", "5", "6", "1", "2", "3", "0"];
const PROGRAMMER_OPERATORS: readonly ProgrammerBinaryOperator[] = ["and", "or", "xor", "lsh", "rsh", "rol", "ror", "+", "-", "*", "/", "mod"];
function ProgrammerCalculatorView(props: HistorySinkProps): JSX.Element {
    const reducerState = useReducer<ProgrammerState, ProgrammerAction>(programmerReducer, initialProgrammerState);
    const state = reducerState[0]; const dispatch = reducerState[1]; const readouts = programmerReadouts(state);
    const equals = (): void => { const next = programmerReducer(state, { type: "equals" }); dispatch({ type: "equals" }); if (next.error === "") props.onHistory(state.entry + " = " + next.entry); };
    return <Grid rows="96,72,38,*,*,*,*,*,*" columns="*,*,*,*">
        <Border gridRow={0} gridColumnSpan={4}><ResultPanel expression={state.pendingOperator === null ? "" : String(state.pendingOperator)} display={state.error === "" ? state.entry : "Error"} status={state.error === "" ? state.wordSize + "-bit" : state.error} /></Border>
        <StackPanel gridRow={1} gridColumnSpan={4} spacing={1}><TextBlock automationName="Hexadecimal value">HEX  {readouts.HEX}</TextBlock><TextBlock automationName="Decimal value">DEC  {readouts.DEC}</TextBlock><TextBlock automationName="Octal value">OCT  {readouts.OCT}</TextBlock><TextBlock automationName="Binary value">BIN  {readouts.BIN}</TextBlock></StackPanel>
        <ComboBox key="programmer-base" gridRow={2} gridColumn={0} gridColumnSpan={2} items={["HEX", "DEC", "OCT", "BIN"]} selectedIndex={state.base === 16 ? 0 : state.base === 10 ? 1 : state.base === 8 ? 2 : 3} automationName="Number base" onSelectionChanged={index => dispatch({ type: "base", base: index === 0 ? 16 : index === 2 ? 8 : index === 3 ? 2 : 10 })} />
        <ComboBox key="programmer-word" gridRow={2} gridColumn={2} gridColumnSpan={2} items={["QWORD", "DWORD", "WORD", "BYTE"]} selectedIndex={state.wordSize === 64 ? 0 : state.wordSize === 32 ? 1 : state.wordSize === 16 ? 2 : 3} automationName="Word size" onSelectionChanged={index => dispatch({ type: "wordSize", wordSize: index === 1 ? 32 : index === 2 ? 16 : index === 3 ? 8 : 64 })} />
        {PROGRAMMER_DIGITS.map((digit, index) => { const value = digit >= "0" && digit <= "9" ? digit.charCodeAt(0) - 48 : digit.charCodeAt(0) - 55; return <Button key={"programmer-digit-" + digit} gridRow={3 + Math.floor(index / 4)} gridColumn={index % 4} margin={2} isEnabled={value < state.base} automationName={"Programmer digit " + digit} onClick={() => dispatch({ type: "digit", digit })}>{digit}</Button>; })}
        <ComboBox key="programmer-operator" gridRow={7} gridColumn={0} gridColumnSpan={2} items={PROGRAMMER_OPERATORS} selectedIndex={0} automationName="Programmer operator" onSelectionChanged={index => dispatch({ type: "operator", operator: PROGRAMMER_OPERATORS[index] === undefined ? "and" : PROGRAMMER_OPERATORS[index] })} />
        <Button key="programmer-not" gridRow={7} gridColumn={2} automationName="Bitwise not" onClick={() => dispatch({ type: "not" })}>NOT</Button><Button key="programmer-equals" gridRow={7} gridColumn={3} background="#2563eb" foreground="white" automationName="Equals" onClick={equals}>=</Button>
        <Button key="programmer-clear" gridRow={8} gridColumn={0} gridColumnSpan={2} automationName="Clear" onClick={() => dispatch({ type: "clear" })}>C</Button><Button key="toggle-high-bit" gridRow={8} gridColumn={2} gridColumnSpan={2} automationName="Toggle highest bit" onClick={() => dispatch({ type: "toggleBit", bit: state.wordSize - 1 })}>Toggle bit {state.wordSize - 1}</Button>
    </Grid>;
}

function DateCalculatorView(): JSX.Element {
    const today = new Date().toISOString().slice(0, 10);
    const fromState = useState<string>(today); const from = fromState[0]; const setFrom = fromState[1]; const toState = useState<string>(today); const to = toState[0]; const setTo = toState[1];
    const yearsState = useState<number>(0); const years = yearsState[0]; const setYears = yearsState[1]; const monthsState = useState<number>(0); const months = monthsState[0]; const setMonths = monthsState[1]; const daysState = useState<number>(0); const days = daysState[0]; const setDays = daysState[1];
    const subtractState = useState<boolean>(false); const subtract = subtractState[0]; const setSubtract = subtractState[1];
    const duration = durationBetweenDates(from, to); const added = addCalendarDuration(from, { years, months, days }, subtract);
    return <ScrollViewer><StackPanel spacing={12}><TextBlock fontSize={22} fontWeight="bold">Difference between dates</TextBlock><TextBlock>From</TextBlock><DatePicker key="date-from" automationName="From date" value={from} onValueChanged={value => setFrom(value === null ? today : value)} /><TextBlock>To</TextBlock><DatePicker key="date-to" automationName="To date" value={to} onValueChanged={value => setTo(value === null ? today : value)} />
        <TextBlock key="date-difference" automationName="Date difference" fontSize={18}>{Math.abs(daysBetweenDates(from, to)) + " days · " + duration.years + " years, " + duration.months + " months, " + duration.days + " days"}</TextBlock><TextBlock fontSize={22} fontWeight="bold">{subtract ? "Subtract from a date" : "Add to a date"}</TextBlock>
        <Button key="date-operation" automationName="Toggle date operation" onClick={() => setSubtract(!subtract)}>{subtract ? "Switch to add" : "Switch to subtract"}</Button>
        <Grid columns="*,*,*"><NumericUpDown key="date-years" gridColumn={0} minimum={0} maximum={999} value={years} automationName="Years" onValueChanged={value => setYears(value === null ? 0 : value)} /><NumericUpDown key="date-months" gridColumn={1} minimum={0} maximum={999} value={months} automationName="Months" onValueChanged={value => setMonths(value === null ? 0 : value)} /><NumericUpDown key="date-days" gridColumn={2} minimum={0} maximum={99999} value={days} automationName="Days" onValueChanged={value => setDays(value === null ? 0 : value)} /></Grid>
        <TextBlock key="date-result" automationName="Date result" fontSize={20}>{added}</TextBlock></StackPanel></ScrollViewer>;
}

const CURRENCIES: readonly string[] = ["USD", "EUR", "GBP", "JPY", "CAD", "AUD"];
function ConverterView(): JSX.Element {
    const familyState = useState<number>(0); const familyIndex = familyState[0]; const setFamilyIndex = familyState[1]; const fromState = useState<number>(0); const fromIndex = fromState[0]; const setFromIndex = fromState[1]; const toState = useState<number>(1); const toIndex = toState[0]; const setToIndex = toState[1]; const inputState = useState<string>("1"); const input = inputState[0]; const setInput = inputState[1];
    const currencyMode = familyIndex === UNIT_FAMILIES.length;
    const family = UNIT_FAMILIES[familyIndex] === undefined ? UNIT_FAMILIES[0] : UNIT_FAMILIES[familyIndex];
    const categoryNames = [...UNIT_FAMILIES.map(item => item.name), "Currency"];
    const unitNames = currencyMode ? CURRENCIES : family.units.map(unit => unit.name + " (" + unit.symbol + ")");
    const safeFrom = Math.min(fromIndex, unitNames.length - 1); const safeTo = Math.min(toIndex, unitNames.length - 1);
    const numeric = Number.parseFloat(input);
    const converted = currencyMode
        ? convertCurrency(numeric, CURRENCIES[safeFrom], CURRENCIES[safeTo], OFFLINE_CURRENCY_RATES)
        : convertUnit(numeric, family, safeFrom, safeTo);
    const output = Number.isFinite(numeric) ? formatApproximate(converted) : "Error";
    return <StackPanel spacing={12}><TextBlock fontSize={24} fontWeight="bold">{currencyMode ? "Currency converter" : "Unit converter"}</TextBlock><ComboBox key="converter-family" items={categoryNames} selectedIndex={familyIndex} automationName="Conversion category" onSelectionChanged={index => { setFamilyIndex(index); setFromIndex(0); setToIndex(1); }} /><TextBox key="converter-input" text={input} automationName="Conversion input" onTextChanged={setInput} /><ComboBox key="converter-from" items={unitNames} selectedIndex={safeFrom} automationName="From unit" onSelectionChanged={setFromIndex} /><Button key="converter-swap" automationName="Swap units" onClick={() => { const previous = fromIndex; setFromIndex(toIndex); setToIndex(previous); }}>⇅ Swap</Button><ComboBox key="converter-to" items={unitNames} selectedIndex={safeTo} automationName="To unit" onSelectionChanged={setToIndex} /><TextBlock key="converter-result" automationName="Conversion result" fontSize={28} fontWeight="semibold">{output}</TextBlock><TextBlock automationName="Conversion rate status" foreground="#64748b">{currencyMode ? OFFLINE_CURRENCY_RATES.updated + " · injectable rate provider supported" : family.name + " conversion"}</TextBlock></StackPanel>;
}

function GraphingView(): JSX.Element {
    const equationsState = useState<GraphEquation[]>([{ id: 1, expression: "y=x^2", color: GRAPH_COLORS[0], visible: true }]); const equations = equationsState[0]; const setEquations = equationsState[1]; const viewportState = useState<GraphViewport>(DEFAULT_VIEWPORT); const viewport = viewportState[0]; const setViewport = viewportState[1]; const traceState = useState<number>(0); const traceX = traceState[0]; const setTraceX = traceState[1];
    const commands = useMemo(() => graphCommands(equations, viewport), [equations, viewport]); const primary = equations[0]; const trace = traceEquation(primary.expression, traceX); const updatePrimary = (value: string): void => setEquations([{ ...primary, expression: value }, ...equations.slice(1)]); const zoom = (factor: number): void => setViewport({ ...viewport, scaleX: viewport.scaleX * factor, scaleY: viewport.scaleY * factor });
    return <Grid rows="40,44,*,44,44" columns="2*,*"><TextBox key="graph-equation" gridRow={0} gridColumn={0} text={primary.expression} automationName="Graph equation" onTextChanged={updatePrimary} /><Button key="graph-toggle" gridRow={0} gridColumn={1} background={primary.color} foreground="white" automationName="Toggle equation visibility" onClick={() => setEquations([{ ...primary, visible: !primary.visible }, ...equations.slice(1)])}>{primary.visible ? "Visible" : "Hidden"}</Button><Button key="graph-add" gridRow={1} gridColumn={0} gridColumnSpan={2} automationName="Add equation" onClick={() => setEquations([...equations, { id: equations.length + 1, expression: "y=sin(x)", color: GRAPH_COLORS[equations.length % GRAPH_COLORS.length], visible: true }])}>+ Add equation</Button><DrawingCanvas key="graph-canvas" gridRow={2} gridColumn={0} gridColumnSpan={2} width={viewport.width} height={viewport.height} automationName="Graph plot" commands={commands} /><Grid gridRow={3} gridColumn={0} gridColumnSpan={2} columns="*,*,*"><Button key="graph-zoom-in" gridColumn={0} automationName="Zoom in" onClick={() => zoom(1.25)}>Zoom in</Button><Button key="graph-reset" gridColumn={1} automationName="Reset graph view" onClick={() => setViewport(DEFAULT_VIEWPORT)}>Reset</Button><Button key="graph-zoom-out" gridColumn={2} automationName="Zoom out" onClick={() => zoom(0.8)}>Zoom out</Button></Grid><NumericUpDown key="graph-trace" gridRow={4} gridColumn={0} minimum={-100} maximum={100} increment={0.1} value={traceX} automationName="Trace x coordinate" onValueChanged={value => setTraceX(value === null ? 0 : value)} /><TextBlock key="graph-trace-result" gridRow={4} gridColumn={1} automationName="Trace coordinates">x={formatApproximate(trace.x)}, y={formatApproximate(trace.y)}</TextBlock></Grid>;
}

export type CalculatorMode = "Standard" | "Scientific" | "Programmer" | "Date Calculation" | "Converter" | "Graphing";
export const CALCULATOR_MODES: readonly CalculatorMode[] = ["Standard", "Scientific", "Programmer", "Date Calculation", "Converter", "Graphing"];
export function CalculatorApp(): JSX.Element {
    const modeState = useState<number>(0); const modeIndex = modeState[0]; const setModeIndex = modeState[1];
    const historyState = useState<string[]>([]); const history = historyState[0]; const setHistory = historyState[1];
    const historyVisibleState = useState<boolean>(false); const historyVisible = historyVisibleState[0]; const setHistoryVisible = historyVisibleState[1];
    const topmostState = useState<boolean>(false); const topmost = topmostState[0]; const setTopmost = topmostState[1];
    const standardReducer = useReducer<CalculatorState, CalculatorAction>(calculatorReducer, initialCalculatorState); const standardState = standardReducer[0]; const standardDispatch = standardReducer[1];
    const windowRef = useControlRef<WindowHandle>(); const mode = CALCULATOR_MODES[modeIndex] === undefined ? "Standard" : CALCULATOR_MODES[modeIndex];
    const addHistory = useCallback((entry: string): void => setHistory([entry, ...history]), [history]);
    const standardPress = (action: CalculatorAction): void => { const next = calculatorReducer(standardState, action); standardDispatch(action); if (action.type === "equals" && !next.error && next.lastExpression !== "") addHistory(next.lastExpression + " " + next.display); };
    const onKey = (event: KeyEvent): boolean => { if (mode !== "Standard" || event.ctrl || event.alt || event.meta) return false; const action = calculatorActionForKey(event.key); if (action === null) return false; standardPress(action); return true; };
    let content: JSX.Element; if (mode === "Scientific") content = <ScientificCalculatorView onHistory={addHistory} />; else if (mode === "Programmer") content = <ProgrammerCalculatorView onHistory={addHistory} />; else if (mode === "Date Calculation") content = <DateCalculatorView />; else if (mode === "Converter") content = <ConverterView />; else if (mode === "Graphing") content = <GraphingView />; else content = <StandardCalculatorView state={standardState} onPress={standardPress} onHistory={addHistory} />;
    return <Window ref={windowRef} title="SharpTS Calculator" width={topmost ? 430 : 720} height={topmost ? 640 : 760} minWidth={360} minHeight={560} canResize={true} topmost={topmost} theme="light" onKeyDown={onKey}><Border padding={14} background="#eef2f7"><Grid rows="44,*" columns={historyVisible && !topmost ? "3*,2*" : "*"}><Grid gridRow={0} gridColumn={0} columns="*,100,110"><ComboBox key="calculator-mode" gridColumn={0} items={CALCULATOR_MODES} selectedIndex={modeIndex} automationName="Calculator mode" onSelectionChanged={setModeIndex} isEnabled={!topmost} /><Button key="history-toggle" gridColumn={1} automationName="Toggle history" isVisible={!topmost} onClick={() => setHistoryVisible(!historyVisible)}>History</Button><Button key="topmost-toggle" gridColumn={2} automationName={topmost ? "Back to full view" : "Keep on top"} isVisible={mode === "Standard"} onClick={() => setTopmost(!topmost)}>{topmost ? "Full view" : "On top"}</Button></Grid><Border gridRow={1} gridColumn={0} padding={6}>{content}</Border><Border gridRow={0} gridRowSpan={2} gridColumn={1} padding={10} background="#ffffff" isVisible={historyVisible && !topmost} cornerRadius={8}><StackPanel spacing={8}><TextBlock fontSize={22} fontWeight="bold">History</TextBlock><Button key="history-clear" automationName="Clear history" isEnabled={history.length > 0} onClick={() => setHistory([])}>Clear history</Button><ScrollViewer><StackPanel spacing={6}>{history.map((entry, index) => <TextBlock key={"history-" + index} automationName={"History entry " + (index + 1)} textWrapping="wrap">{entry}</TextBlock>)}</StackPanel></ScrollViewer></StackPanel></Border></Grid></Border></Window>;
}

export function CalculatorShowcase(): JSX.Element {
    return <ErrorBoundary fallback={(error: unknown, reset: () => void) => { console.error("Calculator render failed: " + String(error)); return <Window title="SharpTS Calculator · Recovery" width={390} height={260} minWidth={330} minHeight={240} canResize={true} theme="light"><Border padding={24} background="#fff7ed"><StackPanel spacing={14}><TextBlock fontSize={22} fontWeight="bold" foreground="#9a3412">The calculator hit a snag</TextBlock><TextBlock textWrapping="wrap" foreground="#7c2d12">Your window is still open. Retry the calculation view when you are ready.</TextBlock><Button key="retry" automationName="Retry calculator" toolTip="Retry rendering" padding={10} background="#2563eb" foreground="white" cornerRadius={8} onClick={reset}>Retry</Button></StackPanel></Border></Window>; }}><CalculatorApp /></ErrorBoundary>;
}
let recoveryFailurePending = true;
function OneTimeRenderFailure(): JSX.Element { if (recoveryFailurePending) { recoveryFailurePending = false; throw new Error("Intentional Calculator recovery check"); } return <CalculatorApp />; }
export function CalculatorRecoveryDemo(): JSX.Element { return <ErrorBoundary fallback={(_error: unknown, reset: () => void) => <Window title="SharpTS Calculator · Recovery" width={390} height={260} minWidth={330} minHeight={240} canResize={true} theme="light"><Border padding={24} background="#fff7ed"><StackPanel spacing={14}><TextBlock automationName="Recovery message" fontSize={22} fontWeight="bold" foreground="#9a3412">The calculator hit a snag</TextBlock><Button key="retry" automationName="Retry calculator" padding={10} background="#2563eb" foreground="white" cornerRadius={8} onClick={reset}>Retry</Button></StackPanel></Border></Window>}><OneTimeRenderFailure /></ErrorBoundary>; }
