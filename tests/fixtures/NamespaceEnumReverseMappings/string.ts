namespace Options {export enum Kind {First="a",Second="b"}}const options:any=Options;console.log(options.Kind.First,options.Kind.Second,options.Kind["a"]===undefined,options.Kind[0]===undefined);
