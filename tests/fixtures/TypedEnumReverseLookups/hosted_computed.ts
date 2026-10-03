enum Flags {One=1,Two=2,All=One|Two}export function read(key:number):string{return Flags[key];}
