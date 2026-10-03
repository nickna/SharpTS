namespace Options {export enum Kind {First=2,Text="label",Last=5}}const options:any=Options;console.log(options.Kind[2],options.Kind[5],options.Kind.Text,options.Kind.label===undefined);
