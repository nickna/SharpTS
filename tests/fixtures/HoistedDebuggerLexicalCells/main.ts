import { marker } from "./helper";
const delay = () => new Promise<number>((resolve): void => { setTimeout(() => resolve(0), 10); });
async function loop() {
    const readers: any[] = [];
    for (let index = 0; index < 2; index++) {
        readers.push(() => index);
        console.log("loop-before", index); // @break:loop-before
        {
            let index = 98;
            const inner = () => index;
            index++;
            await delay();
            console.log("loop-inside", inner(), index); // @break:loop-inside
        }
        console.log("loop-after", index); // @break:loop-after
    }
    console.log("loop-done", readers[0](), readers[1](), marker); // @break:loop-done
}
async function parameter(value: number) {
    console.log("parameter-before", value); // @break:parameter-before
    {
        let value = 98;
        const inner = () => value;
        value++;
        await delay();
        console.log("parameter-inside", inner(), value); // @break:parameter-inside
    }
    console.log("parameter-after", value); // @break:parameter-after
}
async function main() { await loop(); await parameter(11); }
main();
