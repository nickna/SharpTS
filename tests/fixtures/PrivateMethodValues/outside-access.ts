class Box {
    #read(): number { return 3; }
}
const box = new Box();
console.log(box.#read);
