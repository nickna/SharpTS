import {
    Border,
    Button,
    ErrorBoundary,
    Grid,
    StackPanel,
    TextBlock,
    Window,
    useCallback,
    useControlRef,
    useEffect,
    useMemo,
    useReducer,
    WindowHandle,
    KeyEvent,
} from "@sharpts/gui";
import {
    CalculatorAction,
    CalculatorState,
    Operator,
    calculatorActionForKey,
    calculatorReducer,
    deriveCalculatorPresentation,
    initialCalculatorState,
} from "./calculator";

export type CalculatorButtonRole = "digit" | "utility" | "operator" | "equal";

export interface CalculatorButtonDefinition {
    readonly id: string;
    readonly label: string;
    readonly automationName: string;
    readonly shortcut: string;
    readonly role: CalculatorButtonRole;
    readonly row: number;
    readonly column: number;
    readonly action: CalculatorAction;
}

export const CALCULATOR_BUTTONS: CalculatorButtonDefinition[] = [
    { id: "clear", label: "C", automationName: "Clear", shortcut: "C or Escape", role: "utility", row: 2, column: 0, action: { type: "clear" } },
    { id: "sign", label: "±", automationName: "Change sign", shortcut: "Mouse", role: "utility", row: 2, column: 1, action: { type: "sign" } },
    { id: "percent", label: "%", automationName: "Percent", shortcut: "%", role: "utility", row: 2, column: 2, action: { type: "percent" } },
    { id: "divide", label: "÷", automationName: "Divide", shortcut: "/", role: "operator", row: 2, column: 3, action: { type: "operator", operator: "/" } },
    { id: "digit-7", label: "7", automationName: "Seven", shortcut: "7", role: "digit", row: 3, column: 0, action: { type: "digit", digit: "7" } },
    { id: "digit-8", label: "8", automationName: "Eight", shortcut: "8", role: "digit", row: 3, column: 1, action: { type: "digit", digit: "8" } },
    { id: "digit-9", label: "9", automationName: "Nine", shortcut: "9", role: "digit", row: 3, column: 2, action: { type: "digit", digit: "9" } },
    { id: "multiply", label: "×", automationName: "Multiply", shortcut: "* or X", role: "operator", row: 3, column: 3, action: { type: "operator", operator: "*" } },
    { id: "digit-4", label: "4", automationName: "Four", shortcut: "4", role: "digit", row: 4, column: 0, action: { type: "digit", digit: "4" } },
    { id: "digit-5", label: "5", automationName: "Five", shortcut: "5", role: "digit", row: 4, column: 1, action: { type: "digit", digit: "5" } },
    { id: "digit-6", label: "6", automationName: "Six", shortcut: "6", role: "digit", row: 4, column: 2, action: { type: "digit", digit: "6" } },
    { id: "subtract", label: "−", automationName: "Subtract", shortcut: "-", role: "operator", row: 4, column: 3, action: { type: "operator", operator: "-" } },
    { id: "digit-1", label: "1", automationName: "One", shortcut: "1", role: "digit", row: 5, column: 0, action: { type: "digit", digit: "1" } },
    { id: "digit-2", label: "2", automationName: "Two", shortcut: "2", role: "digit", row: 5, column: 1, action: { type: "digit", digit: "2" } },
    { id: "digit-3", label: "3", automationName: "Three", shortcut: "3", role: "digit", row: 5, column: 2, action: { type: "digit", digit: "3" } },
    { id: "add", label: "+", automationName: "Add", shortcut: "+", role: "operator", row: 5, column: 3, action: { type: "operator", operator: "+" } },
    { id: "digit-0", label: "0", automationName: "Zero", shortcut: "0", role: "digit", row: 6, column: 0, action: { type: "digit", digit: "0" } },
    { id: "decimal", label: ".", automationName: "Decimal point", shortcut: ".", role: "digit", row: 6, column: 1, action: { type: "decimal" } },
    { id: "backspace", label: "⌫", automationName: "Backspace", shortcut: "Backspace or Delete", role: "utility", row: 6, column: 2, action: { type: "backspace" } },
    { id: "equals", label: "=", automationName: "Equals", shortcut: "Enter or =", role: "equal", row: 6, column: 3, action: { type: "equals" } },
];

export interface CalculatorButtonProps {
    readonly definition: CalculatorButtonDefinition;
    readonly active: boolean;
    readonly onPress: (action: CalculatorAction) => void;
}

function buttonBackground(role: CalculatorButtonRole, active: boolean): string {
    if (active) return "#b45309";
    if (role === "operator") return "#f59e0b";
    if (role === "equal") return "#2563eb";
    if (role === "utility") return "#dbe4ee";
    return "#ffffff";
}

