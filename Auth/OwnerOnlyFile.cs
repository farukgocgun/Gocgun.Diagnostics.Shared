namespace Gocgun.Diagnostics.Shared.Auth;

/// <summary>Writes files the owner alone may read. On Windows the profile ACLs apply and the Unix mode is skipped.</summary>
internal static class OwnerOnlyFile
{
    private const UnixFileMode OwnerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>Opens a file for writing. The Unix mode is 600 from the moment it is created.</summary>
    /// <param name="path">File path.</param>
    /// <param name="mode">Create mode. <see cref="FileMode.CreateNew"/> fails when the file exists.</param>
    /// <returns>The open stream.</returns>
    internal static FileStream Open(string path, FileMode mode)
    {
        var options = new FileStreamOptions { Mode = mode, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerReadWrite;
        }

        var stream = new FileStream(path, options);
        try
        {
            Restrict(path);
        }
        catch
        {
            stream.Dispose();
            throw;
        }

        return stream;
    }

    internal static void WriteAllBytes(string path, byte[] bytes)
    {
        using var stream = Open(path, FileMode.Create);
        stream.Write(bytes);
    }

    internal static void WriteAllText(string path, string text)
    {
        WriteAllBytes(path, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text));
    }

    private static void Restrict(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, OwnerReadWrite);
        }
    }
}
