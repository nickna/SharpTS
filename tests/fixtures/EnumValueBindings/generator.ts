enum Values {First=2,Second=3}function* names(key:number){yield Values[key];}console.log(names(3).next().value);
