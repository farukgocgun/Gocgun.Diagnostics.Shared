namespace Gocgun.Diagnostics.Shared.Audit;

/// <summary>One audit line. There is no payload, password, or value field.</summary>
internal sealed class AuditEntry
{
    internal AuditEntry(string operatorName, string profile, string target, string? contract, string action)
    {
        OperatorName = operatorName;
        Profile = profile;
        Target = target;
        Contract = contract;
        Action = action;
    }

    internal string OperatorName { get; }

    internal string Profile { get; }

    internal string Target { get; }

    internal string? Contract { get; }

    internal string Action { get; }
}
