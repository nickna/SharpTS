namespace Inner {
    interface NumberConstructor { unrelated: string; }
    const predicate: (value:unknown)=>boolean=Number.isNaN;
    console.log(predicate(NaN),predicate.name,predicate.length);
}
