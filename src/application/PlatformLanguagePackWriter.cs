using System.IO;

namespace CodexAccountDesk;

internal sealed class PlatformLanguagePackWriter : ILanguagePackWriter
{
    public static readonly PlatformLanguagePackWriter Instance = new();
    public void Write(string directory, string fileName, byte[] bytes)
    {
        SecureFiles.CreatePrivateDirectory(directory);
        SecureFiles.AtomicWrite(Path.Combine(directory, fileName), bytes);
    }
}
