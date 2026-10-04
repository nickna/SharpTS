function* strings(){yield "wrong";}const source:number[]=[1,2];source[Symbol.iterator]=strings;
