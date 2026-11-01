namespace Gocgun.Diagnostics.Shared.Auth;

/// <summary>Marker user for <c>PasswordHasher</c>. Not an identity-store user.</summary>
internal sealed class DiagnosticOperator
{
    internal string Name { get; } = "local-admin";
}
