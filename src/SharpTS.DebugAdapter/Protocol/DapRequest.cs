using System.Text.Json;

namespace SharpTS.DebugAdapter.Protocol;

internal sealed record DapRequest(int Sequence, string Command, JsonElement Arguments);
