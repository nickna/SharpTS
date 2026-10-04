async function run(){class C{static value=5;}const value=C.value;await Promise.resolve(0);return value;}run().then(v=>console.log(v));
