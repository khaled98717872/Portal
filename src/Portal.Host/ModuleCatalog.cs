using System.Reflection;
using Portal.Modules.Documents.Infrastructure;
using Portal.Modules.Documents.UI;
using Portal.Modules.Hr.Infrastructure;
using Portal.Modules.Hr.UI;
using Portal.SharedKernel.Modules;

namespace Portal.Host;

public static class ModuleCatalog
{
    public static readonly IReadOnlyList<IModule> Modules =
    [
        new HrModule(),
        new DocumentsModule()
    ];

    public static readonly IReadOnlyDictionary<string, ModuleMenuItem> Menu =
        new Dictionary<string, ModuleMenuItem>(StringComparer.OrdinalIgnoreCase)
        {
            ["Hr"] = new("Human Resources", "hr"),
            ["Documents"] = new("Documents", "documents")
        };

    public static readonly Assembly[] UiAssemblies =
    [
        typeof(HrUi).Assembly,
        typeof(DocumentsUi).Assembly
    ];
}

public sealed record ModuleMenuItem(string Title, string Href);

