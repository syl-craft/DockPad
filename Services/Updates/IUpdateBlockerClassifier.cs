using System.IO;

namespace DockPad.Services.Updates;

public sealed record UpdateBlockerContext(string Image, string Directory, string? CommandLine,
    string? ParentName, string? ParentPath, string? GrandparentName = null, string? GrandparentPath = null);

public sealed record UpdateBlockerClassification(bool IsRelay, string? Program);

public interface IUpdateBlockerClassifier
{
    UpdateBlockerClassification Classify(UpdateBlockerContext context);
    bool IsIgnored(string? image, string directory);
}

public sealed class ProgramClassifier : IUpdateBlockerClassifier
{
    public UpdateBlockerClassification Classify(UpdateBlockerContext context) =>
        new(false, Path.GetFileNameWithoutExtension(context.Image));
    public bool IsIgnored(string? image, string directory) => false;
}
