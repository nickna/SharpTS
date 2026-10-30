@DotNetType('System.Text.StringBuilder')
declare class Builder {
    constructor();
    append(value: string): Builder;
    toString(): string;
    length: number;
}
const builder: Builder = new Builder();
builder.append('native');
console.log(builder.toString(), builder.length);

@DotNetType('System.Runtime.CompilerServices.RuntimeFeature')
declare class RuntimeFeatures {
    static isDynamicCodeSupported: boolean;
}
console.log(RuntimeFeatures.isDynamicCodeSupported);
