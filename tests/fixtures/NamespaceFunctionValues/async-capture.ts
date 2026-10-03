namespace AsyncBox {const base=6;export async function read(){await Promise.resolve(0);return base+1;}}AsyncBox.read().then(value=>console.log(value));
