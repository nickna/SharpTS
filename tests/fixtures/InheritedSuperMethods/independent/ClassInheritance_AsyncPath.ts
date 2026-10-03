class Animal {
                name: string;
                constructor(name: string) {
                    this.name = name;
                }
                speak(): string {
                    return this.name + " makes a sound";
                }
            }
            class Dog extends Animal {
                speak(): string {
                    return this.name + " barks";
                }
            }
            async function test() {
                const dog = new Dog("Rex");
                console.log(dog.speak());
            }
            test();
