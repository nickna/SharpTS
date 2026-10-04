namespace Inner {
    interface NumberConstructor { unrelated: string; }
    export function check():boolean {
        const predicate: (value:unknown)=>boolean=Number.isNaN;
        return predicate(NaN);
    }
}
console.log(Inner.check());
