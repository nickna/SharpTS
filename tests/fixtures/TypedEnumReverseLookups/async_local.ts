enum Values {First=2,Second=3}async function read(){let key:number=3;return Values[key];}read().then(value=>console.log(value));
