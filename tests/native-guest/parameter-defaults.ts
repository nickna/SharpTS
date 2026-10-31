const later = 99;
function invoke(read: () => number): number { return read(); }
function* values(first: number = invoke(() => later), later: number = 2) {
    yield first;
}
try {
    const iterator = values();
    console.log("created", iterator.next().value);
} catch (error: any) {
    console.log(error.name);
}
async function run(first: number = invoke(() => later), later: number = 3) {
    return first;
}
run().then(value => console.log("resolved", value), error => console.log(error.name));
