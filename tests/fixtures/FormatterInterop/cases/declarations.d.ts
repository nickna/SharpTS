/** formatter-comment: declaration signatures and defaults survive formatting. */
export interface FormatterOptions<T extends string=string>{readonly label:T;optional?:number;callback:(value:T)=>T;}
export type Pair<T> = readonly[T,T];
export declare namespace FormatterApi{function normalize<T extends string>(value:T,options?:FormatterOptions<T>):T;function normalize(value:number):string;}
