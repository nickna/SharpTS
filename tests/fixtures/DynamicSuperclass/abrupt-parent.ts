// New #1907 negative control: rejected heritage cannot run static initialization.
let order = "";
async function main() {
    try {
        class Child extends (await Promise.reject<any>(new Error("parent"))) {
            static value = (order += "bad");
        }
    } catch (error: any) { console.log(error.message, order); }
}
main();
