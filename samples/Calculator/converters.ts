export interface UnitDefinition {
    readonly name: string;
    readonly symbol: string;
    readonly toBase: (value: number) => number;
    readonly fromBase: (value: number) => number;
}

export interface UnitFamily { readonly name: string; readonly units: readonly UnitDefinition[]; }

function linear(name: string, symbol: string, factor: number): UnitDefinition {
    return { name, symbol, toBase: value => value * factor, fromBase: value => value / factor };
}

function temperature(name: string, symbol: string, toKelvin: (value: number) => number, fromKelvin: (value: number) => number): UnitDefinition {
    return { name, symbol, toBase: toKelvin, fromBase: fromKelvin };
}

export const UNIT_FAMILIES: readonly UnitFamily[] = [
    { name: "Length", units: [linear("Meters", "m", 1), linear("Kilometers", "km", 1000), linear("Centimeters", "cm", 0.01), linear("Feet", "ft", 0.3048), linear("Inches", "in", 0.0254), linear("Miles", "mi", 1609.344)] },
    { name: "Mass", units: [linear("Kilograms", "kg", 1), linear("Grams", "g", 0.001), linear("Metric tonnes", "t", 1000), linear("Pounds", "lb", 0.45359237), linear("Ounces", "oz", 0.028349523125)] },
    { name: "Temperature", units: [temperature("Celsius", "°C", value => value + 273.15, value => value - 273.15), temperature("Fahrenheit", "°F", value => (value - 32) * 5 / 9 + 273.15, value => (value - 273.15) * 9 / 5 + 32), temperature("Kelvin", "K", value => value, value => value)] },
    { name: "Area", units: [linear("Square meters", "m²", 1), linear("Square kilometers", "km²", 1000000), linear("Square feet", "ft²", 0.09290304), linear("Acres", "ac", 4046.8564224), linear("Hectares", "ha", 10000)] },
    { name: "Volume", units: [linear("Liters", "L", 1), linear("Milliliters", "mL", 0.001), linear("Cubic meters", "m³", 1000), linear("US gallons", "gal", 3.785411784), linear("Cups", "cup", 0.2365882365)] },
    { name: "Speed", units: [linear("Meters per second", "m/s", 1), linear("Kilometers per hour", "km/h", 1 / 3.6), linear("Miles per hour", "mph", 0.44704), linear("Knots", "kn", 0.514444444444)] },
    { name: "Time", units: [linear("Seconds", "s", 1), linear("Minutes", "min", 60), linear("Hours", "h", 3600), linear("Days", "d", 86400), linear("Weeks", "wk", 604800)] },
    { name: "Energy", units: [linear("Joules", "J", 1), linear("Kilojoules", "kJ", 1000), linear("Calories", "cal", 4.184), linear("Kilowatt-hours", "kWh", 3600000), linear("BTU", "BTU", 1055.05585262)] },
    { name: "Power", units: [linear("Watts", "W", 1), linear("Kilowatts", "kW", 1000), linear("Horsepower", "hp", 745.699871582)] },
    { name: "Pressure", units: [linear("Pascals", "Pa", 1), linear("Kilopascals", "kPa", 1000), linear("Bar", "bar", 100000), linear("PSI", "psi", 6894.757293168), linear("Atmospheres", "atm", 101325)] },
    { name: "Angle", units: [linear("Degrees", "°", Math.PI / 180), linear("Radians", "rad", 1), linear("Gradians", "grad", Math.PI / 200)] },
    { name: "Data", units: [linear("Bytes", "B", 1), linear("Kilobytes", "KB", 1000), linear("Megabytes", "MB", 1000000), linear("Gigabytes", "GB", 1000000000), linear("Kibibytes", "KiB", 1024), linear("Mebibytes", "MiB", 1048576)] },
];

export function convertUnit(value: number, family: UnitFamily, fromIndex: number, toIndex: number): number {
    const from = family.units[fromIndex];
    const to = family.units[toIndex];
    if (from === undefined || to === undefined) throw new Error("Unknown conversion unit");
    return to.fromBase(from.toBase(value));
}

export interface CurrencyRates { readonly base: string; readonly updated: string; readonly rates: Readonly<Record<string, number>>; }
export interface CurrencyRateProvider { load(): Promise<CurrencyRates>; }

export const OFFLINE_CURRENCY_RATES: CurrencyRates = {
    base: "USD", updated: "Offline reference rates", rates: { USD: 1, EUR: 0.92, GBP: 0.79, JPY: 149.5, CAD: 1.36, AUD: 1.52 },
};

export function convertCurrency(value: number, from: string, to: string, rates: CurrencyRates): number {
    const fromRate = rates.rates[from];
    const toRate = rates.rates[to];
    if (fromRate === undefined || toRate === undefined || fromRate === 0) throw new Error("Currency rate unavailable");
    return value / fromRate * toRate;
}
