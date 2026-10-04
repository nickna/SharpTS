const source:number[]=[1,2];source[Symbol.iterator]=()=>({next(){return {value:8,done:false as const};}});
