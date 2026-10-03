export enum Values{First=2,Second=3}export function* names(){const alias:any=Values;yield alias[2];yield alias===Values;}
