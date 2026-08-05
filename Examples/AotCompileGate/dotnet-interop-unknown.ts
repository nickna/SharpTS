import { FileInfo } from "dotnet:System.IO.FileInfo";

console.log(new FileInfo("not-created.txt").name);
