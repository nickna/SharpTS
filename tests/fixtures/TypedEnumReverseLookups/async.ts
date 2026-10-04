enum Values {First=2,Second=3}async function read(key:number){return Values[key];}read(3).then(value=>console.log(value));
