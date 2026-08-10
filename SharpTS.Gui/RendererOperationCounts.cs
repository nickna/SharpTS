namespace SharpTS.Gui;

internal readonly record struct RendererOperationCounts(
    int Creates,
    int DescriptorUpdates,
    int Removes,
    int Moves);