export function CalculatorButton(props: CalculatorButtonProps): JSX.Element {
    const definition = props.definition;
    const dark = definition.role === "operator" || definition.role === "equal" || props.active;
    return (
        <Button
            gridRow={definition.row}
            gridColumn={definition.column}
            margin={5}
            minWidth={58}
            minHeight={54}
            fontSize={20}
            fontWeight="semibold"
            background={buttonBackground(definition.role, props.active)}
            foreground={dark ? "white" : "#172033"}
            cornerRadius={10}
            automationName={definition.automationName}
            toolTip={definition.automationName + " · " + definition.shortcut}
            onClick={() => props.onPress(definition.action)}
        >{definition.label}</Button>
    );
}

export function CalculatorApp(): JSX.Element {
    const reducerState = useReducer<CalculatorState, CalculatorAction>(calculatorReducer, initialCalculatorState);
    const state = reducerState[0];
    const dispatch = reducerState[1];
    const windowRef = useControlRef<WindowHandle>();
    useEffect(() => { windowRef.focus(); }, []);

    const presentation = useMemo(() => deriveCalculatorPresentation(state), [
        state.display,
        state.accumulator,
        state.pendingOperator,
        state.waitingForOperand,
        state.repeatOperator,
        state.error,
        state.lastExpression,
    ]);
    const send = useCallback((action: CalculatorAction): void => dispatch(action), [dispatch]);
    const onKey = (event: KeyEvent): boolean => {
        if (event.ctrl || event.alt || event.meta) return false;
        const action = calculatorActionForKey(event.key);
        if (action === null) return false;
        send(action);
        return true;
    };

    return (
        <Window ref={windowRef} title="SharpTS Calculator" width={390} height={610} minWidth={330} minHeight={520} canResize={true} theme="light" onKeyDown={onKey}>
            <Border padding={18} background="#eef2f7">
                <Grid rows="106,28,*,*,*,*,*" columns="*,*,*,*">
                    <Border gridRow={0} gridColumn={0} gridColumnSpan={4} margin={5} padding={16} background="#111827" cornerRadius={12}>
                        <StackPanel spacing={6}>
                            <TextBlock key="expression" horizontalAlignment="right" textAlignment="right" fontSize={16} foreground="#a8b3c7" automationName="Expression">{presentation.expression === "" ? " " : presentation.expression}</TextBlock>
                            <TextBlock key="display" horizontalAlignment="right" textAlignment="right" fontSize={38} fontWeight="semibold" foreground="white" automationName="Display">{state.display}</TextBlock>
                        </StackPanel>
                    </Border>
                    <TextBlock key="status" gridRow={1} gridColumn={0} gridColumnSpan={4} margin={6} fontSize={13} foreground="#536079" automationName="Status">{presentation.status}</TextBlock>
                    {CALCULATOR_BUTTONS.map(definition => (
                        <CalculatorButton
                            key={definition.id}
                            definition={definition}
                            active={definition.action.type === "operator" && state.pendingOperator === (definition.action as { type: "operator"; operator: Operator }).operator}
                            onPress={send}
                        />
                    ))}
                </Grid>
            </Border>
        </Window>
    );
}

export function CalculatorShowcase(): JSX.Element {
    return (
        <ErrorBoundary fallback={(error: unknown, reset: () => void) => {
            console.error("Calculator render failed: " + String(error));
            return (
            <Window title="SharpTS Calculator · Recovery" width={390} height={260} minWidth={330} minHeight={240} canResize={true} theme="light">
                <Border padding={24} background="#fff7ed">
                    <StackPanel spacing={14}>
                        <TextBlock fontSize={22} fontWeight="bold" foreground="#9a3412">The calculator hit a snag</TextBlock>
                        <TextBlock textWrapping="wrap" foreground="#7c2d12">Your window is still open. Retry the calculation view when you are ready.</TextBlock>
                        <Button key="retry" automationName="Retry calculator" toolTip="Retry rendering" padding={10} background="#2563eb" foreground="white" cornerRadius={8} onClick={reset}>Retry</Button>
                    </StackPanel>
                </Border>
            </Window>
            );
        }}>
            <CalculatorApp />
        </ErrorBoundary>
    );
}

let recoveryFailurePending = true;

function OneTimeRenderFailure(): JSX.Element {
    if (recoveryFailurePending) {
        recoveryFailurePending = false;
        throw new Error("Intentional Calculator recovery check");
    }
    return <CalculatorApp />;
}

export function CalculatorRecoveryDemo(): JSX.Element {
    return (
        <ErrorBoundary fallback={(_error: unknown, reset: () => void) => (
            <Window title="SharpTS Calculator · Recovery" width={390} height={260} minWidth={330} minHeight={240} canResize={true} theme="light">
                <Border padding={24} background="#fff7ed">
                    <StackPanel spacing={14}>
                        <TextBlock automationName="Recovery message" fontSize={22} fontWeight="bold" foreground="#9a3412">The calculator hit a snag</TextBlock>
                        <Button key="retry" automationName="Retry calculator" padding={10} background="#2563eb" foreground="white" cornerRadius={8} onClick={reset}>Retry</Button>
                    </StackPanel>
                </Border>
            </Window>
        )}>
            <OneTimeRenderFailure />
        </ErrorBoundary>
    );
}
