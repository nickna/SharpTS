export interface CalendarDuration { readonly years: number; readonly months: number; readonly days: number; }

function utcDate(value: string): any {
    const parts = value.split("-");
    if (parts.length !== 3) throw new Error("Date must use YYYY-MM-DD");
    return new Date(Date.UTC(Number.parseInt(parts[0], 10), Number.parseInt(parts[1], 10) - 1, Number.parseInt(parts[2], 10)));
}

export function formatIsoDate(value: any): string { return value.toISOString().slice(0, 10); }

export function daysBetweenDates(from: string, to: string): number {
    return Math.round((utcDate(to).getTime() - utcDate(from).getTime()) / 86400000);
}

export function durationBetweenDates(from: string, to: string): CalendarDuration {
    let start: any = utcDate(from);
    let end: any = utcDate(to);
    if (end.getTime() < start.getTime()) { const swap = start; start = end; end = swap; }
    let years = end.getUTCFullYear() - start.getUTCFullYear();
    let months = end.getUTCMonth() - start.getUTCMonth();
    let days = end.getUTCDate() - start.getUTCDate();
    if (days < 0) {
        months--;
        days += new Date(Date.UTC(end.getUTCFullYear(), end.getUTCMonth(), 0)).getUTCDate();
    }
    if (months < 0) { years--; months += 12; }
    return { years, months, days };
}

export function addCalendarDuration(source: string, duration: CalendarDuration, subtract: boolean = false): string {
    const date = utcDate(source);
    const direction = subtract ? -1 : 1;
    const originalDay = date.getUTCDate();
    date.setUTCDate(1);
    date.setUTCFullYear(date.getUTCFullYear() + direction * duration.years);
    date.setUTCMonth(date.getUTCMonth() + direction * duration.months);
    const lastDay = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + 1, 0)).getUTCDate();
    date.setUTCDate(Math.min(originalDay, lastDay));
    date.setUTCDate(date.getUTCDate() + direction * duration.days);
    return formatIsoDate(date);
}
