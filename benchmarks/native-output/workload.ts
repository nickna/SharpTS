function kernel(count: number): number {
    let checksum = 0;
    for (let index = 0; index < count; index++) checksum = (checksum + index) % 1000003;
    return checksum;
}

if (process.argv[2] === "steady") {
    const count = 2000000;
    for (let warmup = 0; warmup < 5; warmup++) kernel(count);
    for (let sample = 0; sample < 9; sample++) {
        const start = Date.now();
        const checksum = kernel(count);
        console.log(JSON.stringify({ milliseconds: Date.now() - start, checksum }));
    }
} else console.log("ready");
