namespace First {export const value=3;}namespace Second {export const value=4;}const first:any=First;const second:any=Second;console.log(delete first.value,first.value===undefined,second.value);
