enum Values {First=2,Second=3}function* names(){let key:number=3;yield Values[key];}console.log(names().next().value);
