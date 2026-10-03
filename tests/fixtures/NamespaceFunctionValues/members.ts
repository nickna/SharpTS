namespace MathBox {export const base=2;export function add(value:number){return value+base;}}const box:any=MathBox;console.log(MathBox.base,MathBox.add(3),box.base,box.add(4),box===MathBox);
