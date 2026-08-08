import {
    Border,
    Button,
    Grid,
    TextBlock,
    Window,
    useCallback,
    useControlRef,
    useEffect,
    useMemo,
    useReducer,
    renderDesktop,
    WindowHandle,
    KeyEvent,
} from "@sharpts/gui";
import {
    CalculatorAction,
    CalculatorState,
    Operator,
    calculatorReducer,
    initialCalculatorState,
} from "./calculator";

interface CalculatorButtonProps {
    label: string;
    row: number;
    column: number;
    accent?: boolean;
    onPress: () => void;
}

function CalculatorButton(props: CalculatorButtonProps): JSX.Element {
    return (
        <Button
            key={props.label}
            gridRow={props.row}
            gridColumn={props.column}
            margin={4}
            minWidth={64}
            minHeight={52}
            fontSize={20}
            fontWeight="semibold"
            background={props.accent ? "#2563eb" : "#e5e7eb"}
            foreground={props.accent ? "white" : "#111827"}
            cornerRadius={8}
            automationName={props.label}
            onClick={props.onPress}
        >
            {props.label}
        </Button>
    );
}

function CalculatorApp(): JSX.Element {
    const reducerState = useReducer<CalculatorState, CalculatorAction>(calculatorReducer, initialCalculatorState);
    const state = reducerState[0];
    const dispatch = reducerState[1];
    const windowRef = useControlRef<WindowHandle>();
    useEffect(() => {
        windowRef.focus();
        return () => {};
    }, []);

    const display = useMemo(() => state.display, [state.display]);
    const send = useCallback((action: CalculatorAction): void => dispatch(action), [dispatch]);
    const digit = (value: string): void => send({ type: "digit", digit: value });
    const operation = (value: Operator): void => send({ type: "operator", operator: value });

    const onKey = (event: KeyEvent): boolean => {
        if (event.ctrl || event.alt || event.meta) return false;
        if (event.key >= "0" && event.key <= "9") digit(event.key);
        else if (event.key === "." || event.key === "Decimal") send({ type: "decimal" });
        else if (event.key === "+") operation("+");
        else if (event.key === "-") operation("-");
        else if (event.key === "*" || event.key.toLowerCase() === "x") operation("*");
        else if (event.key === "/") operation("/");
        else if (event.key === "Enter" || event.key === "=") send({ type: "equals" });
        else if (event.key === "%") send({ type: "percent" });
        else if (event.key === "Backspace" || event.key === "Delete") send({ type: "backspace" });
        else if (event.key === "Escape" || event.key.toLowerCase() === "c") send({ type: "clear" });
        else return false;
        return true;
    };

    return (
        <Window ref={windowRef} title="SharpTS Calculator" width={360} height={520} minWidth={320} minHeight={480} canResize={true} theme="light" onKeyDown={onKey}>
            <Border padding={16} background="#f8fafc">
                <Grid rows="120,*,*,*,*,*" columns="*,*,*,*">
                    <Border gridRow={0} gridColumn={0} gridColumnSpan={4} margin={4} padding={14} background="#111827" cornerRadius={10}>
                        <TextBlock horizontalAlignment="right" verticalAlignment="center" textAlignment="right" fontSize={38} fontWeight="medium" foreground="white" automationName="Display">
                            {display}
                        </TextBlock>
                    </Border>
                    <CalculatorButton label="C" row={1} column={0} onPress={() => send({ type: "clear" })} />
                    <CalculatorButton label="±" row={1} column={1} onPress={() => send({ type: "sign" })} />
                    <CalculatorButton label="%" row={1} column={2} onPress={() => send({ type: "percent" })} />
                    <CalculatorButton label="÷" row={1} column={3} accent={true} onPress={() => operation("/")} />
                    <CalculatorButton label="7" row={2} column={0} onPress={() => digit("7")} />
                    <CalculatorButton label="8" row={2} column={1} onPress={() => digit("8")} />
                    <CalculatorButton label="9" row={2} column={2} onPress={() => digit("9")} />
                    <CalculatorButton label="×" row={2} column={3} accent={true} onPress={() => operation("*")} />
                    <CalculatorButton label="4" row={3} column={0} onPress={() => digit("4")} />
                    <CalculatorButton label="5" row={3} column={1} onPress={() => digit("5")} />
                    <CalculatorButton label="6" row={3} column={2} onPress={() => digit("6")} />
                    <CalculatorButton label="−" row={3} column={3} accent={true} onPress={() => operation("-")} />
                    <CalculatorButton label="1" row={4} column={0} onPress={() => digit("1")} />
                    <CalculatorButton label="2" row={4} column={1} onPress={() => digit("2")} />
                    <CalculatorButton label="3" row={4} column={2} onPress={() => digit("3")} />
                    <CalculatorButton label="+" row={4} column={3} accent={true} onPress={() => operation("+")} />
                    <CalculatorButton label="0" row={5} column={0} onPress={() => digit("0")} />
                    <CalculatorButton label="." row={5} column={1} onPress={() => send({ type: "decimal" })} />
                    <CalculatorButton label="⌫" row={5} column={2} onPress={() => send({ type: "backspace" })} />
                    <CalculatorButton label="=" row={5} column={3} accent={true} onPress={() => send({ type: "equals" })} />
                </Grid>
            </Border>
        </Window>
    );
}

const root = renderDesktop(<CalculatorApp />);
if (process.env.SHARPTS_GUI_SMOKE_CLOSE === "1") {
    setTimeout((() => root.dispose()) as any, 25);
}
