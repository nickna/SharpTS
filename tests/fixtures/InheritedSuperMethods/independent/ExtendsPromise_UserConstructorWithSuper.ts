class Tracked<T> extends Promise<T> {
                created: string;
                constructor(executor: (resolve: (v: T) => void, reject: (r: any) => void) => void) {
                    super(executor);
                    this.created = "yes";
                }
            }
            async function main() {
                const t = new Tracked<string>((resolve) => resolve("v"));
                console.log(t.created);
                console.log(t instanceof Tracked, t instanceof Promise);
                console.log(await t);
            }
            main();
