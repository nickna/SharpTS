async function run(){await Promise.resolve(0);class C{static value=5;}return C.value;}run().then(v=>console.log(v),e=>console.log("rejected",e.message));
