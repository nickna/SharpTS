using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class EventsClassStaticIdentityTests
{
    [Theory, ModeData]
    public void ImportedConstructorAndHelpersShareDefaultListenerState(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter as Emitter, setMaxListeners, getMaxListeners } from 'events';
                setMaxListeners(7);
                console.log(Emitter.defaultMaxListeners, getMaxListeners(null));
                Emitter.defaultMaxListeners = 12;
                console.log(Emitter.defaultMaxListeners, getMaxListeners(null));
                setMaxListeners(10);
                """
        };
        Assert.Equal("7 7\n12 12\n", TestHarness.RunModules(files, "main.ts", mode));
    }
}
