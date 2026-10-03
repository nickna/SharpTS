namespace N{export enum E{A=1,B=A<<1,C=A|B}export const original=E;}const ns:any=N;const alias:any=ns.E;console.log(alias===ns.original,alias===N.E,alias[3],N.E.C);
