import { DesktopTestingBridge } from "dotnet:SharpTS.Gui";
import type { DesktopWindow } from "./runtime.ts";

/** Native property that can be read through DesktopTestDriver. @category Testing */
export type DesktopTestProperty =
    "automationName" | "background" | "foreground" | "toolTip" | "isEnabled" | "isVisible";

/** Headless driver for locating and interacting with controls by their key. @category Testing */
export interface DesktopTestDriver {
    /** Runs a callback after all pending render and native commit work completes. */
    afterRender(callback: () => void): void;
    /** Activates the keyed control. */
    click(key: string): void;
    /** Sends a normalized key press to the keyed control. */
    pressKey(key: string): void;
    /** Reads the keyed control's text content. @returns The normalized text content. */
    getText(key: string): string;
    /** Reads a supported native property from the keyed control. @returns The normalized property value. */
    getProperty(key: string, property: DesktopTestProperty): string;
    /** Replaces the value of a keyed TextBox. */
    setTextBoxValue(key: string, value: string): void;
    /** Replaces the checked state of a keyed CheckBox. */
    setCheckBoxValue(key: string, value: boolean): void;
    /** Selects an item in a keyed ComboBox. */
    setComboBoxIndex(key: string, value: number): void;
    /** Replaces the numeric value of a keyed Slider. */
    setSliderValue(key: string, value: number): void;
    /** Simulates dropping text on a keyed control. @returns The drop effect selected by the control. */
    dropText(key: string, value: string): string;
}

/**
 * Creates a headless test driver for an active desktop window.
 * @param window - Live window whose native tree will be driven.
 * @returns A key-based desktop test driver.
 * @category Testing
 */
export function createDesktopTestDriver(window: DesktopWindow): DesktopTestDriver {
    const managed: any = (window as any).__managedRoot;
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
