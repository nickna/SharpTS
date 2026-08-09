import { DesktopTestingBridge } from "dotnet:SharpTS.Gui";
import type { DesktopRoot } from "./runtime.ts";

export type DesktopTestProperty =
    "automationName" | "background" | "foreground" | "toolTip" | "isEnabled" | "isVisible";

export interface DesktopTestDriver {
    afterRender(callback: () => void): void;
    click(key: string): void;
    pressKey(key: string): void;
    getText(key: string): string;
    getProperty(key: string, property: DesktopTestProperty): string;
    setTextBoxValue(key: string, value: string): void;
    setCheckBoxValue(key: string, value: boolean): void;
    setComboBoxIndex(key: string, value: number): void;
    setSliderValue(key: string, value: number): void;
    dropText(key: string, value: string): string;
}

export function createDesktopTestDriver(root: DesktopRoot): DesktopTestDriver {
    const managed: any = (root as any).__managedRoot;
    if (managed === undefined || managed === null)
        throw new Error("The supplied value is not an active SharpTS desktop root.");
    return {
        afterRender(callback: () => void): void { DesktopTestingBridge.AfterRender(managed, callback); },
        click(key: string): void { DesktopTestingBridge.Click(managed, key); },
        pressKey(key: string): void { DesktopTestingBridge.PressKey(managed, key); },
        getText(key: string): string { return DesktopTestingBridge.GetText(managed, key); },
        getProperty(key: string, property: DesktopTestProperty): string {
            return DesktopTestingBridge.GetProperty(managed, key, property);
        },
        setTextBoxValue(key: string, value: string): void {
            DesktopTestingBridge.SetTextBoxValue(managed, key, value);
        },
        setCheckBoxValue(key: string, value: boolean): void {
            DesktopTestingBridge.SetCheckBoxValue(managed, key, value);
        },
        setComboBoxIndex(key: string, value: number): void {
            DesktopTestingBridge.SetComboBoxIndex(managed, key, value);
        },
        setSliderValue(key: string, value: number): void {
            DesktopTestingBridge.SetSliderValue(managed, key, value);
        },
        dropText(key: string, value: string): string {
            return DesktopTestingBridge.DropText(managed, key, value);
        },
    };
}
