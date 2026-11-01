using Gocgun.Diagnostics.Shared.Audit;
using Gocgun.Diagnostics.Shared.Decoding;

namespace Gocgun.Diagnostics.Shared.Hosting;

/// <summary>Loads contract assemblies only below <c>Diagnostics:ContractRoot</c> and audits every attempt.</summary>
internal static class ContractLoader
{
    internal static ContractCatalog Load(string folder, DiagnosticsOptions options, AuditLog audit, string profile)
    {
        string resolved;
        try
        {
            resolved = ContractCatalog.ResolveFolder(folder, options.ContractRoot);
        }
        catch (InvalidOperationException)
        {
            audit.Append(new AuditEntry(DiagnosticsHost.OperatorName, profile, folder.Trim(), null, "contract-load-refused"));
            throw;
        }

        var catalog = ContractCatalog.LoadFolder(resolved, options.ContractRoot);
        audit.Append(new AuditEntry(DiagnosticsHost.OperatorName, profile, resolved, null, "contract-load"));
        return catalog;
    }
}
