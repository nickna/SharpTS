namespace Options {export enum Kind {Negative=-2,Fraction=0.5,Zero=-0}}const options:any=Options;console.log(options.Kind[-2],options.Kind[0.5],options.Kind[0],options.Kind["0.5"]);
