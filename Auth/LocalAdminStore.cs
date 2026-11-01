using System.Text;
using Microsoft.AspNetCore.Identity;

namespace Gocgun.Diagnostics.Shared.Auth;

/// <summary>Stores one admin password with <see cref="PasswordHasher{TUser}"/>. Does not reference Gocgun.Identity.</summary>
internal sealed class LocalAdminStore
{
    private readonly PasswordHasher<DiagnosticOperator> hasher = new();
    private readonly Lock gate = new();
    private readonly string path;

    internal LocalAdminStore(string path)
    {
        this.path = path;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    internal bool IsConfigured
    {
        get
        {
            lock (gate)
            {
                return File.Exists(path);
            }
        }
    }

    internal void SetPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Trim().Length < 12)
        {
            throw new ArgumentException("Password must be at least 12 characters.", nameof(password));
        }

        lock (gate)
        {
            var hash = hasher.HashPassword(new DiagnosticOperator(), password);

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(hash);
            FileStream stream;

            // CreateNew closes the check-then-write race: a second caller fails here instead of overwriting.
            try
            {
                stream = OwnerOnlyFile.Open(path, FileMode.CreateNew);
            }
            catch (IOException ex) when (File.Exists(path))
            {
                throw new InvalidOperationException("Password is already set.", ex);
            }

            // A failed write must not leave an empty hash file behind, or setup would stay locked.
            try
            {
                using (stream)
                {
                    stream.Write(bytes);
                }
            }
            catch (IOException)
            {
                File.Delete(path);
                throw;
            }
        }
    }

    internal bool Verify(string password)
    {
        lock (gate)
        {
            if (!File.Exists(path) || string.IsNullOrEmpty(password))
            {
                return false;
            }

            var hash = File.ReadAllText(path).Trim();
            var result = hasher.VerifyHashedPassword(new DiagnosticOperator(), hash, password);
            return result != PasswordVerificationResult.Failed;
        }
    }

    internal string ReadRaw()
    {
        lock (gate)
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
    }
}
