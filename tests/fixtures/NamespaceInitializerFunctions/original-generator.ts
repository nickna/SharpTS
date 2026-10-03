namespace Values {const base=4;function* read(){yield base;yield base+1;}export const result=[...read()].join(",");}console.log(Values.result);
