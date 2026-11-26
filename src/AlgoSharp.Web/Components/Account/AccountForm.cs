using AlgoSharp.Accounts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace AlgoSharp.Web.Components.Account;

/// <summary>
/// Base for the server-rendered account forms: owns the EditContext so errors returned by
/// <see cref="AccountService"/> show next to the field they belong to.
/// </summary>
public abstract class AccountForm<TModel> : ComponentBase where TModel : class, new()
{
    [CascadingParameter] protected HttpContext HttpContext { get; set; } = default!;
    [Inject] protected AccountService Accounts { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;

    [SupplyParameterFromForm] protected TModel? Input { get; set; }

    protected EditContext Edit { get; private set; } = default!;
    private ValidationMessageStore? store;

    /// <summary>A message for the whole form (not tied to one field).</summary>
    protected string? FormError { get; set; }

    protected override void OnInitialized()
    {
        Input ??= new TModel();
        Edit = new EditContext(Input);
        store = new ValidationMessageStore(Edit);
        // Server-side errors are cleared when the user edits a field (interactive forms only).
        Edit.OnFieldChanged += (_, e) => store.Clear(e.FieldIdentifier);
    }

    /// <summary>
    /// Server-rendered forms re-render with the posted values; passwords must never be written back
    /// into the page, so every password property is cleared before the form is shown again.
    /// </summary>
    protected void ClearSecrets()
    {
        foreach (var prop in typeof(TModel).GetProperties())
            if (prop.PropertyType == typeof(string) && prop.Name.Contains("Password", StringComparison.Ordinal) && prop.CanWrite)
                prop.SetValue(Input, "");
    }

    protected void Show(AccountResult result)
    {
        foreach (var e in result.Errors)
        {
            if (e.Field.Length == 0 || typeof(TModel).GetProperty(e.Field) is null) FormError = e.Message;
            else store!.Add(Edit.Field(e.Field), e.Message);
        }
        Edit.NotifyValidationStateChanged();
    }
}
