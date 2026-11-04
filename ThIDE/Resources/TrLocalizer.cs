// IStringLocalizer<Strings> over Tr, so view models that take an injected localizer read the same
// UI language as {l:Loc} and Tr.Get. The framework's ResourceManagerStringLocalizer resolves against
// the ambient CultureInfo.CurrentUICulture, which flows with the ExecutionContext and can differ
// from the language the user picked on a flow captured before the switch.

using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;

namespace ThIDE.Resources;

public sealed class TrLocalizer : IStringLocalizer<Strings>
{
    public LocalizedString this[string name] =>
        Tr.Find(name) is { } value ? new(name, value) : new(name, name, resourceNotFound: true);

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var s = this[name];
            return new(name, string.Format(CultureInfo.CurrentCulture, s.Value, arguments), s.ResourceNotFound);
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var set = Tr.Manager.GetResourceSet(Tr.Culture, createIfNotExists: true, tryParents: includeParentCultures);
        if (set is null) yield break;
        foreach (System.Collections.DictionaryEntry e in set)
            if (e is { Key: string key, Value: string value }) yield return new(key, value);
    }
}
